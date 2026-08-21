# The backend suites in a container, so a reviewer needs Docker and nothing else - no .NET SDK, no local SQL
# Server, no Testcontainers.
#
# The integration fixture prefers USERMANAGEMENT_TEST_SQL over starting its own database container, which is
# what makes this work without mounting the Docker socket: compose points it at a dedicated SQL Server service
# instead. See tests/UserManagement.IntegrationTests/TestSupport/ApiFixture.cs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test
WORKDIR /src

# Manifests first: a source change should not invalidate the restore layer.
# Optional corporate root CAs, before anything touches the network. Empty on a normal network, in which case
# this is a no-op; behind a TLS-inspecting proxy its absence makes `dotnet restore` fail with a certificate
# chain error that reads like a broken Dockerfile. See certs/.gitkeep.
COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

COPY Directory.Build.props Directory.Packages.props NuGet.config UserManagement.slnx ./
COPY src/UserManagement.Domain/UserManagement.Domain.csproj src/UserManagement.Domain/
COPY src/UserManagement.Application/UserManagement.Application.csproj src/UserManagement.Application/
COPY src/UserManagement.Infrastructure/UserManagement.Infrastructure.csproj src/UserManagement.Infrastructure/
COPY src/UserManagement.Api/UserManagement.Api.csproj src/UserManagement.Api/
COPY tests/UserManagement.UnitTests/UserManagement.UnitTests.csproj tests/UserManagement.UnitTests/
COPY tests/UserManagement.IntegrationTests/UserManagement.IntegrationTests.csproj tests/UserManagement.IntegrationTests/
RUN dotnet restore UserManagement.slnx

COPY src/ src/
COPY tests/ tests/

# Built at image build time, so `docker compose run` starts testing rather than compiling.
RUN dotnet build UserManagement.slnx -c Debug --no-restore

CMD ["dotnet", "test", "UserManagement.slnx", "-c", "Debug", "--no-build", "--nologo"]
