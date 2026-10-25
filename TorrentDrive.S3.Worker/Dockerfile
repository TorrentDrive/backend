# Stage 1: Build
# Pinned to the build machine's own architecture. dotnet publish runs with
# /p:UseAppHost=false, so the output is portable IL with no native launcher —
# identical for every target. Only the runtime stage below needs to vary by
# architecture, which keeps arm64 images from compiling under QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy ONLY the project files we need (no solution file, no other workers)
COPY TorrentDrive.Core/TorrentDrive.Core.csproj TorrentDrive.Core/
COPY TorrentDrive.Application/TorrentDrive.Application.csproj TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/TorrentDrive.Infrastructure.csproj TorrentDrive.Infrastructure/
COPY TorrentDrive.S3.Worker/TorrentDrive.S3.Worker.csproj TorrentDrive.S3.Worker/

# Restore dependencies for the S3 Worker project only
WORKDIR /src/TorrentDrive.S3.Worker
RUN dotnet restore TorrentDrive.S3.Worker.csproj
WORKDIR /src

# Copy all source code (excluding solution file via .dockerignore)
COPY TorrentDrive.Core/ TorrentDrive.Core/
COPY TorrentDrive.Application/ TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/ TorrentDrive.Infrastructure/
COPY TorrentDrive.S3.Worker/ TorrentDrive.S3.Worker/

# Build and publish
# Explicitly build only this project, ignoring any solution file that might be present
WORKDIR /src/TorrentDrive.S3.Worker
RUN dotnet build TorrentDrive.S3.Worker.csproj -c Release --no-restore -o /app/build /p:BuildProjectReferences=true /p:SolutionDir=
# Stamped by CI so /api/version can report exactly what this image was built
# from. Optional: a local build simply reports them as unknown.
ARG BUILD_SHA=""
ARG BUILD_TIME=""

RUN dotnet publish TorrentDrive.S3.Worker.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:SolutionDir= /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create temp directories and downloads mount point
RUN mkdir -p /app/tmp /app/tmp/bundle /app/logs /app/data /app/downloads

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published output
COPY --from=build /app/publish .

# === Environment Variables ===
ENV ASPNETCORE_ENVIRONMENT=Production

# === .NET Runtime Disk Safety (CRITICAL - Prevents root FS fill) ===
ENV TMPDIR=/app/tmp
ENV TEMP=/app/tmp
ENV TMP=/app/tmp
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/tmp/bundle
ENV HOME=/app

# === .NET Runtime Performance for long-lived workers ===
ENV DOTNET_gcServer=1
ENV DOTNET_EnableDiagnostics=0

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Set entry point
ENTRYPOINT ["dotnet", "TorrentDrive.S3.Worker.dll"]
