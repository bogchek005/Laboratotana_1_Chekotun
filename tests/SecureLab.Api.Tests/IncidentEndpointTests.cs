
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;
using Xunit;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // Допоміжний метод входу через Identity
    private async Task LoginAsync(string userName = "alice")
    {
        var password = Environment.GetEnvironmentVariable(
            "SeedUsers__Password")
            ?? throw new InvalidOperationException(
                "Set SeedUsers__Password before running tests.");

        using var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName,
                password
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    [Fact]
    public async Task GetList_ReturnsOnlyAliceIncidents()
    {
        await LoginAsync("alice");

        var incidents = await _client.GetFromJsonAsync<
            List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);

        Assert.Contains(incidents!,
            incident => incident.Id == DbSeeder.AliceIncidentId);

        Assert.DoesNotContain(incidents!,
            incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        await LoginAsync("alice");
        using var response = await _client.GetAsync(
            $"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        await LoginAsync("alice");
        using var response = await _client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.False(
            document.RootElement.TryGetProperty(
                "ownerUserId", out _));

        Assert.False(
            document.RootElement.TryGetProperty(
                "email", out _));

        Assert.Equal(
            "Аліса Коваль",
            document.RootElement
                .GetProperty("ownerDisplayName")
                .GetString());
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain(
            "innerHTML",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "textContent",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_InvalidDto_ReturnsValidationProblem()
    {
        // НОВЕ: спочатку виконуємо вхід
        await LoginAsync();

        await _client.UseAntiforgeryTokenAsync(
            "/api/auth/csrf-token",
            "X-CSRF-TOKEN");

        var body = new
        {
            title = " ",
            description = "",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        using var response = await _client.PostAsJsonAsync(
            "/api/incidents",
            body);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"title\"", json);
        Assert.Contains("\"description\"", json);
        Assert.Contains("\"severity\"", json);
        Assert.Contains("\"occurredAtUtc\"", json);

        Assert.DoesNotContain(
            "stack trace",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "SELECT",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_DuplicateActiveTitle_ReturnsConflict()
    {
        // Авторизація під навчальним користувачем.
        await LoginAsync();

        await _client.UseAntiforgeryTokenAsync(
            "/api/auth/csrf-token",
            "X-CSRF-TOKEN");

        // Унікальна назва, щоб тест не залежав від seed.
        var title = "LR04 Duplicate Test " + Guid.NewGuid();

        var body = new
        {
            title,
            description = "Перевірка правила унікальності активних інцидентів.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        // 1. Створюємо перший інцидент.
        using var firstResponse = await _client.PostAsJsonAsync(
            "/api/incidents",
            body);

        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);

        // 2. Повторюємо POST з тією самою назвою.
        using var duplicateResponse = await _client.PostAsJsonAsync(
            "/api/incidents",
            body);

        // 3. Другий запит повинен отримати 409 Conflict.
        Assert.Equal(
            HttpStatusCode.Conflict,
            duplicateResponse.StatusCode);

        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType);

        var json = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Contains(
            "уже існує",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "SELECT",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "connection",
            json,
            StringComparison.OrdinalIgnoreCase);
    }
}
