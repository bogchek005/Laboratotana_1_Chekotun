using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;
using SecureLab.Api.Scaffolding;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login-limit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("search-limit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst(
                        System.Security.Claims.ClaimTypes.NameIdentifier
                      )?.Value ?? "unknown-user"
                    : context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

});
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' не налаштовано.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IncidentQueries>();

builder.AddLab03Identity();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Lab05BrowserProbe", policy =>
        {
            policy.WithOrigins("http://localhost:4173")
                  .WithMethods("GET")
                  .WithHeaders("X-Lab-Cors-Probe")
                  .AllowCredentials();
        });
    });
}

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "SecureLab.Antiforgery";
    options.Cookie.HttpOnly = true;
});

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);
await DatabaseBootstrap.InitializeAsync(app.Services, app.Configuration, app.Environment, resetRequested);

if (resetRequested)
{
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

var csp =
    "default-src 'self'; " +
    "script-src 'self'; " +
    "style-src 'self'; " +
    "connect-src 'self'; " +
    "img-src 'self'; " +
    "object-src 'none'; " +
    "base-uri 'self'; " +
    "frame-ancestors 'none'";

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";

    var isDevelopmentApiReference =
        app.Environment.IsDevelopment()
        && context.Request.Path.StartsWithSegments("/scalar");

    if (!isDevelopmentApiReference)
    {
        context.Response.Headers["Content-Security-Policy"] = csp;
    }

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseCors("Lab05BrowserProbe");
}

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (SecureLabDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            title: "База даних недоступна",
            statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();
app.MapLab03Identity();
app.MapLab02Endpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
