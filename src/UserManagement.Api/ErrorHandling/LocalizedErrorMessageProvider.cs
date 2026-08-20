using Microsoft.Extensions.Localization;
using UserManagement.Application.Common.Abstractions;

namespace UserManagement.Api.ErrorHandling;

/// <summary>
/// Resolves error codes and message keys to text in the request's culture.
/// </summary>
/// <remarks>
/// <para>
/// The whole localization design in one place: an error code is stable and never translated, and the sentence
/// shown next to it is looked up per request. Nothing in the application branches on a language - the culture
/// is set by the request-localization middleware and read from <c>CultureInfo.CurrentUICulture</c> by the
/// localizer.
/// </para>
/// <para>
/// A missing key resolves to a generic sentence rather than to the key itself. Showing "Title.SOMETHING" to a
/// user turns a missing translation into an obviously broken UI; the parity test is what catches the missing
/// entry, at build time.
/// </para>
/// </remarks>
public sealed class LocalizedErrorMessageProvider(IStringLocalizer<ErrorMessages> localizer)
    : IErrorMessageProvider, IMessageLocalizer
{
    public string GetTitle(string errorCode)
    {
        var localized = localizer[$"Title.{errorCode}"];

        return localized.ResourceNotFound ? localizer["Title.RESOURCE_CONFLICT"] : localized.Value;
    }

    public string? GetDetail(string errorCode)
    {
        var localized = localizer[$"Detail.{errorCode}"];

        // An empty value is a deliberate choice in the resource file (the 500 case), not a missing entry, so
        // it is passed through as "no detail" rather than replaced with a fallback.
        return localized.ResourceNotFound || string.IsNullOrEmpty(localized.Value) ? null : localized.Value;
    }

    public string Get(string key, params object[] arguments) =>
        arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
}
