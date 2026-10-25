# syntax=docker/dockerfile:1

# Build stage: publishes the Web API and, alongside it, an EF Core migrations bundle. The bundle is
# built here because applying migrations needs the SDK and the dotnet-ef tool, neither of which the
# runtime image carries - the bundle is a plain executable that a one-shot container can run before
# the API starts, so the deployed image never needs the SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /source

# Restored first and on their own so the tool and package layers survive a source-only change.
COPY .config/dotnet-tools.json .config/
RUN dotnet tool restore

# Central package management: the project files name their packages and this file alone gives the
# versions, so restore fails outright without it. It belongs in this layer rather than with the
# sources below for the same reason the csproj files do - a source-only change must not invalidate
# the restore cache, and a version change should.
COPY Directory.Packages.props ./

COPY src/Presentation/Library.WebApi/Library.WebApi.csproj src/Presentation/Library.WebApi/
COPY src/Application/Library.Command/Library.Command.csproj src/Application/Library.Command/
COPY src/Application/Library.Query/Library.Query.csproj src/Application/Library.Query/
COPY src/Application/Library.Shared/Library.Shared.csproj src/Application/Library.Shared/
COPY src/Domain/Library.Domain/Library.Domain.csproj src/Domain/Library.Domain/
COPY src/Infrastructure/Library.Data/Library.Data.csproj src/Infrastructure/Library.Data/
COPY src/Infrastructure/Library.Cache/Library.Cache.csproj src/Infrastructure/Library.Cache/
COPY src/Common/Library.Output/Library.Output.csproj src/Common/Library.Output/
COPY src/Common/Library.Mediator/Library.Mediator.csproj src/Common/Library.Mediator/
COPY src/Common/Library.Data.Relational/Library.Data.Relational.csproj src/Common/Library.Data.Relational/
COPY src/Common/Library.WebApi.Core/Library.WebApi.Core.csproj src/Common/Library.WebApi.Core/

RUN dotnet restore src/Presentation/Library.WebApi/Library.WebApi.csproj

COPY src/ src/

RUN dotnet publish src/Presentation/Library.WebApi/Library.WebApi.csproj \
        --configuration Release --no-restore --output /app

# Framework-dependent on purpose: the runtime image already ships the .NET runtime the bundle needs,
# so there is no reason to duplicate it. The placeholder connection string is only what
# DesignTimeDbContextFactory needs to hand `dotnet ef` a DbContext while the bundle is built; nothing
# connects here, and the bundle uses the --connection it is given at run time instead.
RUN LIBRARY_DATA_CONNECTIONSTRING="Host=localhost;Port=5432;Database=library;Username=postgres;Password=postgres" \
    dotnet ef migrations bundle \
        --project src/Infrastructure/Library.Data \
        --startup-project src/Infrastructure/Library.Data \
        --configuration Release \
        --output /app/library-migrate \
        --force

# Runtime stage.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS final
WORKDIR /app

# curl is installed for the container health check alone: the runtime image ships neither curl nor
# wget, and without one Compose has no way to tell a started container from a ready one - which is
# what "depends_on: service_healthy" needs to be meaningful.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh /app/library-migrate

# Non-root. Nothing in the image is written to at run time - logs go to stdout, not to a file - so
# there is no directory to hand over first.
USER $APP_UID

# Matches the aspnet image's own default; named here so the Compose port mapping has an explicit
# counterpart to point at.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "Library.WebApi.dll"]
