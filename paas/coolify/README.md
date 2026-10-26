# Deploying TorrentDrive on Coolify

TorrentDrive includes native Coolify magic variables (SERVICE_FQDN_TORRENTDRIVE_47100 and SERVICE_URL_TORRENTDRIVE) and service template metadata headers.

## Steps

1. In Coolify, open your project and click **+ New** -> **Docker Compose** (or **Git Repository**).
2. Set:
   - **Repository URL**: https://github.com/TorrentDrive/backend
   - **Branch**: main
   - **Docker Compose Location**: /paas/coolify/docker-compose.yml
3. Click **Deploy**. Coolify automatically assigns your domain (with https:// scheme via SERVICE_URL_TORRENTDRIVE), routes port 47100, and mounts persistent volumes for PostgreSQL, Redis, and the Staging Volume.
