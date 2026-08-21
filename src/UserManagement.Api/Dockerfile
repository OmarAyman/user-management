# Multi-stage: the SDK image builds, the smaller runtime image ships.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Manifests first, so a source-only change does not invalidate the restore layer.
# Optional corporate root CAs, before anything touches the network. Empty on a normal network, in which case
# this is a no-op; behind a TLS-inspecting proxy its absence makes `dotnet restore` fail with a certificate
# chain error that reads like a broken Dockerfile. See certs/.gitkeep.
COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

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

# curl is not in the runtime image, and the HEALTHCHECK below needs something that can make an HTTP request.
# Installed here rather than dropping the check: a container permanently reported unhealthy while actually
# serving teaches an operator to ignore health, and no check at all teaches them nothing.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# A non-root user: the container has no reason to run with more privilege than the process needs. The base
# image already provides one - `app`, uid 1654, exposed as $APP_UID - so there is nothing to create here.
# adduser does not exist in this image, which is why the previous version of this line never built. The
# published files stay root-owned and readable, so the process cannot rewrite its own binaries either.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 8080

# Liveness, matching the endpoint the application exposes. No database call, so a saturated pool does not
# report the process as dead.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD ["/bin/sh", "-c", "curl -fsS http://127.0.0.1:8080/health >/dev/null || exit 1"]

ENTRYPOINT ["dotnet", "UserManagement.Api.dll"]
