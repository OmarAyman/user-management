using UserManagement.Application.Common.Models;
using UserManagement.Domain.Enums;

namespace UserManagement.Api.Contracts.Audit;

/// <summary>
/// Filters for the audit listing.
/// </summary>
/// <remarks>
/// <see cref="EntityId"/> is the target's immutable id. There is no username filter on purpose: usernames are
/// released when a user is deleted, so filtering a history by name could mix two different people (ADR-0009).
/// </remarks>
public sealed record AuditLogQueryParameters
{
    public int PageNumber { get; init; } = PagingDefaults.DefaultPageNumber;

    public int PageSize { get; init; } = PagingDefaults.DefaultPageSize;

    public string? EntityName { get; init; }

    public string? EntityId { get; init; }

    public AuditAction? Action { get; init; }

    public Guid? PerformedByUserId { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public string? SortBy { get; init; }

    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
}
