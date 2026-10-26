# TorrentDrive Backend

The .NET 9 API, background workers, and all-in-one deployment files behind
[TorrentDrive](https://tc.gitnasr.com) — self-hosted torrent-to-cloud.

> **Just want to run it?**
>
> <!-- snippet:install-linux -->
> ```bash
> curl -fsSL https://raw.githubusercontent.com/TorrentDrive/backend/main/install.sh | bash
> ```
> <!-- /snippet -->
>
> Or run `docker compose up -d` from the root of this repository.
> Full documentation: **[tc.gitnasr.com/docs](https://tc.gitnasr.com/docs)**

## What lives in this repo

Clean Architecture, so the domain does not depend on frameworks or databases, plus the zero-config all-in-one container and PaaS blueprints.

```
TorrentDrive.sln
├── TorrentDrive.Core/                 # Domain entities, interfaces, enums, options
├── TorrentDrive.Application/          # Use cases, DTOs, validators
├── TorrentDrive.Infrastructure/       # EF Core, Redis, Hangfire, external clients
├── TorrentDrive.API/                  # ASP.NET Core controllers, middleware, DI
├── TorrentDrive.Worker/               # Torrent downloads
├── TorrentDrive.GoogleDrive.Worker/   # Google Drive uploads
├── TorrentDrive.S3.Worker/            # S3 uploads
├── Dockerfile                         # Unified all-in-one container image
├── docker-compose.yml                 # Zero-config single-command stack
├── compose/                           # Split, dev, and local-build compose topologies
└── paas/                              # 1-click blueprints (Dokploy, Coolify, Render, Railway)
```

**Dependency flow:** `API / Workers` → `Application` → `Core` ← `Infrastructure`

Each worker is an independent process sharing the same PostgreSQL and Redis as
the API, pulling from its own Hangfire queue, and scalable on its own.

## Developing

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and
[Docker](https://docs.docker.com/get-docker/).

```bash
git clone https://github.com/TorrentDrive/backend.git
cd backend
docker compose -f compose/docker-compose.local-build.yml up -d postgres redis
dotnet run --project TorrentDrive.API
```

The API listens on `http://localhost:47200`. No `.env` is needed — compose and
the app both fall back to working development defaults. Copy `.env.example` to
`.env` only if you want to override something.

Workers run the same way, in their own terminals:

```bash
dotnet run --project TorrentDrive.Worker
dotnet run --project TorrentDrive.GoogleDrive.Worker
dotnet run --project TorrentDrive.S3.Worker
```

The API reference and every configuration key are documented at
[tc.gitnasr.com/docs](https://tc.gitnasr.com/docs) — they are generated from this
code, so they are not restated here.

## Repositories

| Repository | Contents |
|------------|----------|
| [backend](https://github.com/TorrentDrive/backend) | .NET 9 API, workers, all-in-one image, installer, and PaaS blueprints |
| [frontend](https://github.com/TorrentDrive/frontend) | Next.js web app |
| [website](https://github.com/TorrentDrive/website) | Documentation site — the canonical docs live here |

## License

MIT — see [LICENSE](https://github.com/TorrentDrive/backend/blob/main/LICENSE).
