#Requires -Version 5.1
#
# TorrentDrive one-command installer for Windows.
#
#   irm https://raw.githubusercontent.com/TorrentDrive/backend/main/install.ps1 | iex
#
$ErrorActionPreference = 'Stop'

$Image = if ($env:TORRENTDRIVE_IMAGE) { $env:TORRENTDRIVE_IMAGE } else { 'ghcr.io/torrentdrive/torrentdrive:latest' }
$ContainerName = if ($env:TORRENTDRIVE_CONTAINER) { $env:TORRENTDRIVE_CONTAINER } else { 'torrentdrive' }
$AppPort = if ($env:FRONTEND_PORT) { $env:FRONTEND_PORT } else { '47100' }

function Write-Info($msg) { Write-Host "  -> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  [!] $msg" -ForegroundColor Yellow }
function Write-Fail($msg) { Write-Host "  [ERR] $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "  ==========================================" -ForegroundColor Cyan
Write-Host "            TorrentDrive Installer            " -ForegroundColor Cyan
Write-Host "  ==========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Fail "Docker is not installed. Install Docker Desktop from https://docs.docker.com/desktop/install/windows-install/"
}

try { docker info *> $null } catch {
    Write-Fail "Docker Desktop is not running. Start it and try again."
}
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Docker Desktop is not running. Start it and try again."
}
Write-Ok "Docker is ready"

foreach ($name in @($ContainerName, 'torrentdrive')) {
    $existing = docker ps -aq -f "name=^${name}$" 2>$null
    if ($existing) {
        Write-Info "Replacing existing container '$name' (data volumes are preserved)..."
        docker stop $name *> $null
        docker rm $name *> $null
    }
}

Write-Info "Pulling $Image..."
docker pull $Image
if ($LASTEXITCODE -ne 0) { Write-Fail "Could not pull $Image" }

Write-Info "Starting TorrentDrive..."
docker run -d `
    --name $ContainerName `
    --restart unless-stopped `
    -p "${AppPort}:47100" `
    -p 127.0.0.1:47200:47200 `
    -p 127.0.0.1:47500:47500 `
    -p 127.0.0.1:47600:47600 `
    -v torrentdrive-pgdata:/data/postgres `
    -v torrentdrive-redis:/data/redis `
    -v torrentdrive-downloads:/data/downloads `
    $Image | Out-Null

if ($LASTEXITCODE -ne 0) { Write-Fail "Could not start the container." }
Write-Ok "Container started"

Write-Info "Waiting for services to start (up to 90 seconds)..."
$elapsed = 0
$healthy = $false
while ($elapsed -lt 90) {
    try {
        Invoke-WebRequest -Uri "http://localhost:47200/api/health/ready" -UseBasicParsing -TimeoutSec 3 | Out-Null
        $healthy = $true
        break
    } catch {
        Start-Sleep -Seconds 3
        $elapsed += 3
        Write-Host "`r    $elapsed s elapsed..." -NoNewline -ForegroundColor DarkGray
    }
}
Write-Host "`r                            `r" -NoNewline

if ($healthy) {
    Write-Ok "All services are healthy"
} else {
    Write-Warn "Health check timed out - services may still be starting"
    Write-Host "  Run 'docker logs -f $ContainerName' to watch progress" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  ==========================================" -ForegroundColor Green
Write-Host "    TorrentDrive is running!" -ForegroundColor Green
Write-Host "  ==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Open this URL to claim your instance:"
Write-Host "  http://localhost:$AppPort" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Grafana (local):   http://localhost:47500" -ForegroundColor DarkGray
Write-Host "  Generated secrets: docker exec $ContainerName cat /data/postgres/secrets.env" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Logs:    docker logs -f $ContainerName" -ForegroundColor DarkGray
Write-Host "  Restart: docker restart $ContainerName" -ForegroundColor DarkGray
Write-Host "  Docs:    https://tc.gitnasr.com/docs" -ForegroundColor DarkGray
Write-Host ""
