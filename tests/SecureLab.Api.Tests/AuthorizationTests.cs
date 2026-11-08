using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using Xunit;

namespace SecureLab.Api.Tests;

public sealed class AuthorizationTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string SeedPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set SeedUsers__Password before running tests.");

    // A-02: Reporter бачить у списку лише власні інциденти.
    [Fact]
    public async Task A02_AliceList_ContainsOnlyOwnIncidents()
    {
        using var alice =
            await factory.LoginAsync("alice", SeedPassword);

        using var response =
            await alice.GetAsync("/api/incidents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content
            .ReadFromJsonAsync<JsonArray>();

        Assert.NotNull(items);

        Assert.Contains(items!, item =>
            item?["id"]?.GetValue<Guid>()
                == DbSeeder.AliceIncidentId);

        Assert.DoesNotContain(items!, item =>
            item?["id"]?.GetValue<Guid>()
                == DbSeeder.BobIncidentId);
    }

    // A-03: Reporter не може прочитати чужий інцидент.
    // Відповідь повинна бути еквівалентна неіснуючому ID.
    [Fact]
    public async Task A03_AliceCannotReadBobIncident()
    {
        using var alice =
            await factory.LoginAsync("alice", SeedPassword);

        using var foreign = await alice.GetAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        using var unknown = await alice.GetAsync(
            $"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound,
            foreign.StatusCode);

        Assert.Equal(unknown.StatusCode,
            foreign.StatusCode);

        Assert.Equal(
            unknown.Content.Headers.ContentType?.MediaType,
            foreign.Content.Headers.ContentType?.MediaType);

        var foreignBody = JsonNode.Parse(
            await foreign.Content.ReadAsStringAsync());

        var unknownBody = JsonNode.Parse(
            await unknown.Content.ReadAsStringAsync());

        Assert.NotNull(foreignBody);
        Assert.NotNull(unknownBody);

        // Ідентифікатори окремих запитів можуть відрізнятися.
        RemoveTraceFields(foreignBody!);
        RemoveTraceFields(unknownBody!);

        Assert.True(
            JsonNode.DeepEquals(foreignBody, unknownBody),
            "Foreign and unknown resources must have equivalent responses.");

        var body = await foreign.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            "Невідома спроба входу",
            body,
            StringComparison.Ordinal);
    }

    // A-06: Reporter не може виконати triage навіть
    // над власним інцидентом, стан не змінюється.
    [Fact]
    public async Task A06_ReporterTriageDenied_StateUnchanged()
    {
        using var alice =
            await factory.LoginAsync("alice", SeedPassword);

        await alice.UseAntiforgeryTokenAsync(
            "/api/auth/csrf-token",
            "X-CSRF-TOKEN");

        var url =
            $"/api/incidents/{DbSeeder.SafeTextIncidentId}";

        var before = await alice.GetFromJsonAsync<JsonObject>(url);

        Assert.NotNull(before);

        var beforeStatus =
            before!["status"]!.GetValue<string>();

        using var response =
            await alice.PostAsync(url + "/triage", null);

        Assert.Equal(HttpStatusCode.Forbidden,
            response.StatusCode);

        var after = await alice.GetFromJsonAsync<JsonObject>(url);

        Assert.NotNull(after);

        Assert.Equal(
            beforeStatus,
            after!["status"]!.GetValue<string>());
    }

    // A-07: Analyst може перевести New -> Triaged.
    // Для цього тест створює окремий інцидент.
    [Fact]
    public async Task A07_AnalystCanTriageNewIncident()
    {
        using var alice =
            await factory.LoginAsync("alice", SeedPassword);

        await alice.UseAntiforgeryTokenAsync(
            "/api/auth/csrf-token",
            "X-CSRF-TOKEN");

        using var morgan =
            await factory.LoginAsync("morgan", SeedPassword);

        await morgan.UseAntiforgeryTokenAsync(
            "/api/auth/csrf-token",
            "X-CSRF-TOKEN");

        Guid createdId = Guid.Empty;

        try
        {
            var request = new
            {
                title = "LR04 authorization test " + Guid.NewGuid(),
                description = "Temporary incident for A-07.",
                severity = "Low",
                occurredAtUtc =
                    DateTimeOffset.UtcNow.AddMinutes(-10)
            };

            using var created =
                await alice.PostAsJsonAsync(
                    "/api/incidents", request);

            Assert.Equal(
                HttpStatusCode.Created,
                created.StatusCode);

            var createdBody = await created.Content
                .ReadFromJsonAsync<JsonObject>();

            Assert.NotNull(createdBody);

            createdId =
                createdBody!["id"]!.GetValue<Guid>();

            var url = $"/api/incidents/{createdId}";

            var before =
                await morgan.GetFromJsonAsync<JsonObject>(url);

            Assert.NotNull(before);

            Assert.Equal(
                "New",
                before!["status"]!.GetValue<string>());

            using var triage =
                await morgan.PostAsync(url + "/triage", null);

            Assert.Equal(
                HttpStatusCode.NoContent,
                triage.StatusCode);

            var after =
                await morgan.GetFromJsonAsync<JsonObject>(url);

            Assert.NotNull(after);

            Assert.Equal(
                "Triaged",
                after!["status"]!.GetValue<string>());
        }
        finally
        {
            // Прибираємо тільки створений цим тестом ресурс.
            if (createdId != Guid.Empty)
            {
                await using var scope =
                    factory.Services.CreateAsyncScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<SecureLabDbContext>();

                var incident = await db.Incidents
                    .FindAsync(createdId);

                if (incident is not null)
                {
                    db.Incidents.Remove(incident);
                    await db.SaveChangesAsync();
                }
            }
        }
    }

    private static void RemoveTraceFields(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("traceId");
            obj.Remove("TraceId");

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    RemoveTraceFields(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    RemoveTraceFields(item);
                }
            }
        }
    }
}
