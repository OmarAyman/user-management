using System.Reflection;
using Microsoft.OpenApi;

namespace UserManagement.Api.Configuration;

/// <summary>
/// Swagger/OpenAPI setup.
/// </summary>
/// <remarks>
/// The bearer scheme is declared so a reviewer can paste a token into <c>Authorize</c> and exercise the whole
/// API from the browser. Without it the document describes endpoints nobody can call, which is documentation
/// in name only.
/// </remarks>
public static class SwaggerConfiguration
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddApplicationSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "User Management API",
                Version = "v1",
                Description =
                    "Authentication, user management, roles and the audit trail.\n\n"
                    + "Sign in through POST /api/auth/login, then paste the accessToken into Authorize. The "
                    + "refresh token is not in the response body - it is an httpOnly cookie, which the browser "
                    + "stores and sends on its own.\n\n"
                    + "Every error is RFC 7807 problem+json carrying a stable errorCode alongside a localized "
                    + "title and detail. Send Accept-Language: ar, or add ?culture=ar, for Arabic messages; the "
                    + "errorCode never changes.",
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the accessToken from a sign-in response. Swagger adds the Bearer prefix.",
            });

            // Swashbuckle 10 takes a factory so the requirement can reference the scheme in the document being
            // built, rather than a detached copy of it.
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                // No scopes: this is a bearer token carrying a role claim, not OAuth.
                { new OpenApiSecuritySchemeReference(BearerScheme, document), [] },
            });

            // The XML comments on controllers and contracts become the endpoint and schema descriptions, so the
            // document explains the same reasoning as the code rather than repeating it in a second place.
            var documentation = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(documentation))
            {
                options.IncludeXmlComments(documentation, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}
