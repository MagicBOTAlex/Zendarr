# syntax=docker/dockerfile:1

# Zendarr image: builds the single unified backend (src/Zendarr.Series,
# .NET 10) plus the single unified SPA (src/Zendarr.Web) and runs them on one
# listener (:6868). No gateway, no reverse proxy, no second process.
#
#   build:   docker build -t zendarr .
#   run:     docker run --rm -p 6868:6868 -v zendarr-data:/data zendarr
#   compose: docker compose up --build

# Node is only needed to build the SPA; it is not part of the runtime image.
# Pinned to the Node release declared in src/Zendarr.Web/package.json.
FROM node:24.19.0-bookworm-slim AS node

# ---------------------------------------------------------------------------
# Build stage: .NET SDK 10.0.401 (pinned by src/Zendarr.Series/global.json)
# plus Node 24 / Yarn 1.22.22 for the React frontend.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_ROOT=/usr/share/dotnet \
    NODE_OPTIONS=--max-old-space-size=8192 \
    YARN_CACHE_FOLDER=/usr/local/share/.cache/yarn \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0

# Node + Corepack come from the pinned Node image; Corepack provisions the Yarn
# version declared by package.json's "packageManager" field (no hard-coded
# /opt/yarn-vX path).
COPY --from=node /usr/local /usr/local

# The Node image's yarn/yarnpkg/pnpm shims point at /opt/yarn-vX, which is not
# copied (only /usr/local is), so they are dangling here and make `corepack
# enable` fail on realpath. Remove them so Corepack installs its own shims.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates git \
 && rm -rf /var/lib/apt/lists/* \
 && rm -f /usr/local/bin/yarn /usr/local/bin/yarnpkg /usr/local/bin/pnpm \
 && corepack enable \
 && dotnet --list-sdks \
 && node --version

WORKDIR /src

# Prime frontend dependencies in their own layer so source edits don't refetch.
COPY src/Zendarr.Web/package.json src/Zendarr.Web/yarn.lock src/Zendarr.Web/.yarnrc src/Zendarr.Web/
RUN --mount=type=cache,target=/usr/local/share/.cache/yarn,sharing=locked \
    cd src/Zendarr.Web \
    && yarn --version \
    && yarn install --frozen-lockfile --network-timeout 600000

# Full source (node_modules/_output excluded via .dockerignore), then the
# project's own build: unified backend + unified UI.
COPY . .

# Backend build configuration: Release (default) or Debug. A Debug build enables
# the debug-only diagnostics endpoint (GET /api/v3/system/diagnostics). Enable it
# by building with `CONFIGURATION=Debug docker compose up -d --build` (compose
# forwards CONFIGURATION here) or `docker build --build-arg CONFIGURATION=Debug`.
ARG CONFIGURATION=Release

RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    --mount=type=cache,target=/usr/local/share/.cache/yarn,sharing=locked \
    CONFIGURATION=$CONFIGURATION ./build.sh

# ---------------------------------------------------------------------------
# Runtime stage: ASP.NET Core runtime 10, python3 (zendarr.sh reads the API
# keys out of config.xml) and curl (healthcheck).
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
SHELL ["/bin/bash", "-o", "pipefail", "-c"]

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    ZENDARR_DATA=/data \
    ZENDARR_PORT=6868 \
    PATH=/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin

# ffmpeg powers media compression (NVENC / VA-API / software; resolved from PATH).
# vainfo is optional but makes VA-API capability diagnosis easier. passwd
# provides usermod/groupmod used by the entrypoint when it starts as root.
#
# The `app` user is added to the video/render groups so hardware transcoding can
# access /dev/dri. The entrypoint uses `setpriv --init-groups` (not
# `--clear-groups`) so these supplementary groups survive the privilege drop.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      curl ca-certificates python3-minimal tzdata util-linux ffmpeg vainfo passwd \
 && rm -rf /var/lib/apt/lists/* \
 && (getent group video >/dev/null || groupadd -r video) \
 && (getent group render >/dev/null || groupadd -r render) \
 && usermod -aG video,render app \
 && dotnet --list-runtimes \
 && ffmpeg -version | head -1

WORKDIR /app

# Keep the repo layout so zendarr.sh's find_dll resolves exactly as it does
# from a normal checkout.
COPY --from=build /src/src/Zendarr.Series/_output ./src/Zendarr.Series/_output
COPY --from=build /src/src/Zendarr.Web/_output/UI ./src/Zendarr.Web/_output/UI
COPY zendarr.sh zendarr-stop.sh ./
COPY Logo ./Logo
# License/attribution notices must accompany GPL binary redistribution.
COPY LICENSE NOTICE ./
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh

# /data backs the named volume; /transcode is the default transcode working
# folder (mount a volume there to persist it). LinuxServer.io-style: the
# entrypoint starts as root, remaps the `app` user to PUID/PGID, fixes the
# ownership of /app and the mounted volumes, then drops privileges to that user
# for the service itself.
RUN mkdir -p /data /transcode \
 && chown -R app:app /app /data /transcode \
 && chmod +x ./zendarr.sh ./zendarr-stop.sh /usr/local/bin/docker-entrypoint.sh

VOLUME ["/data"]

EXPOSE 6868

HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=5 \
  CMD curl -fsS "http://localhost:${ZENDARR_PORT:-6868}/ping" || exit 1

# No USER directive: the entrypoint starts as root, remaps PUID/PGID and chowns
# the volumes, then drops to the non-root service user with setpriv (preserving
# supplementary groups for GPU access).
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["./zendarr.sh", "--attach"]
