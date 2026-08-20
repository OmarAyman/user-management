using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace UserManagement.Application;

/// <summary>
/// Composition for the Application layer. Registers use-case handlers and validators; it deliberately knows
/// nothing about how persistence, hashing or token issuing are implemented.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddUseCaseHandlers(assembly);

        return services;
    }

    /// <summary>
    /// Registers every concrete <c>ICommandHandler</c> / <c>IQueryHandler</c> in the assembly as scoped.
    /// </summary>
    /// <remarks>
    /// Convention-based registration rather than a hand-maintained list: forgetting a line in a DI file is a
    /// runtime failure, and a use case that exists but is not registered is the kind of bug that only shows up
    /// in production. This is assembly scanning at startup, not a service locator - handlers are still injected
    /// by constructor.
    /// </remarks>
    private static void AddUseCaseHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerInterfaces = new[]
        {
            typeof(Common.Abstractions.ICommandHandler<,>),
            typeof(Common.Abstractions.ICommandHandler<>),
            typeof(Common.Abstractions.IQueryHandler<,>),
        };

        var implementations = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var closedInterfaces = implementation.GetInterfaces()
                .Where(candidate => candidate.IsGenericType
                                    && handlerInterfaces.Contains(candidate.GetGenericTypeDefinition()));

            foreach (var closedInterface in closedInterfaces)
            {
                services.AddScoped(closedInterface, implementation);
            }
        }
    }
}
