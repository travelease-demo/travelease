# syntax=docker/dockerfile:1
# =============================================================================================
# TravelEase - production multi-stage Dockerfile (recommended starting point)
#
#   docker build -t travelease:1.0.0 --build-arg APP_VERSION=1.0.0 .
#   docker run -d -p 8080:8080 -e DB_HOST=host.docker.internal -e DB_PASSWORD=... travelease:1.0.0
#
# Stages:
#   build   SDK image: restore + publish        (large, never shipped)
#   export  scratch:   just the published files  (docker build --target export --output publish .)
#   final   ASP.NET runtime image                (small, non-root, health-checked)
# =============================================================================================

# ARG before the first FROM can be used in FROM lines (change the .NET tag without editing the file).
ARG DOTNET_VERSION=8.0

# ---------------------------------------------------------------------------------------------
# Stage 1: build
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=1.0.0
WORKDIR /src

# Copy only the project file first: the restore layer is cached until dependencies change,
# so editing a .cs or .cshtml file does not re-download NuGet packages.
COPY TravelEase.csproj ./
RUN dotnet restore TravelEase.csproj

# Now copy the rest of the source and publish.
COPY . .
RUN dotnet publish TravelEase.csproj \
        --configuration "${BUILD_CONFIGURATION}" \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:Version="${APP_VERSION}"

# ---------------------------------------------------------------------------------------------
# Stage 2 (optional): export the published files to the host without installing the .NET SDK
#   docker build --target export --output publish .
# ---------------------------------------------------------------------------------------------
FROM scratch AS export
COPY --from=build /app/publish /

# ---------------------------------------------------------------------------------------------
# Stage 3: runtime image
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG APP_VERSION=1.0.0
ARG BUILD_DATE=unknown
ARG VCS_REF=unknown

LABEL org.opencontainers.image.title="TravelEase" \
      org.opencontainers.image.description="TravelEase - Travel Booking Management System (ASP.NET Core MVC + PostgreSQL)" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.vendor="TravelEase Platform Team" \
      org.opencontainers.image.authors="platform-team@travelease.example" \
      com.travelease.owner="platform-team"

# Defaults - every one of these can be overridden with `docker run -e NAME=value`.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__Version=${APP_VERSION} \
    Storage__ReportsPath=/app/data/reports \
    Storage__DataProtectionKeysPath=/app/data/keys \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

WORKDIR /app

# Writable data directory owned by the built-in non-root "app" user (UID $APP_UID = 1654).
RUN mkdir -p /app/data/reports /app/data/keys \
    && chown -R "${APP_UID}:${APP_UID}" /app/data

# Application files stay owned by root, i.e. read-only for the app user.
COPY --from=build /app/publish .

# Drop root privileges.
USER ${APP_UID}

# Generated reports and Data Protection keys live here. Mount a named volume to persist them.
VOLUME ["/app/data"]

# Documentation only - publish with -p 8080:8080. Change the internal port with ASPNETCORE_HTTP_PORTS.
EXPOSE 8080

# No curl/wget in this image: the app has a built-in probe mode (see Infrastructure/HealthProbe.cs).
# /health/ready also verifies PostgreSQL connectivity.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "TravelEase.dll", "healthcheck", "/health/ready"]

# ENTRYPOINT = the executable; anything after the image name in `docker run` is appended as arguments:
#   docker run travelease:1.0.0 --urls http://+:9090       (extra ASP.NET Core args)
#   docker run travelease:1.0.0 healthcheck                  (run the probe once and exit)
ENTRYPOINT ["dotnet", "TravelEase.dll"]
