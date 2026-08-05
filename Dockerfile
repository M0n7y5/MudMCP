# Copyright (c) 2026 Mud MCP Contributors
# Licensed under the GNU General Public License v2.0.

# ---------------------------------------------------------------------------
# Stage 1 – build
#
# Runs on the build host's architecture ($BUILDPLATFORM) and cross-compiles
# for the target architecture ($TARGETARCH), so multi-arch CI builds do not
# need QEMU emulation for the .NET build itself.
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy repo-level files first so NuGet restore is cached independently.
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./

# Copy project files for restore layer caching.
COPY src/MudBlazor.Mcp/MudBlazor.Mcp.csproj src/MudBlazor.Mcp/

RUN dotnet restore src/MudBlazor.Mcp/MudBlazor.Mcp.csproj -a $TARGETARCH

# Copy the actual source after restore to preserve the cache layer above.
COPY src/MudBlazor.Mcp/ src/MudBlazor.Mcp/

RUN dotnet publish src/MudBlazor.Mcp/MudBlazor.Mcp.csproj \
    -a $TARGETARCH -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Stage 2 – runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.source="https://github.com/M0n7y5/MudMCP" \
      org.opencontainers.image.description="MCP server exposing version-accurate MudBlazor component documentation" \
      org.opencontainers.image.licenses="GPL-2.0"

# curl is used by the image health check and compose probes.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# The app stores per-version git clones, serialized index files, and the
# versions.json manifest under /app/data/v{Version}/.
# Mount a named volume at /app/data so all cached data persists across restarts.
# Owned by the non-root 'app' user the container runs as.
RUN mkdir -p /app/data && chown app:app /app/data
VOLUME ["/app/data"]

# Use port 8080 inside the container (12-factor / Fly.io / Azure Container Apps
# convention). The host-side mapping is defined in docker-compose.yml.
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV MUDBLAZOR_VERSION=9.0.0

EXPOSE 8080

# Run as the non-root user shipped with the .NET images.
USER app

# Liveness only: /health/live responds immediately, while /health/ready stays
# degraded until the first clone + index build completes (up to ~10 minutes).
HEALTHCHECK --interval=30s --timeout=10s --start-period=600s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Shell form so ${MUDBLAZOR_VERSION} expands; exec keeps dotnet as PID 1 for
# proper signal handling.
ENTRYPOINT exec dotnet MudBlazor.Mcp.dll --version ${MUDBLAZOR_VERSION}
