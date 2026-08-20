using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;

namespace UserManagement.Infrastructure.Persistence;

/// <summary>
/// Runs queries composed by the Application layer.
/// </summary>
/// <remarks>
/// The whole of EF Core's async query surface, confined to one small class. This is what lets a use case
/// compose <c>Where</c>, <c>OrderBy</c>, <c>Select</c>, <c>Skip</c> and <c>Take</c> - all plain
/// <c>System.Linq</c> - and still execute asynchronously without the Application project referencing the ORM.
/// </remarks>
public sealed class EfQueryExecutor : IQueryExecutor
{
    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.ToListAsync(cancellationToken);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.CountAsync(cancellationToken);

    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
        query.AnyAsync(cancellationToken);
}
