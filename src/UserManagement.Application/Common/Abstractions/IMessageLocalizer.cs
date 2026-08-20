namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Renders a resource key in the current request's culture.
/// </summary>
/// <remarks>
/// Declared here and implemented in the API layer, like the other ports: the Application layer names messages
/// and never formats them, so no use case has to know which languages exist.
/// </remarks>
public interface IMessageLocalizer
{
    string Get(string key, params object[] arguments);
}
