# Deploying TorrentDrive on Dokploy

TorrentDrive ships a ready-to-run Dokploy blueprint (docker-compose.yml + `template.toml`).

## Option A: Dokploy Compose Service (30 Seconds)

1. In Dokploy, open your project and click **Create Service** -> **Compose**.
2. Choose **Git** (or **Raw**):
   - **Repository URL**: https://github.com/TorrentDrive/backend.git
   - **Branch**: main
   - **Compose Path**: ./paas/dokploy/docker-compose.yml
3. Open the **Domains** tab, click **Add Domain**:
   - **Service Name**: `torrentdrive`
   - **Container Port**: 47100
   - Enable **HTTPS** (Let's Encrypt).
4. Click **Deploy**. Once green, open your domain to complete the First-Run Instance Claiming wizard.

## Option B: Dokploy 1-Click Template (`template.toml`)

`template.toml` automatically provisions the Traefik domain mapping on port 47100 and populates PUBLIC_FRONTEND_URL and NEXTAUTH_URL. Zero manual environment variables or secret generation required.
