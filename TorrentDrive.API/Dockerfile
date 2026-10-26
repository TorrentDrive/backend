# Stage 1: Build
# Pinned to the build machine's own architecture. dotnet publish runs with
# /p:UseAppHost=false, so the output is portable IL with no native launcher —
# identical for every target. Only the runtime stage below needs to vary by
# architecture, which keeps arm64 images from compiling under QEMU emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution, shared build properties, and project files for dependency restoration
COPY Directory.Build.props .
COPY TorrentDrive.sln .
COPY TorrentDrive.Core/TorrentDrive.Core.csproj TorrentDrive.Core/
COPY TorrentDrive.Application/TorrentDrive.Application.csproj TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/TorrentDrive.Infrastructure.csproj TorrentDrive.Infrastructure/
COPY TorrentDrive.API/TorrentDrive.API.csproj TorrentDrive.API/
COPY TorrentDrive.Worker/TorrentDrive.Worker.csproj TorrentDrive.Worker/
COPY TorrentDrive.GoogleDrive.Worker/TorrentDrive.GoogleDrive.Worker.csproj TorrentDrive.GoogleDrive.Worker/
COPY TorrentDrive.S3.Worker/TorrentDrive.S3.Worker.csproj TorrentDrive.S3.Worker/

# Restore dependencies
RUN dotnet restore TorrentDrive.sln

# Copy all source code
COPY TorrentDrive.Core/ TorrentDrive.Core/
COPY TorrentDrive.Application/ TorrentDrive.Application/
COPY TorrentDrive.Infrastructure/ TorrentDrive.Infrastructure/
COPY TorrentDrive.API/ TorrentDrive.API/

# Build and publish
WORKDIR /src/TorrentDrive.API
RUN dotnet build -c Release --no-restore -o /app/build
# Stamped by CI so /api/version can report exactly what this image was built
# from. Optional: a local build simply reports them as unknown.
ARG BUILD_SHA=""
ARG BUILD_TIME=""

RUN dotnet publish -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:BuildSha=$BUILD_SHA /p:BuildTime=$BUILD_TIME

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl is needed by the container healthcheck; the aspnet image ships neither curl nor wget
RUN apt-get update     && apt-get install -y --no-install-recommends curl     && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Create downloads mount point
RUN mkdir -p /app/downloads

# Copy published output
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Change ownership to non-root user
RUN chown -R appuser:appuser /app

# Switch to non-root user
USER appuser

# Expose port
EXPOSE 8080
# Set entry point
ENTRYPOINT ["dotnet", "TorrentDrive.API.dll"]
