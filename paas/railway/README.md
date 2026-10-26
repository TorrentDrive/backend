# Railway

## What this directory is, and is not

`railway.json` configures a Railway service once one exists. It is **not** a
one-click deploy on its own — Railway's one-click path is a Template, which is
defined in Railway's own dashboard and published to their marketplace, not from
a file in a repository. Saying "Railway supported" because this file exists
would be overselling it.

So: this file plus the steps below is what "Railway support" currently means. A
published Template is the follow-up.

## Deploying

1. **New Project → Deploy from GitHub repo →** `TorrentDrive/backend`.
2. Railway builds the all-in-one `Dockerfile`. That build is slow — it compiles
   the .NET backend and the Next.js frontend from source. Deploying the
   prebuilt image instead is faster: **New → Docker Image →**
   `ghcr.io/torrentdrive/torrentdrive:latest`.
3. **Add a Volume**, mount path `/data`, at least 20 GB.
4. **Settings → Networking → Generate Domain**, then set the port to `47100`.

## The volume is not optional

Everything that must survive a redeploy lives under `/data`:

| Path | Contents |
|------|----------|
| `/data/postgres` | The database **and** the secrets generated on first boot |
| `/data/redis` | Job state |
| `/data/downloads` | In-flight torrent downloads |

Without a volume mounted at `/data`, every deploy starts a fresh database, and
the signing keys are regenerated — so every account and every storage connection
is gone. The app will look like a brand new install.

Mount **one** volume at `/data` rather than three. The three workers share
`/data/downloads`: the torrent worker writes finished files there and the Drive
and S3 workers read them back to upload. Railway volumes attach to a single
service, which is also why this is the all-in-one image rather than separate
services per worker — split up, the upload workers cannot see what the torrent
worker downloaded.

## Sizing

Nine processes plus PostgreSQL. Give it at least 2 GB of memory; it will be
killed during startup on less.

## Configuration

None required. The container generates its own secrets on first boot. If Railway's
generated domain does not reach the app correctly behind their proxy, set:

```
PUBLIC_FRONTEND_URL=https://your-app.up.railway.app
NEXTAUTH_URL=https://your-app.up.railway.app
```

Everything else is in the app under **Settings** — see
[the configuration reference](https://tc.gitnasr.com/docs/configuration).
