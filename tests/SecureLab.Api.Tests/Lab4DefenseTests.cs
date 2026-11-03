using System;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using Xunit;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class TriageReplayDefenseTests : IClassFixture<SecureLabApiFactory>
{
    private readonly SecureLabApiFactory factory;
    private readonly ITestOutputHelper output;

    private static readonly Guid FixtureId = Guid.Parse("91000000-0000-0000-0000-000000000041");

    private static string SeedPassword => Environment.GetEnvironmentVariable("SeedUsers__Password") ?? throw new InvalidOperationException("Set SeedUsers__Password before running tests.");

    public TriageReplayDefenseTests(SecureLabApiFactory factory, ITestOutputHelper output)
    {
        this.factory = factory;
        this.output = output;
    }

    [Fact]
    public async Task Prepare_AliceFixture()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        Assert.False(await db.Incidents.AnyAsync(x => x.Id == FixtureId), "Fixture already exists. Use a new FixtureId for a new run.");

        var now = DateTimeOffset.UtcNow;

        db.Incidents.Add(new Incident
        {
            Id = FixtureId,
            OwnerUserId = DbSeeder.AliceId,
            Title = "LR04 replay fixture " + FixtureId,
            Description = "Independent Alice fixture for sequential triage.",
            Severity = IncidentSeverity.Low,
            Status = IncidentStatus.New,
            OccurredAtUtc = now.AddMinutes(-10),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync();

        var before = await ReadStateAsync();

        Assert.Equal(DbSeeder.AliceId, before.Owner);
        Assert.Equal(IncidentStatus.New, before.Status);
        Assert.Equal(0, before.History);

        output.WriteLine($"PREPARED: id={FixtureId}; " + $"owner={before.Owner}; " + $"status={before.Status}; history={before.History}");
    }

    [Fact]
    public async Task Morgan_RepeatedTriage_SameFixture()
    {
        output.WriteLine("Prediction: Morgan1 = 204; Morgan2 = 409" + "after each: Triaged; history = 1" + "Bob = 404 in both states");

        using var morgan = await factory.LoginAsync("morgan", SeedPassword);

        using var bob = await factory.LoginAsync("bob", SeedPassword);

        var url = $"/api/incidents/{FixtureId}/triage";

        var initial = await ReadStateAsync();

        output.WriteLine($"Initial: Id={FixtureId}; owner={initial.Owner};" + $"status={initial.Status};history={initial.History}");

        Assert.Equal(DbSeeder.AliceId, initial.Owner);
        Assert.Equal(IncidentStatus.New, initial.Status);
        Assert.Equal(0, initial.History);

        using var bobNew = await bob.PostAsync(url, null);

        output.WriteLine($"Bob / New: Http {(int)bobNew.StatusCode}");

        Assert.Equal(HttpStatusCode.NotFound, bobNew.StatusCode);

        var afterbobNew = await ReadStateAsync();

        Assert.Equal(IncidentStatus.New, afterbobNew.Status);
        Assert.Equal(0, afterbobNew.History);

        using var first = await morgan.PostAsync(url, null);

        output.WriteLine($"Morgan1: Http {(int)first.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var afterfirst = await ReadStateAsync();

        output.WriteLine($"State1: status = {afterfirst.Status};" + $"History={afterfirst.History}");

        Assert.Equal(DbSeeder.AliceId, afterfirst.Owner);
        Assert.Equal(IncidentStatus.Triaged, afterfirst.Status);
        Assert.Equal(1, afterfirst.History);

        await AssertSingleTransitionAsync();

        using var second = await morgan.PostAsync(url, null);

        output.WriteLine($"Morgan2: Http {(int)second.StatusCode}");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var aftersecond = await ReadStateAsync();

        output.WriteLine($"State2: status = {aftersecond.Status};" + $"history = {aftersecond.History};");

        Assert.Equal(DbSeeder.AliceId, aftersecond.Owner);
        Assert.Equal(IncidentStatus.Triaged, aftersecond.Status);

        Assert.Equal(afterfirst.History, aftersecond.History);
        Assert.Equal(1, aftersecond.History);

        await AssertSingleTransitionAsync();

        using var bobtriaged = await bob.PostAsync(url, null);

        output.WriteLine($"Bob / Triaged: Http {(int)bobtriaged.StatusCode}");

        Assert.Equal(HttpStatusCode.NotFound, bobtriaged.StatusCode);

        var final = await ReadStateAsync();

        Assert.Equal(IncidentStatus.Triaged, final.Status);
        Assert.Equal(1, final.History);

        output.WriteLine("Verified: one New -> Triaged by Morgan;" + "repeat rejected; state, history preserved");
    }

    private async Task<(Guid Owner, IncidentStatus Status, int History)> ReadStateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents.AsNoTracking().SingleAsync(x => x.Id == FixtureId);

        var historyCount = await db.IncidentStatusHistory.AsNoTracking().CountAsync(x => x.IncidentId == FixtureId);

        return (incident.OwnerUserId,incident.Status,historyCount);
    }

    private async Task AssertSingleTransitionAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();

        var rows = await db.IncidentStatusHistory.AsNoTracking().Where(x => x.IncidentId == FixtureId).ToListAsync();

        var transition = Assert.Single(rows);

        Assert.Equal(IncidentStatus.New, transition.OldStatus);
        Assert.Equal(IncidentStatus.Triaged, transition.NewStatus);
        Assert.Equal(DbSeeder.MorganId, transition.ChangedByUserId);
    }
}