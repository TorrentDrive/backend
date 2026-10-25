---
name: torrentdrive-sync
description: Cross-repo sync verification for TorrentDrive. Invoke before finishing any code, UI, configuration, or deployment edit across backend, frontend, website, or deploy to keep documentation, landing page components, domain glossary, and Docker/PaaS manifests synchronized.
---

# TorrentDrive Cross-Repo Sync (`torrentdrive-sync`)

TorrentDrive spans four coupled repositories (`backend`, `frontend`, `website`, `deploy`). A change in one repository is incomplete until its downstream surfaces in docs, the landing page, and deployment manifests are verified and updated.

## Step 1: Reconcile Domain Model & ADRs

Inspect [`backend/GLOSSARY.md`](file:///D:/Coding/TorrentDrive/backend/GLOSSARY.md) and [`backend/docs/adr/`](file:///D:/Coding/TorrentDrive/backend/docs/adr/) whenever your change touches:
- Job lifecycle states (`JobStatus`), Hangfire queue names (`torrents`, `googledrive`, `s3`), or worker boundaries (`IJobOrchestrator`, `IJobExecutionTracker`, `StorageTransferOrchestrator`).
- Storage routing, health probes (`StorageHealthStatus`), `Failover`, `Pinning`, `Staging Volume`, `Purge`, or `Instance Claiming`.
- BitTorrent engine settings (`TorrentDownloadJob`, `UdpTrackerScraper`, BEP 27 private torrent rules).

**Completion criterion**: Every added, renamed, or retired domain concept, queue, or module seam is updated in `backend/GLOSSARY.md` and, if architectural, captured in `backend/docs/adr/`.

## Step 2: Synchronize Documentation (`website/content/docs/` & `deploy/README.md`)

Match your change to the affected documentation pages in `D:\Coding\TorrentDrive\website\content\docs\`:
- **UI workflows, batch torrent analysis, file selection, pinning, or purge** → `usage.mdx`, `first-run.mdx`
- **Container processes, queues, workers, or BitTorrent engine** → `architecture.mdx`
- **Environment variables, `SystemSettings`, `UserSettings`, or ports** → `configuration.mdx`, `getting-started.mdx`, `security.mdx`, and `deploy/README.md`
- **Google Drive or S3 storage providers, health probes, or failover** → `providers/google-drive.mdx`, `providers/s3.mdx`, `providers/s3-compatible.mdx`
- **Metrics, logs, traces, or Grafana provisioning** → `grafana.mdx`
- **PaaS deployment or compose changes** → `paas/index.mdx`, `paas/dokploy.mdx`, `paas/coolify.mdx`, `paas/render.mdx`, `paas/railway.mdx`, and `deploy/paas/README.md`

**Completion criterion**: Every modified user flow, setting, API contract, port, or operational behavior is accurately described in `website/content/docs/` and `deploy/README.md` with zero stale claims.

## Step 3: Synchronize Landing Page (`website/src/components/landing/`)

Check the landing page whenever user-visible features, onboarding steps, install commands, or supported PaaS platforms change:
- **Install commands, ports, setup wizard, or PaaS badges** → `website/src/components/landing/HeroSection.tsx`
- **Core product capabilities (torrent engine, storage providers, failover, telemetry, packaging)** → `website/src/components/landing/FeatureCards.tsx`
- **End-to-end user workflow steps** → `website/src/components/landing/HowItWorks.tsx`
- **Frequently asked questions & JSON-LD schema** → `website/src/lib/faq.ts`

**Completion criterion**: `HeroSection.tsx`, `FeatureCards.tsx`, `HowItWorks.tsx`, and `faq.ts` match the current product behavior and installation commands.

## Step 4: Verify Deployment & PaaS Manifests (`deploy/`)

Every change to ports, volumes (`torrentdrive-pgdata`, `torrentdrive-redis`, `torrentdrive-downloads`), health endpoints (`/health/ready`, `/api/health/ready`), or startup environment variables must stay aligned across all deployment targets in `D:\Coding\TorrentDrive\deploy\`:
1. **One-command installers**: `install.sh` and `install.ps1`
2. **Docker Compose**: `docker-compose.yml`, `compose/docker-compose.yml`, and `compose/docker-compose.split.yml`
3. **Dokploy**: `paas/dokploy/docker-compose.yml` and `paas/dokploy/template.toml` (Traefik routes port `47100` only)
4. **Coolify**: `paas/coolify/docker-compose.yml` (`SERVICE_FQDN_TORRENTDRIVE_47100` and `SERVICE_URL_TORRENTDRIVE`)
5. **Render & Railway**: `paas/render/render.yaml` and `paas/railway/railway.json`

**Security guardrail**: Keep internal admin ports (`47200` API/Hangfire, `47500` Grafana, `47600` Prometheus) bound to `127.0.0.1` in host-published manifests and unrouted in PaaS manifests; only port `47100` (Next.js frontend with same-origin `/proxy/api` proxy) is public.

**Completion criterion**: All compose files, installers, and PaaS blueprints in `deploy/` agree on image reference, volume names, healthcheck paths, port bindings, and environment variables.

## Step 5: Build & Health Verification

Run verification on every modified repository before finishing:
- **Backend**: `dotnet build D:\Coding\TorrentDrive\backend\TorrentDrive.sln` exits `0` with `0 Error(s)`.
- **Frontend**: `D:\Coding\TorrentDrive\frontend` compiles cleanly with no undefined Tailwind tokens (`primary`, `surface`, `surface-tonal`, `success`, `warning`, `danger`, `info` only) and uses Radix UI primitives (`Switch`, `DropdownMenu`, `Tooltip`).

**Completion criterion**: All modified projects build cleanly and all four sync steps above are satisfied.