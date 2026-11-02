using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SecureLab.Api.Data;
using Xunit;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitingDefenseTests
{
    private readonly ITestOutputHelper _output;

    public LoginRateLimitingDefenseTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static WebApplicationFactory<Program> CreateHost(SecureLabApiFactory root)
    {
        return root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();

                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = (int)HttpStatusCode.TooManyRequests;

                    options.AddPolicy("login-limit", context => RateLimitPartition.GetFixedWindowLimiter(partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown", factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(2),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
                });
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
    {
        return host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private static string GetCookieName(WebApplicationFactory<Program> host)
    {
        return host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie.Name ?? throw new InvalidOperationException("Application cookie name is missing");
    }

    private static bool HasMainCookie(HttpResponseMessage response, string cookieName)
    {
        return response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task D15_RateRejection_And_FreshHostControl()
    {
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password");

        Assert.False(string.IsNullOrWhiteSpace(password), "SetUsers__Password before running the test.");

        using var root = new SecureLabApiFactory();

        using (var limiteHost = CreateHost(root))
        using (var client = CreateClient(limiteHost))
        {
            var cookieName = GetCookieName(limiteHost);

            using var before = await client.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);

            _output.WriteLine("Host A: initial /api/me = 401");

            for (var attempt = 1; attempt <=3; attempt++)
            {
                using var failed = await client.PostAsJsonAsync("/api/auth/login", new
                {
                    userName = "ghost-d15",
                    password = "Wrong123"
                });
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
                Assert.False(HasMainCookie(failed, cookieName));

                _output.WriteLine($"Host A: ghost attempt {attempt} = 401");
            }

            using var rejected = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "bob",
                password
            });
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.False(rejected.IsSuccessStatusCode);

            _output.WriteLine("Host A: Bob login = 429");
            _output.WriteLine("Host A: IsSuccessStatusCode = false");

            var cookieIssued = HasMainCookie(rejected, cookieName);

            Assert.False(cookieIssued);

            _output.WriteLine($"Host A: main Set-Cookie present = {cookieIssued}");
            
            var body = await rejected.Content.ReadAsStringAsync();

            Assert.False(body.Contains("bob", StringComparison.OrdinalIgnoreCase), "429 body must not disclose the submitted account.");

            _output.WriteLine("Host A: 429 body contains Bob = false");

            using var after = await client.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

            _output.WriteLine("Host A: next /api/me = 401");
        }

        using (var freshHost = CreateHost(root))
        using (var client = CreateClient (freshHost))
        {
            var cookieName = GetCookieName(freshHost);

            using var before = await client.GetAsync("/api/me");

            Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);

            _output.WriteLine("Host B: initial /api/me = 401");

            using var login = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "bob",
                password
            });

            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            var cookieIssued = HasMainCookie(login, cookieName);

            Assert.True(cookieIssued);

            _output.WriteLine("Host B: Bob login = 204");
            _output.WriteLine($"Host B: main Set-Cookie present = {cookieIssued}");

            using var me = await client.GetAsync("/api/me");

            var profile = await me.Content.ReadFromJsonAsync<ProfileResponse>();

            Assert.NotNull(profile);
            Assert.Equal(DbSeeder.BobId, profile!.UserId);

            _output.WriteLine("Host B: next /api/me = 200");
            _output.WriteLine("Host B: profile belongs to Bob = true");
        }
    }

    private sealed record ProfileResponse(Guid UserId);
}