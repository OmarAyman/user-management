using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Features.Users.GetUsers;

/// <summary>
/// A page of users. There is deliberately no "include deleted" flag: soft-delete visibility is an
/// authorization decision served by a separate Admin-only route, not a value a client passes (ADR-0004).
/// </summary>
public sealed record GetUsersQuery(
    int PageNumber,
    int PageSize,
    string? Search,
    int? RoleId,
    string? SortBy,
    SortDirection SortDirection);

/// <summary>
/// Lists active users with search, role filter, sorting and paging.
/// </summary>
/// <remarks>
/// <para>
/// Everything happens in SQL: filter, sort, count, then <c>OFFSET/FETCH</c>. Nothing is materialised before
/// paging, so the cost of serving a page does not grow with the size of the table.
/// </para>
/// <para>
/// The count is a second query over the same filtered queryable. Two cheap round trips beat one heavier
/// windowed query at these page sizes, and EF cannot batch a window function with <c>OFFSET/FETCH</c> safely.
/// </para>
/// </remarks>
public sealed class GetUsersQueryHandler(
    IUserRepository users,
    IRoleRepository roles,
    IQueryExecutor executor,
    IDateTimeProvider clock) : IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    public async Task<PagedResult<UserListItemDto>> HandleAsync(
        GetUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.RoleId is { } roleId && !await roles.ExistsAsync(roleId, cancellationToken))
        {
            // A filter on a role that does not exist is a client mistake, not an empty result: returning an
            // empty page would let a typo look like "no users hold this role".
            throw ValidationException.ForKey("roleId", MessageKeys.RoleNotFound, roleId);
        }

        return await users.QueryActive()
            .ApplyFilters(query.Search, query.RoleId)
            .ToPageAsync(query, executor, clock.UtcNow, cancellationToken);
    }
}

/// <summary>Filters and paging shared by the active and deleted listings, so the two cannot drift apart.</summary>
public static class UserQueryComposition
{
    public static IQueryable<User> ApplyFilters(this IQueryable<User> query, string? search, int? roleId)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (roleId is { } role)
        {
            query = query.Where(user => user.RoleId == role);
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var term = search.Trim();

        // Four parameterised LIKE '%term%' predicates once translated. A leading wildcard cannot seek, so this
        // is a covering index scan - the documented trade-off, with full-text search as the scaling path. The
        // term is a parameter either way, so this is a performance note and not an injection one.
        return query.Where(user =>
            user.Username.Contains(term)
            || user.Email.Contains(term)
            || user.FirstName.Contains(term)
            || user.LastName.Contains(term));
    }

    public static async Task<PagedResult<UserListItemDto>> ToPageAsync(
        this IQueryable<User> filtered,
        GetUsersQuery query,
        IQueryExecutor executor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtered);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(executor);

        var totalCount = await executor.CountAsync(filtered, cancellationToken);

        if (totalCount == 0)
        {
            return PagedResult<UserListItemDto>.Empty(query.PageNumber, query.PageSize);
        }

        // Sort, page, then project. The order is not cosmetic: EF cannot translate an OrderBy over a
        // constructor-projected record, and sorting on the entity keeps the ORDER BY on indexed columns.
        var page = filtered
            .ApplySort(query.SortBy, query.SortDirection)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(UserProjections.ToListItem(now));

        var items = await executor.ToListAsync(page, cancellationToken);

        // A page past the end is an empty page with honest metadata, not a 404: the client was not wrong to
        // ask, and the metadata tells it where the data actually ends.
        return new PagedResult<UserListItemDto>(items, query.PageNumber, query.PageSize, totalCount);
    }
}


