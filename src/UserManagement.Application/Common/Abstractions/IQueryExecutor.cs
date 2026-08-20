namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Executes a composed <see cref="IQueryable{T}"/> asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// Composing a query - <c>Where</c>, <c>OrderBy</c>, <c>Select</c>, <c>Skip</c>, <c>Take</c> - is plain
/// <c>System.Linq</c> and belongs in a use case. <i>Running</i> it asynchronously is not: <c>ToListAsync</c>
/// and <c>CountAsync</c> are EF Core extensions, and importing them here would put the ORM in the Application
/// layer, which is exactly what the architecture test forbids.
/// </para>
/// <para>
/// So the shape of the query stays with the use case that means it, and only the execution crosses into
/// Infrastructure. The alternative - repositories returning finished pages - would move filtering, sorting and
/// paging decisions into the persistence layer, where they are no longer visible next to the rule they serve.
/// </para>
/// </remarks>
public interface IQueryExecutor
{
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}
