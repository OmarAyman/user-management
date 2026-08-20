namespace UserManagement.Application.Common.Models;

/// <summary>
/// One page of results plus the metadata a client needs to render paging controls.
/// </summary>
/// <remarks>
/// <see cref="TotalPages"/>, <see cref="HasPreviousPage"/> and <see cref="HasNextPage"/> are derived rather
/// than stored, so the arithmetic exists once here instead of being recomputed - slightly differently - in
/// every consumer.
/// </remarks>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public static PagedResult<T> Empty(int pageNumber, int pageSize) =>
        new([], pageNumber, pageSize, 0);
}

/// <summary>Paging bounds. The cap is a protection, not a preference: an unbounded page size is a denial-of-service knob.</summary>
public static class PagingDefaults
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;
}

public enum SortDirection
{
    Ascending = 0,
    Descending = 1,
}
