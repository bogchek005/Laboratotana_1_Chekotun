using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
    {
        var pattern = "%" + EscapeLike(q ?? "") + "%";

        var found = db.Incidents
            .AsNoTracking()
            .Where(item =>
                EF.Functions.ILike(item.Title, pattern, "\\")
                || EF.Functions.ILike(item.Description, pattern, "\\"));

        IQueryable<Incident> ordered = sortBy switch
        {
            null or "" or "createdAtUtc" =>
                found
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ThenBy(item => item.Id),

            "severity" =>
                found
                    .OrderBy(item =>
                        item.Severity == IncidentSeverity.Critical ? 0 :
                        item.Severity == IncidentSeverity.High ? 1 :
                        item.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenBy(item => item.Id),

            "status" =>
                found
                    .OrderBy(item =>
                        item.Status == IncidentStatus.New ? 0 :
                        item.Status == IncidentStatus.Triaged ? 1 :
                        item.Status == IncidentStatus.InProgress ? 2 :
                        item.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenBy(item => item.Id),

            _ => null!
        };

        if (sortBy is not null
            && sortBy != ""
            && sortBy != "createdAtUtc"
            && sortBy != "severity"
            && sortBy != "status")
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["sortBy"] =
                    [
                        "Допустимі значення: createdAtUtc, severity, status."
                    ]
                });
        }

        var items = await ordered
            .Take(50)
            .Select(item => new IncidentListItemResponse(
                item.Id,
                item.Title,
                item.Severity.ToString(),
                item.Status.ToString(),
                item.OccurredAtUtc,
                item.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(items);});
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                errors["title"] = ["Заголовок є обов'язковим."];
            }
            else if (request.Title.Length > 160)
            {
                errors["title"] = ["Заголовок не повинен перевищувати 160 символів."];
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                errors["description"] = ["Опис є обов'язковим."];
            }
            else if (request.Description.Length > 4000)
            {
                errors["description"] = ["Опис не повинен перевищувати 4000 символів."];
            }

            var severityIsValid =
                Enum.TryParse<IncidentSeverity>(
                    request.Severity,
                    ignoreCase: true,
                    out var severity)
                && Enum.IsDefined(severity);

            if (!severityIsValid)
            {
                errors["severity"] =
                [
                    "Допустимі значення: Low, Medium, High, Critical."
                ];
            }

            if (request.OccurredAtUtc is null)
            {
                errors["occurredAtUtc"] =
                [
                    "Дата та час виникнення інциденту є обов'язковими."
                ];
            }
            else if (request.OccurredAtUtc > now.AddMinutes(5))
            {
                errors["occurredAtUtc"] =
                [
                    "Дата та час виникнення інциденту не можуть перевищувати поточний час більше ніж на 5 хвилин."
                ];
            }

            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            if (severityIsValid
                && (severity == IncidentSeverity.High
                    || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description")
                && description.Length < 40)
            {
                errors["description"] =
                [
                    "Для рівнів High або Critical опис повинен містити щонайменше 40 символів."
                ];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            // 6. Перевірка предметного конфлікту — T-03
            var duplicateExists = await db.Incidents.AnyAsync(
                item =>
                    item.Title == title
                    && item.Status != IncidentStatus.Closed,
                ct);

            if (duplicateExists)
            {
                return Results.Problem(
                    title: "Інцидент уже існує",
                    detail: "Активний інцидент із таким самим заголовком уже існує.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // 7. Нормалізація дати та часу до UTC
            var occurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();

            // 8. Створення сутності інциденту із серверними значеннями
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = occurredAtUtc,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            // 9. Формування окремого DTO для відповіді клієнту
            var response = new CreatedIncidentResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc);

            return Results.Created(
                $"/api/incidents/{incident.Id}",
                response);
        });
    }
    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
