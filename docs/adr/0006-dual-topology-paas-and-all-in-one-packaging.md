# ADR-0006: Dual-Topology Deployment Architecture (PaaS Split vs. Single-Container All-in-One)

## Status
Accepted (2026-10-26)

## Context
TorrentDrive targets two distinct deployment personas:
1. **Self-hosted / Single-node users**: Need a zero-friction, one-command install (`curl -fsSL ... | bash` or `docker compose up -d`) where Next.js, .NET API, Hangfire background workers, PostgreSQL 15, and Redis 7 run together in a single container (`ghcr.io/torrentdrive/torrentdrive`) with auto-generated credentials and only port `47100` exposed.
2. **PaaS / Multi-service infrastructure (Dokploy @ admin.gitnasr.com)**: Runs in project `TorrenClou` where the Next.js frontend is deployed as a standalone application (`torrenclou-frontend-c7iw9a` at `torrent.secretzone.top`) and the backend runs as a multi-service Docker Compose stack (`torrenclou-backend-pxkivf`).

When commit `b66b7a1` replaced root `docker-compose.yml` with the all-in-one stack, Dokploy deployments broke because Dokploy's Traefik ingress was attached to service `api`, which no longer existed in `docker-compose.yml`. Furthermore, pushing the all-in-one image to `ghcr.io/torrentdrive/torrentdrive` failed in GitHub Actions due to lack of repository write permissions on the organization-level package namespace.

## Decision
1. **Preserve `docker-compose.dokploy.yml` at the Repository Root**:
   - Dedicated compose manifest tracked by Dokploy compose app `torrenclou-backend-pxkivf`.
   - Defines explicit microservices: `postgres`, `redis`, `api`, `torrent-worker`, `googledrive-worker`, `s3-worker`.
   - Pulls prebuilt multi-arch images (`ghcr.io/torrentdrive/backend/*:latest`) produced by GitHub Actions.
   - Routes ingress to service `api` on internal port `8080` via domain `torrenclou-backend-pxkivf-...sslip.io`.
   - Health checks ensure PostgreSQL and Redis are healthy before `api` runs migrations, and `api` is healthy before Hangfire workers start.
2. **Maintain Root `docker-compose.yml` for All-in-One Distribution**:
   - Uses `ghcr.io/torrentdrive/torrentdrive:latest` for single-node consumers, `install.sh`, and local testing.
   - Internal supervisor process manages all components with auto-generated secrets stored in `torrentdrive-pgdata`.
3. **Authorize Package Write Permissions in GitHub Organization**:
   - The `TorrentDrive` organization explicitly grants repository `TorrentDrive/backend` Write access to the top-level container package `ghcr.io/torrentdrive/torrentdrive` under Package Settings &rarr; Actions Access.

## Invariants for Agents & Engineers
- **Never rename or remove service `api` from `docker-compose.dokploy.yml`**: Dokploy's Traefik domain router is bound to this service name.
- **Never point Dokploy compose to root `docker-compose.yml`**: Root `docker-compose.yml` is reserved for all-in-one consumers and will collide with standalone frontend deployments and Dokploy environment variables.
- **Cross-Service Communication**: The standalone frontend application reaches the backend via `BACKEND_URL=https://torrenclou-backend-pxkivf-651603-46-4-96-211.sslip.io`.
