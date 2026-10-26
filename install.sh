#!/usr/bin/env bash
#
# TorrentDrive one-command installer.
#
#   curl -fsSL https://raw.githubusercontent.com/TorrentDrive/backend/main/install.sh | bash
#
# Pulls the all-in-one image, starts it with persistent volumes, and prints the URL
# to open. Zero configuration files required: secrets are generated automatically on
# first boot and stored in /data/postgres/secrets.env.
#
set -euo pipefail

IMAGE="${TORRENTDRIVE_IMAGE:-ghcr.io/torrentdrive/torrentdrive:latest}"
CONTAINER_NAME="${TORRENTDRIVE_CONTAINER:-torrentdrive}"
APP_PORT="${FRONTEND_PORT:-47100}"

BOLD='\033[1m'; CYAN='\033[0;36m'; GREEN='\033[0;32m'
YELLOW='\033[1;33m'; RED='\033[0;31m'; DIM='\033[2m'; NC='\033[0m'

info()    { echo -e "${CYAN}  -> $1${NC}"; }
success() { echo -e "${GREEN}  [OK] $1${NC}"; }
warn()    { echo -e "${YELLOW}  [!] $1${NC}"; }
fail()    { echo -e "${RED}  [ERR] $1${NC}"; exit 1; }

echo ""
echo -e "${CYAN}${BOLD}  ==============================================${NC}"
echo -e "${CYAN}${BOLD}              TorrentDrive Installer              ${NC}"
echo -e "${CYAN}${BOLD}  ==============================================${NC}"
echo ""

# --- Prerequisites --------------------------------------------------
command -v docker >/dev/null 2>&1 \
    || fail "Docker is not installed. Install it from https://docs.docker.com/get-docker/"

docker info >/dev/null 2>&1 \
    || fail "Docker daemon is not running. Start Docker and try again."

success "Docker is ready"

# --- Replace any existing container (volumes are kept) --------------
for name in "$CONTAINER_NAME" "torrentdrive"; do
    if docker ps -aq -f "name=^${name}$" | grep -q .; then
        info "Replacing existing container '$name' (data volumes are preserved)..."
        docker stop "$name" >/dev/null 2>&1 || true
        docker rm "$name" >/dev/null 2>&1 || true
    fi
done

info "Pulling ${IMAGE}..."
docker pull "$IMAGE"

info "Starting TorrentDrive..."
# Only port 47100 (web UI + same-origin API proxy) is exposed on 0.0.0.0.
# Internal API/Hangfire (47200), Grafana (47500), and Prometheus (47600) are bound
# strictly to 127.0.0.1 so public VPS deployments never leak unauthenticated endpoints.
docker run -d \
    --name "$CONTAINER_NAME" \
    --restart unless-stopped \
    -p "${APP_PORT}:47100" \
    -p 127.0.0.1:47200:47200 \
    -p 127.0.0.1:47500:47500 \
    -p 127.0.0.1:47600:47600 \
    -v torrentdrive-pgdata:/data/postgres \
    -v torrentdrive-redis:/data/redis \
    -v torrentdrive-downloads:/data/downloads \
    "$IMAGE" >/dev/null

success "Container started"

# --- Wait for readiness ---------------------------------------------
info "Waiting for services to become ready (up to 90 seconds)..."
ELAPSED=0
while [ $ELAPSED -lt 90 ]; do
    if curl -sf "http://127.0.0.1:47200/api/health/ready" >/dev/null 2>&1; then
        break
    fi
    sleep 3
    ELAPSED=$((ELAPSED + 3))
    printf "\r  ${DIM}  %ds elapsed...${NC}" "$ELAPSED"
done
printf "\r                            \r"

if [ $ELAPSED -ge 90 ]; then
    warn "Health check timed out — services may still be initializing"
    echo -e "  ${DIM}Run 'docker logs -f ${CONTAINER_NAME}' to watch progress${NC}"
else
    success "All services are healthy"
fi

# --- Print access URL -----------------------------------------------
SERVER_IP="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
[ -z "$SERVER_IP" ] && SERVER_IP="$(ip route get 1 2>/dev/null | awk '/src/{print $7}' | head -1 || true)"
[ -z "$SERVER_IP" ] && SERVER_IP="$(curl -sf --max-time 3 https://api.ipify.org 2>/dev/null || true)"

if [ -n "$SERVER_IP" ]; then
    APP_URL="http://$(echo "$SERVER_IP" | tr '.' '-').sslip.io:${APP_PORT}"
else
    APP_URL="http://localhost:${APP_PORT}"
fi

echo ""
echo -e "${GREEN}${BOLD}  ==============================================${NC}"
echo -e "${GREEN}${BOLD}    TorrentDrive is running!${NC}"
echo -e "${GREEN}${BOLD}  ==============================================${NC}"
echo ""
echo -e "  ${BOLD}Open this URL to claim your instance:${NC}"
echo -e "  ${CYAN}${APP_URL}${NC}"
echo ""
echo -e "  ${DIM}Local access:     http://localhost:${APP_PORT}${NC}"
echo -e "  ${DIM}Grafana (local):  http://127.0.0.1:47500${NC}"
echo -e "  ${DIM}Generated secrets: docker exec ${CONTAINER_NAME} cat /data/postgres/secrets.env${NC}"
echo ""
echo -e "  ${DIM}Logs:    docker logs -f ${CONTAINER_NAME}${NC}"
echo -e "  ${DIM}Restart: docker restart ${CONTAINER_NAME}${NC}"
echo -e "  ${DIM}Docs:    https://tc.gitnasr.com/docs${NC}"
echo ""
