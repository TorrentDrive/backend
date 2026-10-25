# TorrentDrive Backend

The .NET 9 API and background workers behind
[TorrentDrive](https://tc.gitnasr.com) — self-hosted torrent-to-cloud.

> **Just want to run it?** You do not need this repo.
>
> <!-- snippet:install-linux -->
> ```bash
> curl -fsSL https://raw.githubusercontent.com/TorrentDrive/deploy/main/install.sh | bash
> ```
> <!-- /snippet -->
>
> Full documentation: **[tc.gitnasr.com/docs](https://tc.gitnasr.com/docs)**

## What lives in this repo

Clean Architecture, so the domain does not depend on frameworks or databases.

```
TorrentDrive.sln
├── TorrentDrive.Core/                 # Domain entities, interfaces, enums, options
├── TorrentDrive.Application/          # Use cases, DTOs, validators
├── TorrentDrive.Infrastructure/       # EF Core, Redis, Hangfire, external clients
├── TorrentDrive.API/                  # ASP.NET Core controllers, middleware, DI
├── TorrentDrive.Worker/               # Torrent downloads
├── TorrentDrive.GoogleDrive.Worker/   # Google Drive uploads
└── TorrentDrive.S3.Worker/            # S3 uploads
```

**Dependency flow:** `API / Workers` → `Application` → `Core` ← `Infrastructure`

Each worker is an independent process sharing the same PostgreSQL and Redis as
the API, pulling from its own Hangfire queue, and scalable on its own.

> The projects are still named `TorrentDrive.*` while the product is `TorrentDrive`.
> That rename is tracked separately — it touches every namespace and assembly,
> so it lands in one atomic change rather than piecemeal.

## Developing

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and
[Docker](https://docs.docker.com/get-docker/).

```bash
git clone https://github.com/TorrentDrive/backend.git
cd backend
docker compose up -d postgres redis
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
| [frontend](https://github.com/TorrentDrive/frontend) | Next.js web app |
| [website](https://github.com/TorrentDrive/website) | Documentation site — the canonical docs live here |
| [deploy](https://github.com/TorrentDrive/deploy) | All-in-one image, installer, CI |

## License

MIT — see [LICENSE](https://github.com/TorrentDrive/backend/blob/main/LICENSE).
