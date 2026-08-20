namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Commits the current change set. Kept as its own abstraction so a handler can persist without depending on
/// EF Core, and so the audit and stamping interceptors are the only code that reacts to a save.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
