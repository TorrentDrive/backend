# TorrentDrive Workspace Instructions (`AGENTS.md`)

TorrentDrive is a self-hosted torrent-to-cloud platform split across three repositories under `D:\Coding\TorrentDrive`:
- [`backend/`](file:///D:/Coding/TorrentDrive/backend) — .NET 9 Clean Architecture API, Hangfire orchestrator, hardened MonoTorrent worker (`torrent-worker`), cloud upload workers (`gdrive-worker`, `s3-worker`), unified all-in-one container (`Dockerfile`), root `docker-compose.yml`, Dokploy split compose (`docker-compose.dokploy.yml`), one-liner installers (`install.sh`, `install.ps1`), and 1-click PaaS blueprints (`paas/{dokploy,coolify,render,railway}`).
- [`frontend/`](file:///D:/Coding/TorrentDrive/frontend) — Next.js 16 App Router UI on port `47100`, proxying `/proxy/api/*` same-origin to the backend API (`:47200`) and exposing `/api/health/ready`.
- [`website/`](file:///D:/Coding/TorrentDrive/website) — Next.js 16 marketing landing page (`src/components/landing/`) and Fumadocs documentation (`content/docs/`).

## Context Pointers

- **Cross-repo sync** (mandatory before finishing any feature, UI, config, or deployment change): read and execute [`.agents/skills/torrentdrive-sync/SKILL.md`](.agents/skills/torrentdrive-sync/SKILL.md).
- **Architecture, deployment topology & CI/CD** (mandatory before touching Docker, Compose, Dokploy, or GitHub Actions): read and execute [`.agents/skills/torrentdrive-architecture/SKILL.md`](.agents/skills/torrentdrive-architecture/SKILL.md) and [`backend/docs/adr/0006-dual-topology-paas-and-all-in-one-packaging.md`](backend/docs/adr/0006-dual-topology-paas-and-all-in-one-packaging.md).
- **Domain vocabulary & ubiquitous language** (`Torrent`, `Job`, `Download Phase`, `Upload Phase`, `Stale Job`, `Force Start`, `Storage Provider`, `Storage Profile`, `Failover`, `Pinning`, `Staging Volume`, `Purge`, `Instance Claiming`): read [`backend/GLOSSARY.md`](file:///D:/Coding/TorrentDrive/backend/GLOSSARY.md).
- **Architecture Decision Records** (direct Hangfire queue dispatch, unified `StorageTransferOrchestrator`, `IJobExecutionTracker` separation, direct EF Core LINQ queries, continuous SemVer release pipeline, dual-topology deployment): read [`backend/docs/adr/`](file:///D:/Coding/TorrentDrive/backend/docs/adr/).

## Architectural & Security Invariants

1. **Deep Modules & Narrow Interfaces (`backend`)**:
   - Dispatch Hangfire jobs directly via `HangfireJobOrchestrator` using `[Queue("torrents")]`, `[Queue("googledrive")]`, and `[Queue("s3")]`.
   - Route all cloud uploads through `StorageTransferOrchestrator` + `IStorageUploadStrategy`; keep worker hosts (`GoogleDriveUploadJob`, `S3UploadJob`) as thin Hangfire entrypoints.
   - Keep user-facing job operations on `IJobOrchestrator` and worker lifecycle reporting on `IJobExecutionTracker`.
2. **BitTorrent Anonymity & Private Tracker Safety (`backend`)**:
   - When `torrent.IsPrivate` is `true` (BEP 27), disable DHT, PEX, Local Peer Discovery, and public fallback tracker injection/scraping.
   - Keep `AllowLocalPeerDiscovery = false` and `AllowPortForwarding = false` on `EngineSettingsBuilder` to prevent RFC 1918 multicast and UPnP/NAT-PMP noise on cloud VPS networks.
3. **Design System & UI Craft (`frontend`)**:
   - Use only tokens defined in `tailwind.config.ts` (`primary`, `surface`, `surface-tonal`, `success`, `warning`, `danger`, `info`, and shadcn semantic variables).
   - Use Radix `<Switch>` (`@/components/ui/switch`) for boolean toggles and shared `<StatsCard>` (`@/components/shared/StatsCard`) for KPI summary rows.
   - Never introduce `NEXT_PUBLIC_*` backend URLs; browser requests always use same-origin `/proxy/api/*`.
4. **Zero-Config & Loopback-First Deployment (`backend`)**:
   - `docker compose up -d` and `install.sh` / `install.ps1` must work with zero `.env` files; secrets are generated on first boot into `/data/postgres/secrets.env` inside the `torrentdrive-pgdata` volume.
   - Expose only port `47100` publicly. Keep `47200` (API/Hangfire), `47500` (Grafana), and `47600` (Prometheus) bound to `127.0.0.1` in host compose files and unrouted in PaaS templates (`dokploy`, `coolify`, `render`, `railway`).
5. **Dual Deployment Topologies (Dokploy PaaS vs. Single All-in-One)**:
   - Dokploy production deployment (`torrenclou-backend-pxkivf` on `admin.gitnasr.com`) tracks `./docker-compose.dokploy.yml` with dedicated microservices (`api`, 3 workers, Postgres 15, Redis 7).
   - Traefik ingress is bound to service **`api`** on internal port **`8080`**. NEVER remove or rename service `api` in `docker-compose.dokploy.yml`.
   - Standalone Next.js frontend (`torrent.secretzone.top`) communicates with backend via `BACKEND_URL=https://torrenclou-backend-pxkivf-651603-46-4-96-211.sslip.io`.
   - All-in-one distributions (`install.sh`, root `docker-compose.yml`) pull `ghcr.io/torrentdrive/torrentdrive:latest`, which builds via `.github/workflows/docker-build.yml` using organization-level write grants.
