namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Handles one command. Injected directly into the controller that exposes it - there is no dispatcher and no
/// pipeline, because validation runs as an action filter and auditing runs as a persistence interceptor, which
/// are the two concerns a pipeline would otherwise exist for (ADR-0003).
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>A command with no meaningful return value.</summary>
public interface ICommandHandler<in TCommand>
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles one query. Read-only by convention: implementations do not call SaveChanges.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
