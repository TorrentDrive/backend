# ============================================================
# TorrentDrive All-in-One Image
# Frontend + API + Workers + PostgreSQL 15 + Redis 7
# + Prometheus + Grafana
# ============================================================

# ---- Stage 1: Frontend Build ----
FROM node:22-alpine AS frontend-build
WORKDIR /app

COPY frontend/package.json frontend/package-lock.json ./

# npm ci against package-lock.json, matching what the frontend repo's own
# Dockerfile and CI use. This stage previously ran yarn against yarn.lock, so
# the all-in-one image and the standalone frontend image resolved dependencies
# from two different lockfiles that nothing kept in agreement — the two
# artifacts a user can install could ship different transitive versions of the
# same application.
#
# The arm64 network timeout that used to be configured here is no longer needed:
# arm64 builds on a native runner now rather than under QEMU.
#
# This stage is deliberately NOT pinned to $BUILDPLATFORM the way the .NET
# stages are. Next's standalone output carries architecture-specific native
# binaries, and that output is copied straight into the runtime image below, so
# building it on the wrong architecture would ship a broken frontend.
RUN npm ci --no-audit --no-fund

COPY frontend/ ./

# BACKEND_URL is not needed at build time. The browser calls the relative
# /proxy/api path and app/proxy/[...path]/route.ts resolves it per request, so
# one image works against any backend.

RUN npm run build

# ---- Stage 2: Backend Build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS backend-build
WORKDIR /src

# Copy solution and project files for dependency restoration
COPY Directory.Build.props .
COPY TorrentDrive.sln .
COPY TorrentDrive.Core/TorrentDrive.Core.csproj TorrentDrive.Core/
COPY TorrentDrive.Application/TorrentDrive.Application.csproj TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/TorrentDrive.Infrastructure.csproj TorrentDrive.Infrastructure/
COPY TorrentDrive.API/TorrentDrive.API.csproj TorrentDrive.API/
COPY TorrentDrive.Worker/TorrentDrive.Worker.csproj TorrentDrive.Worker/
COPY TorrentDrive.GoogleDrive.Worker/TorrentDrive.GoogleDrive.Worker.csproj TorrentDrive.GoogleDrive.Worker/
COPY TorrentDrive.S3.Worker/TorrentDrive.S3.Worker.csproj TorrentDrive.S3.Worker/

RUN dotnet restore TorrentDrive.sln

# Copy all source code
COPY TorrentDrive.Core/ TorrentDrive.Core/
COPY TorrentDrive.Application/ TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/ TorrentDrive.Infrastructure/
COPY TorrentDrive.API/ TorrentDrive.API/
COPY TorrentDrive.Worker/ TorrentDrive.Worker/
COPY TorrentDrive.GoogleDrive.Worker/ TorrentDrive.GoogleDrive.Worker/
COPY TorrentDrive.S3.Worker/ TorrentDrive.S3.Worker/

ARG BUILD_SHA=""
ARG BUILD_TIME=""

# Publish each project to its own directory
RUN dotnet publish TorrentDrive.API/TorrentDrive.API.csproj \
      -c Release --no-restore -o /publish/api /p:UseAppHost=false /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME
RUN dotnet publish TorrentDrive.Worker/TorrentDrive.Worker.csproj \
      -c Release --no-restore -o /publish/torrent-worker /p:UseAppHost=false /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME
RUN dotnet publish TorrentDrive.GoogleDrive.Worker/TorrentDrive.GoogleDrive.Worker.csproj \
      -c Release --no-restore -o /publish/gdrive-worker /p:UseAppHost=false /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME
RUN dotnet publish TorrentDrive.S3.Worker/TorrentDrive.S3.Worker.csproj \
      -c Release --no-restore -o /publish/s3-worker /p:UseAppHost=false /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME

# ---- Stage 3: Runtime ----
FROM ubuntu:22.04 AS runtime

# Supplied automatically by BuildKit as "amd64" or "arm64". Prometheus and
# Grafana both publish assets under exactly those names, so it substitutes
# directly into their download URLs.
#
# These downloads were hardcoded to amd64, which meant the arm64 image could
# never actually build: dpkg refuses an amd64 .deb on arm64. It went unnoticed
# because the frontend stage timed out first and the build never reached here.
ARG TARGETARCH

ENV DEBIAN_FRONTEND=noninteractive

# Install PostgreSQL 15, Redis, supervisord
RUN apt-get update && apt-get install -y --no-install-recommends \
      gnupg2 lsb-release curl ca-certificates adduser libfontconfig1 musl \
    && echo "deb http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" \
       > /etc/apt/sources.list.d/pgdg.list \
    && curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
       | gpg --dearmor -o /etc/apt/trusted.gpg.d/pgdg.gpg \
    && apt-get update && apt-get install -y --no-install-recommends \
      postgresql-15 \
      redis-server \
      supervisor \
    && rm -rf /var/lib/apt/lists/*

# Install Prometheus
RUN curl -fsSL "https://github.com/prometheus/prometheus/releases/download/v2.48.1/prometheus-2.48.1.linux-${TARGETARCH}.tar.gz" \
      -o /tmp/prometheus.tar.gz \
    && tar -xzf /tmp/prometheus.tar.gz -C /tmp \
    && cp "/tmp/prometheus-2.48.1.linux-${TARGETARCH}/prometheus" /usr/local/bin/ \
    && cp "/tmp/prometheus-2.48.1.linux-${TARGETARCH}/promtool" /usr/local/bin/ \
    && mkdir -p /etc/prometheus /data/prometheus \
    && rm -rf /tmp/prometheus*

# Install Grafana
RUN curl -fsSL "https://dl.grafana.com/oss/release/grafana_10.2.3_${TARGETARCH}.deb" -o /tmp/grafana.deb \
    && dpkg -i /tmp/grafana.deb \
    && rm /tmp/grafana.deb

# Install .NET ASP.NET Runtime 9.0
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

# Install Node.js 22 runtime (for Next.js standalone server)
RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Create data directories (volume mount points)
RUN mkdir -p /data/postgres /data/redis /data/downloads /data/prometheus /data/grafana \
             /app/tmp /app/tmp/bundle /app/logs \
             /var/log/supervisor \
    && chown -R postgres:postgres /data/postgres \
    && chown -R redis:redis /data/redis \
    && chown -R grafana:grafana /data/grafana

# Copy built frontend (Next.js standalone)
COPY --from=frontend-build /app/.next/standalone /app/frontend/
COPY --from=frontend-build /app/.next/static /app/frontend/.next/static

# Copy published backend services
COPY --from=backend-build /publish/api /app/api/
COPY --from=backend-build /publish/torrent-worker /app/torrent-worker/
COPY --from=backend-build /publish/gdrive-worker /app/gdrive-worker/
COPY --from=backend-build /publish/s3-worker /app/s3-worker/

# Copy Prometheus config
COPY config/prometheus.yml /etc/prometheus/prometheus.yml

# Copy Grafana provisioning and dashboards
COPY config/grafana/provisioning/ /etc/grafana/provisioning/
COPY observability/grafana/dashboards/ /var/lib/grafana/dashboards/

# Copy configuration files
COPY config/supervisord.conf /etc/supervisor/conf.d/torrentdrive.conf
COPY entrypoint.sh /entrypoint.sh
COPY wait-for-db.sh /app/wait-for-db.sh
RUN chmod +x /entrypoint.sh /app/wait-for-db.sh

# Persistent data volumes
VOLUME ["/data/postgres", "/data/redis", "/data/downloads", "/data/prometheus", "/data/grafana"]

EXPOSE 47100 47200 47500 47600

ENTRYPOINT ["/entrypoint.sh"]
