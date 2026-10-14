using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {

        app.MapPost("/api/auth/register", async (
            RegistrationInput input,
            UserManager<ApplicationUser> users) =>
        {
            var errors = new Dictionary<string, string[]>();

            var userName = input.UserName?.Trim() ?? "";
            var email = input.Email?.Trim() ?? "";
            var displayName = input.DisplayName?.Trim() ?? "";

            if (!Regex.IsMatch(userName, @"^[a-zA-Z0-9._-]{3,32}$"))
                errors["userName"] =
                    ["Ім'я має містити 3–32 дозволені символи."];

            if (email.Length > 254 ||
                !new EmailAddressAttribute().IsValid(email))
                errors["email"] = ["Некоректна адреса email."];

            if (displayName.Length < 2 || displayName.Length > 100)
                errors["displayName"] =
                    ["Ім'я профілю має містити 2–100 символів."];

            if (string.IsNullOrEmpty(input.Password))
                errors["password"] = ["Пароль є обов'язковим."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                Email = email,
                DisplayName = displayName
            };

            var result = await users.CreateAsync(user, input.Password!);

            if (!result.Succeeded)
            {
                var duplicate = result.Errors.Any(e =>
                    e.Code == "DuplicateUserName" ||
                    e.Code == "DuplicateEmail");

                if (duplicate)
                    return Results.Problem(
                        title: "Обліковий запис уже існує",
                        statusCode: StatusCodes.Status409Conflict);

                return Results.Problem(
                    title: "Реєстрацію відхилено",
                    detail: "Перевірте коректність облікових даних.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var roleResult = await users.AddToRoleAsync(
                user, DbSeeder.ReporterRole);

            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    "Default role assignment failed.");

            return Results.Created("/api/me",
                new RegistrationResponse(
                    user.Id,
                    user.UserName!,
                    user.DisplayName));
        });


        app.MapPost("/api/auth/login", async (LoginInput input,SignInManager<ApplicationUser> signIn, ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) ||
                string.IsNullOrWhiteSpace(input.Password))
            {
                logger.LogWarning(
                    "Authentication failed: empty credentials.");

                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(
                input.UserName,
                input.Password,
                isPersistent: false,
                lockoutOnFailure: false);

            if (result.Succeeded)
            {
                logger.LogInformation(
                    "Authentication succeeded for user {UserName}",
                    input.UserName);

                return Results.NoContent();
            }

            logger.LogWarning(
                "Authentication failed for user {UserName}",
                input.UserName);

            return Results.Unauthorized();
        }).RequireRateLimiting("login-limit");



        app.MapPost("/api/auth/logout", async (
            SignInManager<ApplicationUser> signIn,
            HttpContext context,
            ILogger<Program> logger) =>
        {
            var userId = context.User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            await signIn.SignOutAsync();

            logger.LogInformation(
                "Authentication logout completed for user ID {UserId}",
                userId);

            return Results.NoContent();
        })
        .RequireAuthorization();



        app.MapGet("/api/me", (HttpContext context) =>
        {
            var idValue = context.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(idValue, out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var userName = context.User
                .FindFirst(ClaimTypes.Name)?.Value ?? "";

            var role = context.User
                .FindFirst(ClaimTypes.Role)?.Value ?? "";

            return Results.Ok(new
            {
                UserId = userId,
                UserName = userName,
                Role = role
            });
        }).RequireAuthorization();

        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id,string UserName,string DisplayName);
