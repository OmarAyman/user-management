namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Resolves a stable error code to human-readable text. The seam localization plugs into: the error code stays
/// constant and only the sentence changes with the request culture.
/// </summary>
/// <remarks>
/// Implemented by <see cref="LocalizedErrorMessageProvider"/> over the resource files. Kept as an interface so
/// the exception handlers depend on "resolve this code to a sentence" rather than on how that happens - which
/// is what allowed localization to arrive without touching either handler.
/// </remarks>
public interface IErrorMessageProvider
{
    string GetTitle(string errorCode);

    string? GetDetail(string errorCode);
}
