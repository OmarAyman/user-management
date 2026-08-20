using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using FluentValidation;
using UserManagement.Api.ErrorHandling;
using UserManagement.Api.Filters;
using UserManagement.Api.RateLimiting;
using UserManagement.Application.Common.Options;
using UserManagement.Api.Configuration;
using UserManagement.Api.Middleware;
using UserManagement.Api.Services;
using UserManagement.Application;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Application.Common.Security;
using UserManagement.Domain.Constants;
using UserManagement.Infrastructure;
using UserManagement.Infrastructure.Configuration;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Seeding;
using UserManagement.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Structured logging is configured before anything else, so a failure during startup is itself logged.
builder.Services.AddSerilog(configuration => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        new CompactJsonFormatter(),
        Path.Combine("logs", "usermanagement-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7));

using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var startupLogger = startupLoggerFactory.CreateLogger("Startup");

// Refuses to boot on a placeholder or missing signing key outside Development; generates an ephemeral one
// inside it, so nothing has to be committed for the app to run locally.
JwtKeyGuard.EnsureSigningKey(builder.Configuration, builder.Environment, startupLogger);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IClientInfoProvider, ClientInfoProvider>();
builder.Services.AddScoped<RefreshTokenCookieWriter>();

builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));

// Security policy that use cases enforce, validated at startup like every other option set.
builder.Services.AddOptions<LockoutOptions>()
    .Bind(builder.Configuration.GetSection(LockoutOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PasswordPolicyOptions>()
    .Bind(builder.Configuration.GetSection(PasswordPolicyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Shape validation lives at the transport boundary, because the filter validates the request record the
// client actually posted. Business rules stay in the handlers, where no caller can bypass them.
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly, includeInternalTypes: true);

// Ordered chain: expected failures first, then the catch-all that discloses nothing.
builder.Services.AddSingleton<IErrorMessageProvider, EnglishErrorMessageProvider>();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddApplicationRateLimiting(builder.Configuration);

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        // A payload carrying a field the model does not have is rejected rather than silently ignored. Silent
        // dropping is safe but invisible; rejecting makes a mass-assignment attempt loud in the logs.
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddOpenApi();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Claims arrive exactly as they were issued: no translation into WS-Federation URIs, so a lookup by
        // "role" finds the claim named "role".
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,

            // No grace period. A 15-minute token with five minutes of default skew is a 20-minute token, and
            // the whole point of the short lifetime is that it is short.
            ClockSkew = TimeSpan.Zero,

            NameClaimType = JwtClaimNames.Username,
            RoleClaimType = JwtClaimNames.Role,
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.ManageUsers, policy => policy.RequireRole(RoleNames.Admin))
    .AddPolicy(Policies.ViewAuditLogs, policy => policy.RequireRole(RoleNames.Admin));

var corsOrigins = builder.Configuration
    .GetSection($"{CorsOptions.SectionName}:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy(
    CorsOptions.PolicyName,
    policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Required for the httpOnly refresh cookie, which is why the origin list must be explicit.
        .AllowCredentials()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Wraps everything downstream, so a failure anywhere below becomes a ProblemDetails rather than a stack trace.
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsOptions.PolicyName);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Liveness only: no database call, so a saturated connection pool does not read as a dead process.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .AllowAnonymous();

await ApplyDatabaseStartupTasksAsync(app);

await app.RunAsync();

// Applies migrations and seeds demo data when configured to do so. Enabled in Development only, so a
// deployment never migrates itself as a side effect of starting.
static async Task ApplyDatabaseStartupTasksAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:MigrateOnStartup", false))
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied");

    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    await seeder.SeedAsync();
}

/// <summary>
/// Marker so the integration test project can reference this assembly through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
}
