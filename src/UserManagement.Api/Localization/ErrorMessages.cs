// The marker type for Resources/ErrorMessages.resx. It lives in the project's root namespace on purpose:
// with ResourcesPath = "Resources", IStringLocalizer<T> resolves the resource base name by taking T's
// namespace relative to the root namespace and prefixing the resources path. A type in
// UserManagement.Api.Localization would therefore look for Resources/Localization/ErrorMessages.resx, and the
// lookup would fail silently - returning the key instead of the message, which reads like a missing
// translation rather than a wiring mistake.
namespace UserManagement.Api;

/// <summary>
/// Marker for the localized error and validation messages in <c>Resources/ErrorMessages.resx</c> and its
/// culture-specific siblings. The keys themselves are in <c>UserManagement.Domain.Constants.MessageKeys</c>,
/// shared with the use cases that name them.
/// </summary>
public sealed class ErrorMessages;
