# PaaS Blueprints

Deployment instructions live at **[tc.gitnasr.com/docs/paas](https://tc.gitnasr.com/docs/paas)** —
one page per platform. This directory holds the platform-specific manifests and templates:

| Directory | Files | Purpose |
|-----------|-------|---------|
| `dokploy/` | `docker-compose.yml`, `template.toml`, `README.md` | Dokploy Compose & 1-click template behind Traefik on port `47100` |
| `coolify/` | `docker-compose.yml`, `README.md` | Coolify Compose with `SERVICE_FQDN_TORRENTDRIVE_47100` & `SERVICE_URL_TORRENTDRIVE` magic variables |
| `render/` | `render.yaml` | Render Blueprint checking `/api/health/ready` on port `47100` with persistent `/data` disk |
| `railway/` | `railway.json`, `README.md` | Railway deployment config with `/data` volume |

## Why these all run the all-in-one image

A separate container per worker with managed PostgreSQL and Redis does not work on single-disk PaaS
platforms because **the three workers share a filesystem**: `torrent-worker` downloads into
`/data/downloads/<jobId>` and `gdrive-worker` / `s3-worker` stream those files to cloud storage.
Running the unified image with persistent storage at `/data` guarantees every worker sees the staged
files while persisting PostgreSQL, Redis, and `/data/postgres/secrets.env`.

For multi-container self-hosting on a Docker host where named volumes can be shared across
containers, use [`../compose/docker-compose.split.yml`](../compose/docker-compose.split.yml).