using UserManagement.Application.Common.Exceptions;
using UserManagement.Application.Common.Models;
using UserManagement.Application.Features.Users.Dtos;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Features.Users.GetUsers;

/// <summary>
/// The sort whitelist.
/// </summary>
/// <remarks>
/// <para>
/// Client input selects a branch; it never becomes part of a query string. That is the whole defence against
/// sort-field injection, and it is why this is a switch over known keys rather than a dictionary of column
/// names or an expression built from text.
/// </para>
/// <para>
/// Every ordering carries <c>Id</c> as a final tiebreaker. Without it, rows sharing a sort value can appear on
/// two consecutive pages or on none at all - a paging bug that only shows up once real data has duplicates.
/// </para>
/// </remarks>
public static class UserQuerySorting
{
    public const string DefaultField = "createdAt";

    /// <summary>Documented in the API contract and rendered as an enum in the OpenAPI schema.</summary>
    public static readonly IReadOnlySet<string> AllowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "username",
        "email",
        "firstName",
        "lastName",
        "role",
        "createdAt",
    };

    /// <summary>
    /// Orders the query.
    /// </summary>
    /// <remarks>
    /// Applied to the entity, before the projection, for a concrete reason: EF Core cannot translate an
    /// <c>OrderBy</c> over a constructor-projected record - it attempts to build the DTO inside the ORDER BY
    /// clause and gives up. Sorting first also produces the better SQL, because the ORDER BY lands on indexed
    /// columns instead of on projected expressions.
    /// </remarks>
    public static IQueryable<User> ApplySort(
        this IQueryable<User> query,
        string? sortBy,
        SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(query);

        var field = string.IsNullOrWhiteSpace(sortBy) ? DefaultField : sortBy.Trim();

        if (!AllowedFields.Contains(field))
        {
            // Refused rather than quietly ignored: silently sorting by something else would make a client
            // believe a sort applied when it did not.
            throw BadRequestException.InvalidSortField(field, AllowedFields);
        }

        var descending = direction == SortDirection.Descending;

        var ordered = field.ToLowerInvariant() switch
        {
            "username" => descending
                ? query.OrderByDescending(user => user.Username)
                : query.OrderBy(user => user.Username),
            "email" => descending
                ? query.OrderByDescending(user => user.Email)
                : query.OrderBy(user => user.Email),
            "firstname" => descending
                ? query.OrderByDescending(user => user.FirstName)
                : query.OrderBy(user => user.FirstName),
            "lastname" => descending
                ? query.OrderByDescending(user => user.LastName)
                : query.OrderBy(user => user.LastName),
            // Sorts by the role's name rather than its id, so "Admin, ReadOnlyUser, User" is alphabetical as a
            // reader expects, not seed-order as the database stores it.
            "role" => descending
                ? query.OrderByDescending(user => user.Role!.Name)
                : query.OrderBy(user => user.Role!.Name),
            _ => descending
                ? query.OrderByDescending(user => user.CreatedAt)
                : query.OrderBy(user => user.CreatedAt),
        };

        return descending ? ordered.ThenByDescending(user => user.Id) : ordered.ThenBy(user => user.Id);
    }
}

