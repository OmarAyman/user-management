using System.Text.Json;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Models;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;

namespace UserManagement.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>A page of audit entries. Admin only.</summary>
public sealed record GetAuditLogsQuery(
    int PageNumber,
    int PageSize,
    string? EntityName,
    string? EntityId,
    AuditAction? Action,
    Guid? PerformedByUserId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? SortBy,
    SortDirection SortDirection);

/// <summary>
/// One audit entry as a client sees it.
/// </summary>
/// <remarks>
/// <see cref="OldValues"/> and <see cref="NewValues"/> are returned as parsed JSON objects rather than escaped
/// strings, so a UI can render a field-by-field diff without re-parsing what the API already parsed.
/// </remarks>
public sealed record AuditLogDto(
    long Id,
    string EntityName,
    string EntityId,
    string? EntityDisplayName,
    string Action,
    Guid? PerformedByUserId,
    string PerformedByUsername,
    DateTimeOffset Timestamp,
    string IpAddress,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? CorrelationId);

/// <summary>
/// Reads the audit trail.
/// </summary>
/// <remarks>
/// Read-only by construction: there is no update or delete anywhere in this feature, and none on the
/// repository either. Combined with an entity that has no public setters and no mutating route, that is what
/// makes the trail append-only through the application (audit policy section 5).
/// </remarks>
public sealed class GetAuditLogsQueryHandler(
    IAuditLogRepository auditLogs,
    IQueryExecutor executor) : IQueryHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "timestamp", "action", "entityName" };

    public async Task<PagedResult<AuditLogDto>> HandleAsync(
        GetAuditLogsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.FromUtc is { } from && query.ToUtc is { } to && from > to)
        {
            throw ValidationException.ForField("fromUtc", "The start of the range must not be after its end.");
        }

        var filtered = ApplyFilters(auditLogs.Query(), query);

        var totalCount = await executor.CountAsync(filtered, cancellationToken);

        if (totalCount == 0)
        {
            return PagedResult<AuditLogDto>.Empty(query.PageNumber, query.PageSize);
        }

        var page = ApplySort(filtered, query.SortBy, query.SortDirection)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize);

        var entries = await executor.ToListAsync(page, cancellationToken);

        return new PagedResult<AuditLogDto>(
            [.. entries.Select(ToDto)],
            query.PageNumber,
            query.PageSize,
            totalCount);
    }

    private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> query, GetAuditLogsQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            var entityName = filter.EntityName.Trim();
            query = query.Where(entry => entry.EntityName == entityName);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            // Filtered by the target's immutable id, never by a username: that is what keeps a user's history
            // unambiguous after their released username has been taken by somebody else (ADR-0009).
            var entityId = filter.EntityId.Trim();
            query = query.Where(entry => entry.EntityId == entityId);
        }

        if (filter.Action is { } action)
        {
            query = query.Where(entry => entry.Action == action);
        }

        if (filter.PerformedByUserId is { } actor)
        {
            query = query.Where(entry => entry.PerformedByUserId == actor);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(entry => entry.Timestamp >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(entry => entry.Timestamp <= to);
        }

        return query;
    }

    private static IQueryable<AuditLog> ApplySort(
        IQueryable<AuditLog> query,
        string? sortBy,
        SortDirection direction)
    {
        var field = string.IsNullOrWhiteSpace(sortBy) ? "timestamp" : sortBy.Trim();

        if (!SortableFields.Contains(field))
        {
            throw BadRequestException.InvalidSortField(field, SortableFields);
        }

        var descending = direction != SortDirection.Ascending;

        var ordered = field.ToLowerInvariant() switch
        {
            "action" => descending
                ? query.OrderByDescending(entry => entry.Action)
                : query.OrderBy(entry => entry.Action),
            "entityname" => descending
                ? query.OrderByDescending(entry => entry.EntityName)
                : query.OrderBy(entry => entry.EntityName),

            // Newest first by default: an audit trail is read from the most recent event backwards.
            _ => descending
                ? query.OrderByDescending(entry => entry.Timestamp)
                : query.OrderBy(entry => entry.Timestamp),
        };

        // The identity key is monotonic, so it is both a stable tiebreaker and a proxy for insertion order
        // when two events share a timestamp.
        return descending ? ordered.ThenByDescending(entry => entry.Id) : ordered.ThenBy(entry => entry.Id);
    }

    private static AuditLogDto ToDto(AuditLog entry) => new(
        entry.Id,
        entry.EntityName,
        entry.EntityId,
        entry.EntityDisplayName,
        entry.Action.ToString(),
        entry.PerformedByUserId,
        entry.PerformedByUsername,
        entry.Timestamp,
        entry.IpAddress,
        ParseJson(entry.OldValues),
        ParseJson(entry.NewValues),
        entry.CorrelationId);

    /// <summary>
    /// Returns stored JSON as JSON.
    /// </summary>
    /// <remarks>
    /// A malformed payload is dropped rather than thrown: the trail is written by an interceptor and read for
    /// investigation, so one unreadable row must not take the whole page down with it. Nothing here can widen
    /// what was stored - redaction happened at write time (audit policy section 4.4).
    /// </remarks>
    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
