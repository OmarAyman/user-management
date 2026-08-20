using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Application.Features.Users.GetUsers;

namespace UserManagement.Application.Features.Users.GetDeletedUsers;

/// <summary>A page of soft-deleted users. Admin only.</summary>
public sealed record GetDeletedUsersQuery(
    int PageNumber,
    int PageSize,
    string? Search,
    int? RoleId,
    string? SortBy,
    SortDirection SortDirection);

/// <summary>
/// Lists soft-deleted users.
/// </summary>
/// <remarks>
/// The only read path over deleted rows, reached exclusively through <c>GET /api/users/deleted</c> behind the
/// Admin policy. Deleted rows hold the personal data of people who were removed from the system, so who may
/// read them is an authorization question - which is why this is a separate route with its own policy rather
/// than a boolean on the main listing (ADR-0004).
/// </remarks>
public sealed class GetDeletedUsersQueryHandler(
    IUserRepository users,
    IQueryExecutor executor,
    IDateTimeProvider clock) : IQueryHandler<GetDeletedUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<PagedResult<UserListItemDto>> HandleAsync(
        GetDeletedUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = users.QueryIncludingDeleted()
            .Where(user => user.IsDeleted)
            .ApplyFilters(query.Search, query.RoleId);

        // Reuses the active listing's paging and sorting, so the two endpoints cannot disagree about what
        // page 2 means or how a sort field is spelled.
        return await filtered.ToPageAsync(
            new GetUsersQuery(
                query.PageNumber,
                query.PageSize,
                query.Search,
                query.RoleId,
                query.SortBy,
                query.SortDirection),
            executor,
            clock.UtcNow,
            cancellationToken);
    }
}
