using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using Xunit;

namespace SecureLab.Api.Tests;

public sealed class Lab03SecurityTests
    : IClassFixture<SecureLabApiFactory>
{
    private readonly SecureLabApiFactory _factory;

    public Lab03SecurityTests(SecureLabApiFactory factory)
    {
        _factory = factory;
    }

    // Створення HTTP-клієнта зі збереженням cookie
    private HttpClient CreateClient()
    {
        return _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
    }

    // Отримання пароля навчальних користувачів
    private static string GetPassword()
    {
        return Environment.GetEnvironmentVariable(
            "SeedUsers__Password")
            ?? throw new InvalidOperationException(
                "Не задано SeedUsers__Password.");
    }

    // Вхід через ASP.NET Core Identity
    private static async Task LoginAsync(
        HttpClient client,
        string userName)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName,
                password = GetPassword()
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
    }

    // Створення даних тестового інциденту
    private static object NewIncident(string title)
    {
        return new
        {
            title,
            description = "Автоматизована перевірка ЛР3",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
    }

    // Перевірка власника безпосередньо в базі даних
    private async Task AssertIncidentOwnerAsync(
        string title,
        Guid expectedOwnerId)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var incident = await db.Incidents
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Title == title);

        Assert.NotNull(incident);

        Assert.Equal(
            expectedOwnerId,
            incident.OwnerUserId);
    }

    // A-01: неавторизований користувач не може
    // створити інцидент
    [Fact]
    public async Task A01_AnonymousPost_Returns401()
    {
        using var client = CreateClient();

        var title = "A01_" + Guid.NewGuid().ToString("N");

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            NewIncident(title));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    // A-02: після входу доступний профіль користувача
    [Fact]
    public async Task A02_Login_ProfileReturns200()
    {
        using var client = CreateClient();

        await LoginAsync(client, "alice");

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);

        var userId = document.RootElement
            .GetProperty("userId")
            .GetString();

        Assert.Equal(
            DbSeeder.AliceId.ToString(),
            userId);
    }

    // A-03: підроблений X-Demo-UserId
    // не повинен змінювати особу користувача
    [Fact]
    public async Task A03_ForgedHeader_DoesNotChangeProfile()
    {
        using var client = CreateClient();

        await LoginAsync(client, "alice");

        using var original = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.OK,
            original.StatusCode);

        var originalBody =
            await original.Content.ReadAsStringAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/me");

        request.Headers.Add(
            "X-Demo-UserId",
            DbSeeder.BobId.ToString());

        using var forged = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            forged.StatusCode);

        var forgedBody =
            await forged.Content.ReadAsStringAsync();

        using var originalJson =
            JsonDocument.Parse(originalBody);

        using var forgedJson =
            JsonDocument.Parse(forgedBody);

        var originalUserId = originalJson.RootElement
            .GetProperty("userId")
            .GetString();

        var forgedUserId = forgedJson.RootElement
            .GetProperty("userId")
            .GetString();

        Assert.Equal(
            DbSeeder.AliceId.ToString(),
            originalUserId);

        Assert.Equal(
            originalUserId,
            forgedUserId);
    }

    // A-04: Bob створює інцидент,
    // власником повинен бути Bob
    [Fact]
    public async Task A04_BobCreatesIncident_OwnerIsBob()
    {
        using var client = CreateClient();

        await LoginAsync(client, "bob");

        var title = "A04_Bob_" +
            Guid.NewGuid().ToString("N");

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            NewIncident(title));

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        await AssertIncidentOwnerAsync(
            title,
            DbSeeder.BobId);
    }

    // A-05: Bob передає чужий ownerUserId,
    // але власником повинен залишитися Bob
    [Fact]
    public async Task A05_BobCannotAssignAliceAsOwner()
    {
        using var client = CreateClient();

        await LoginAsync(client, "bob");

        var title = "A05_Bob_" +
            Guid.NewGuid().ToString("N");

        var body = new
        {
            title,
            description = "Перевірка підміни власника",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),

            // Навмисно передаємо ID Alice
            ownerUserId = DbSeeder.AliceId
        };

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            body);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        // Перевіряємо, що власник усе одно Bob
        await AssertIncidentOwnerAsync(
            title,
            DbSeeder.BobId);
    }

    // A-06: після logout профіль недоступний
    [Fact]
    public async Task A06_Logout_ProfileReturns401()
    {
        using var client = CreateClient();

        await LoginAsync(client, "alice");

        using var before = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.OK,
            before.StatusCode);

        using var logout = await client.PostAsync(
            "/api/auth/logout",
            null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            logout.StatusCode);

        using var after = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            after.StatusCode);
    }

    // A-07: неправильний пароль і неіснуючий
    // користувач повинні мати однакові відповіді

    [Fact]
    public async Task A07_InvalidLogins_HaveSameResponse()
    {
        using var client = CreateClient();

        // Спроба входу з неправильним паролем
        using var knownUser = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = "IncorrectPassword_123!"
            });

        // Спроба входу з неіснуючим користувачем
        using var unknownUser = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "unknown_" + Guid.NewGuid().ToString("N"),
                password = "IncorrectPassword_123!"
            });

        // Обидві відповіді повинні мати статус 401
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            knownUser.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unknownUser.StatusCode);

        var firstBody = await knownUser.Content.ReadAsStringAsync();
        var secondBody = await unknownUser.Content.ReadAsStringAsync();

        using var firstJson = JsonDocument.Parse(firstBody);
        using var secondJson = JsonDocument.Parse(secondBody);

        // Порівнюємо значущі поля, а не унікальний traceId
        foreach (var propertyName in new[] { "type", "title", "status", "detail" })
        {
            bool firstHasProperty =
                firstJson.RootElement.TryGetProperty(
                    propertyName, out var firstValue);

            bool secondHasProperty =
                secondJson.RootElement.TryGetProperty(
                    propertyName, out var secondValue);

            Assert.Equal(firstHasProperty, secondHasProperty);

            if (firstHasProperty)
            {
                Assert.Equal(
                    firstValue.GetRawText(),
                    secondValue.GetRawText());
            }
        }
    }

}
