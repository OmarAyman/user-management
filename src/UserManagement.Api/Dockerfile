# Multi-stage: the SDK image builds, the smaller runtime image ships.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Manifests first, so a source-only change does not invalidate the restore layer.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/UserManagement.Domain/UserManagement.Domain.csproj src/UserManagement.Domain/
COPY src/UserManagement.Application/UserManagement.Application.csproj src/UserManagement.Application/
COPY src/UserManagement.Infrastructure/UserManagement.Infrastructure.csproj src/UserManagement.Infrastructure/
COPY src/UserManagement.Api/UserManagement.Api.csproj src/UserManagement.Api/
RUN dotnet restore src/UserManagement.Api/UserManagement.Api.csproj

COPY src/ src/
RUN dotnet publish src/UserManagement.Api/UserManagement.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# A non-root user: the container has no reason to run with more privilege than the process needs.
RUN adduser --disabled-password --gecos "" --uid 10001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

# Liveness, matching the endpoint the application exposes. No database call, so a saturated pool does not
# report the process as dead.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "wget -q -O- http://127.0.0.1:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "UserManagement.Api.dll"]
