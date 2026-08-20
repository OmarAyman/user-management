using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace UserManagement.Api.Configuration;

/// <summary>
/// Culture resolution for the API.
/// </summary>
public static class LocalizationConfiguration
{
    public const string DefaultCulture = "en";
    public const string ArabicCulture = "ar";

    public static readonly string[] SupportedCultures = [DefaultCulture, ArabicCulture];

    public static RequestLocalizationOptions Build()
    {
        var cultures = SupportedCultures.Select(culture => new CultureInfo(culture)).ToArray();

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(DefaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,

            // An unsupported or malformed value falls back to the default rather than failing the request: a
            // browser sending a language this API does not speak is not an error.
            ApplyCurrentCultureToResponseHeaders = true,
        };

        // Order matters. The query string comes first because it makes manual API testing trivial - a reviewer
        // can add ?culture=ar to any request without touching headers - and Accept-Language second because
        // that is what the SPA and any real client actually send.
        options.RequestCultureProviders =
        [
            new QueryStringRequestCultureProvider(),
            new AcceptLanguageHeaderRequestCultureProvider(),
        ];

        return options;
    }
}
