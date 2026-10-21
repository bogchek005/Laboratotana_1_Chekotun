using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using System.Diagnostics;

using SecureLab.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents").RequireAuthorization();

        group.MapPut("/{id:guid}", UpdateIncidentAsync).WithName("UpdateIncident");

        group.MapDelete("/{id:guid}", DeleteIncidentAsync).WithName("DeleteIncident");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        group.MapPost("/{id:guid}/triage", TriageIncidentAsync)
            .WithName("TriageIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var commentsGroup = endpoints.MapGroup("/api/incident-comments").WithTags("Incident Comments").RequireAuthorization();

        commentsGroup.MapGet("/{id:guid}", GetIncidentCommentAsync)
            .WithName("GetIncidentComment")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
    string? status,
    IncidentQueries queries,
    HttpContext httpContext,
    CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;

        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(
                    status,
                    ignoreCase: true,
                    out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["status"] =
                        [
                            "Допустимі значення: New, Triaged, InProgress, Resolved, Closed."
                        ]
                    });
            }

            parsedStatus = candidate;
        }

        var userIdValue = httpContext.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var canReadAll =
            httpContext.User.IsInRole("Analyst") ||
            httpContext.User.IsInRole("Administrator");

        var incidents = await queries.GetListAsync(
            parsedStatus,
            currentUserId,
            canReadAll,
            cancellationToken);

        return Results.Ok(incidents);
    }

    private static async Task<IResult> GetDetailsAsync(
    Guid id,
    IncidentQueries queries,
    HttpContext httpContext,
    CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var canReadAll =
            httpContext.User.IsInRole("Analyst") ||
            httpContext.User.IsInRole("Administrator");

        var incident = await queries.GetDetailsAsync(
            id,
            currentUserId,
            canReadAll,
            cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(incident);
    }

    private static async Task<IResult> UpdateIncidentAsync(
    Guid id,
    UpdateIncidentRequest request,
    SecureLabDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var incident = await db.Incidents
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: 404);
        }

        var isAdmin =
            httpContext.User.IsInRole("Administrator");

        if (!isAdmin && incident.OwnerUserId != currentUserId)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: 404);
        }

        if (string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Length > 160)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["title"] = ["Заголовок має містити від 1 до 160 символів."]
                });
        }

        if (string.IsNullOrWhiteSpace(request.Description) ||
            request.Description.Length > 4000)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["description"] = ["Опис має містити від 1 до 4000 символів."]
                });
        }

        incident.Title = request.Title.Trim();
        incident.Description = request.Description.Trim();
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteIncidentAsync(
    Guid id,
    SecureLabDbContext db,
    HttpContext httpContext,
    CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var incident = await db.Incidents
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: 404);
        }

        var isAdmin =
            httpContext.User.IsInRole("Administrator");

        if (!isAdmin && incident.OwnerUserId != currentUserId)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: 404);
        }

        db.Incidents.Remove(incident);

        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> TriageIncidentAsync(
    Guid id,
    HttpContext httpContext,
    SecureLabDbContext dbContext,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("IncidentAuthorization");
        // 1. Отримуємо користувача з перевіреного principal.
        var userIdValue = httpContext.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        // 2. Завантажуємо ресурс із відстеженням EF Core.
        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        // 3. Перевіряємо існування ресурсу.
        if (incident is null)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        // 4. Визначаємо роль лише за principal.
        var isAnalyst = httpContext.User.IsInRole("Analyst");
        var isAdministrator =
            httpContext.User.IsInRole("Administrator");

        var hasPrivilegedRole = isAnalyst || isAdministrator;

        // 5. Для Reporter чужий ресурс приховуємо.
        if (!hasPrivilegedRole &&
            incident.OwnerUserId != currentUserId)
        {
            return Results.Problem(
                title: "Інцидент не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        // 6. Перевіряємо право виконання triage.
        if (!hasPrivilegedRole)
        {
            var traceId = Activity.Current?.Id
                ?? httpContext.TraceIdentifier;

            logger.LogWarning(
                "authorization.denied SubjectId={SubjectId} " +
                "Action={Action} ResourceId={ResourceId} " +
                "Result={Result} OccurredAtUtc={OccurredAtUtc} " +
                "TraceId={TraceId}",
                currentUserId,
                "incident.triage",
                id,
                "denied",
                DateTimeOffset.UtcNow,
                traceId);

            return Results.Problem(
                title: "Недостатньо прав для виконання triage",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = traceId
                });
        }

        // 7. Перевіряємо допустимість переходу.
        if (incident.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Недопустимий перехід стану інциденту",
                statusCode: StatusCodes.Status409Conflict);
        }

        // 8. Сервер сам визначає наступний стан.
        var previousStatus = incident.Status;
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // 9. Записуємо історію зміни стану.
        dbContext.IncidentStatusHistory.Add(
            new IncidentStatusHistory
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                ChangedByUserId = currentUserId,
                OldStatus = previousStatus,
                NewStatus = IncidentStatus.Triaged,
                Note = "Triage виконано уповноваженим користувачем.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        // 10. Зберігаємо зміну.
        await dbContext.SaveChangesAsync(cancellationToken);

        // 11. Повертаємо успішний результат.
        return Results.NoContent();
    }

    private static async Task<IResult> GetIncidentCommentAsync(
    Guid id,
    HttpContext httpContext,
    SecureLabDbContext dbContext,
    CancellationToken cancellationToken)
    {
        var userIdValue = httpContext.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out var currentUserId))
        {
            return Results.Unauthorized();
        }

        var isAnalyst = httpContext.User.IsInRole("Analyst");
        var isAdministrator =
            httpContext.User.IsInRole("Administrator");

        var canReadAll = isAnalyst || isAdministrator;

        // Перевірка доступу до батьківського інциденту
        // виконується безпосередньо в запиті до БД.
        var comment = await dbContext.IncidentComments
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Where(c =>
                canReadAll ||
                c.Incident.OwnerUserId == currentUserId)
            .Where(c =>
                !c.IsInternal || canReadAll)
            .Select(c => new
            {
                c.Id,
                c.IncidentId,
                c.Text,
                c.IsInternal,
                c.CreatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (comment is null)
        {
            return Results.Problem(
                title: "Коментар не знайдено",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(comment);
    }
}
public sealed record UpdateIncidentRequest(
    string? Title,
    string? Description);