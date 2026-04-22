# NewsParser — Production Deployment Plan

**Date:** 2026-04-22
**Target architecture:**

```
React SPA          → Cloudflare Pages
DNS + TLS + WAF    → Cloudflare
API + Worker       → Oracle Cloud Always Free VM (ARM Ampere A1, EU)
                     reachable via Cloudflare Tunnel (no public ports)
PostgreSQL + R2    → already deployed (out of scope here)
CI/CD              → GitHub Actions
Secrets            → GitHub Actions encrypted secrets → injected at deploy
```

**Cost target:** €0/month compute, only domain (~$10/yr at Cloudflare Registrar).

---

## Part A — One-time prerequisites

### A1. Domain registration (Cloudflare Registrar)

1. Sign up at `https://dash.cloudflare.com` with a strong, unique password and **turn on 2FA immediately** (TOTP, not SMS).
2. Go to **Registrar → Register Domains**, search for the domain you want. Cloudflare sells at wholesale cost (no markup, no renewal gouging).
3. Complete purchase. DNS and nameservers are auto-configured on Cloudflare — nothing to point anywhere.
4. In the zone's **SSL/TLS** tab, set encryption mode to **Full (strict)**. This is required once the tunnel is up.
5. In **SSL/TLS → Edge Certificates**, enable **Always Use HTTPS** and **Automatic HTTPS Rewrites**.

### A2. Oracle Cloud account + VM

1. Sign up at `https://cloud.oracle.com/free`. You'll need a credit card for identity verification (nothing is charged on the Always Free tier, but Oracle requires it).
2. Pick an EU home region. Recommended order by Ampere A1 capacity availability in 2026: **Madrid → Stockholm → Milan → Paris → Amsterdam → Frankfurt**. Frankfurt is the most contested.
3. Enable 2FA on your Oracle account (Identity → Users → your user → Enable MFA).
4. Provision the VM:
   - **Compute → Instances → Create Instance**
   - Name: `newsparser-prod`
   - Image: **Canonical Ubuntu 24.04 LTS** (ARM-compatible)
   - Shape: **VM.Standard.A1.Flex**, **4 OCPU, 24 GB RAM** (uses your full Always Free quota in one VM)
   - Networking: default VCN, assign public IPv4 (we'll close it later via tunnel, but you need it for first SSH)
   - SSH keys: paste your public key (generate with `ssh-keygen -t ed25519` if you don't have one)
5. If you hit "Out of capacity," either switch region or script retries with OCI CLI — don't give up after one try, it's common.
6. Once running, note the public IP. SSH in once as `ubuntu@<ip>` to confirm access.

### A3. Initial VM hardening (run on the VM)

```bash
# 1. System update
sudo apt update && sudo apt upgrade -y

# 2. Create deploy user (non-root, no sudo needed for app ops)
sudo adduser --disabled-password --gecos "" deploy
sudo mkdir -p /home/deploy/.ssh
sudo cp ~/.ssh/authorized_keys /home/deploy/.ssh/
sudo chown -R deploy:deploy /home/deploy/.ssh
sudo chmod 700 /home/deploy/.ssh && sudo chmod 600 /home/deploy/.ssh/authorized_keys

# 3. Harden SSH
sudo sed -i 's/^#\?PasswordAuthentication .*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo sed -i 's/^#\?PermitRootLogin .*/PermitRootLogin no/' /etc/ssh/sshd_config
sudo systemctl restart ssh

# 4. Firewall — block everything inbound except SSH (cloudflared tunnels outbound)
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw enable
# Also clear Oracle's iptables rules that ship with Ubuntu images:
sudo iptables -F INPUT
sudo netfilter-persistent save

# 5. Automatic security updates
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades

# 6. fail2ban
sudo apt install -y fail2ban
sudo systemctl enable --now fail2ban
```

### A4. Install runtime dependencies

```bash
# .NET 10 runtime (ARM64) — ASP.NET Core + Worker don't need the full SDK on the server,
# we publish self-contained or framework-dependent from CI.
# Framework-dependent is smaller; install the runtime:
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
dotnet --list-runtimes  # verify Microsoft.AspNetCore.App 10.0.x and Microsoft.NETCore.App 10.0.x

# cloudflared (Cloudflare Tunnel client)
curl -L https://pkg.cloudflare.com/cloudflare-main.gpg | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo 'deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main' | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt update && sudo apt install -y cloudflared
```

### A5. Cloudflare Tunnel setup

The tunnel makes your API reachable at `https://api.yourdomain.com` with Cloudflare terminating TLS and proxying to the VM via an outbound connection. **No inbound ports open**, no public IP exposure, no Let's Encrypt headache.

```bash
# On the VM, as the deploy user:
sudo -u deploy cloudflared tunnel login          # opens a URL — complete auth in browser
sudo -u deploy cloudflared tunnel create newsparser
# Outputs tunnel ID + credentials file at /home/deploy/.cloudflared/<UUID>.json
```

Create `/home/deploy/.cloudflared/config.yml`:

```yaml
tunnel: <UUID>
credentials-file: /home/deploy/.cloudflared/<UUID>.json

ingress:
  - hostname: api.yourdomain.com
    service: http://localhost:5000
    originRequest:
      noTLSVerify: true
  - service: http_status:404
```

Route DNS + install as system service:

```bash
sudo -u deploy cloudflared tunnel route dns newsparser api.yourdomain.com
sudo cloudflared service install   # runs as root, reads config from /home/deploy/.cloudflared/
sudo systemctl enable --now cloudflared
sudo systemctl status cloudflared  # should show "Connection registered"
```

Verify in Cloudflare dashboard: **Zero Trust → Networks → Tunnels** should show `newsparser` as *Healthy*.

---

## Part B — Secrets strategy

### B1. Inventory

The following secrets must reach the runtime. **None** belong in Git, `appsettings.json`, or Docker images.

| Secret | Consumed by | ASP.NET Core env var name |
|---|---|---|
| Postgres connection string | Api, Worker | `ConnectionStrings__NewsParserDbContext` |
| JWT signing key (min 32 bytes) | Api | `Jwt__SecretKey` |
| JWT issuer / audience | Api | `Jwt__Issuer`, `Jwt__Audience` |
| Anthropic API key | Worker | `Ai__Anthropic__ApiKey` |
| Gemini API key | Worker | `Ai__Gemini__ApiKey` |
| Telegram bot token | Worker | `Telegram__BotToken` |
| Telegram API ID / hash / phone | Worker | `Telegram__ApiId`, `Telegram__ApiHash`, `Telegram__PhoneNumber` |
| Cloudflare R2 credentials | Api, Worker | `CloudflareR2__AccountId`, `CloudflareR2__AccessKeyId`, `CloudflareR2__SecretAccessKey`, `CloudflareR2__BucketName`, `CloudflareR2__PublicBaseUrl` |

ASP.NET Core's configuration system maps env vars with `__` to config-file `:` automatically, so `Ai__Anthropic__ApiKey` lands as `Ai:Anthropic:ApiKey` at runtime. No code changes needed.

### B2. Storage in GitHub

1. In the repo: **Settings → Secrets and variables → Actions → New repository secret**.
2. Add every row from B1 as a separate secret. Use **Environments** (Settings → Environments → *production*) and put them there so you can add **required reviewers** on production deploys — one click of protection against accidental `main` pushes overwriting prod.
3. Also add infrastructure secrets the workflow needs:
   - `OCI_HOST` — the tunnel hostname or VM private host
   - `OCI_SSH_USER` — `deploy`
   - `OCI_SSH_KEY` — private SSH key (ed25519) the runner uses to deploy. **Generate a dedicated deploy key**, don't reuse your personal one. Add the matching public key to `/home/deploy/.ssh/authorized_keys`.
   - `CLOUDFLARE_API_TOKEN` — scoped token for Pages deploys (create in Cloudflare dash → My Profile → API Tokens → Create Token → **Cloudflare Pages — Edit** template, scoped to one account)
   - `CLOUDFLARE_ACCOUNT_ID` — from Cloudflare dashboard home

### B3. Flow at deploy time

Production secrets never sit on the VM at rest in plaintext Git, but they **do** need to exist as files readable by the services. The pipeline:

1. GitHub Actions receives the push to `main`.
2. Runner checks out code, builds .NET and React artifacts.
3. Runner writes an env file **in memory on the runner**, SCPs it over to `/etc/newsparser/api.env` and `/etc/newsparser/worker.env` with mode `0600`, owner `root:deploy`.
4. systemd reads those files via `EnvironmentFile=` directive.
5. Runner SSHs in, stops the service, swaps the binary, starts the service.

The env files end up on disk with `chmod 0600` and `root:deploy` ownership — only root and the service user can read them. This is the standard pattern; it's secure against everything except VM compromise, at which point all bets are off anyway.

### B4. Rotation drill (do this the first time to prove it works)

- Rotate a secret in GitHub Secrets.
- Re-run the deployment workflow.
- New env file lands, service restarts, old value is gone.

Budget 10 minutes end-to-end. If any secret takes longer than that to rotate, the workflow isn't finished.

### B5. What **never** to do

- Don't commit `appsettings.Development.json` or any `appsettings.*.json` with real secrets. Check `.gitignore` covers `appsettings.*.Local.json` or similar.
- Don't echo secrets in CI logs. GitHub Actions auto-masks registered secrets, but don't write scripts that `echo $SECRET` or append them to files printed to stdout.
- Don't bake secrets into build artifacts (`.dll`, published output, Docker images).
- Don't share one JWT signing key across environments — generate a new random one for prod: `openssl rand -base64 48`.

---

## Part C — React UI → Cloudflare Pages

### C1. Build-time environment variables

Vite exposes any env var prefixed with `VITE_` to the bundle. **These are public** — they end up in JavaScript the browser downloads. Put only non-sensitive values there:

- `VITE_API_BASE_URL` = `https://api.yourdomain.com`

Never put API keys, JWT secrets, or anything truly secret in `VITE_*` — if it's in the bundle, anyone can read it.

### C2. Connect the repo to Cloudflare Pages

1. Cloudflare dashboard → **Workers & Pages → Create → Pages → Connect to Git**.
2. Authorize GitHub, pick the NewsParser repo.
3. Production branch: `main`. Build config:
   - **Build command:** `cd UI && npm ci && npm run build`
   - **Build output directory:** `UI/dist`
   - **Root directory:** (leave blank, repo root)
   - **Environment variables:** add `VITE_API_BASE_URL=https://api.yourdomain.com` for *Production*. Add a second one for *Preview* if you want preview builds to hit a staging API.
4. Save & deploy. First build runs; verify at the auto-generated `*.pages.dev` URL.

### C3. Custom domain

1. In the Pages project: **Custom domains → Set up a custom domain → `app.yourdomain.com`** (or apex `yourdomain.com`).
2. Cloudflare wires up DNS automatically since the zone is on Cloudflare. Certificate issues within a few minutes.
3. Test: `https://app.yourdomain.com` loads the SPA.

### C4. SPA routing fix

React Router client-side routes 404 on refresh unless you add a fallback. Create `UI/public/_redirects`:

```
/*  /index.html  200
```

Commit, redeploy.

### C5. Security headers

Create `UI/public/_headers`:

```
/*
  X-Frame-Options: DENY
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: geolocation=(), microphone=(), camera=()
  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://*.r2.dev https://<your-r2-public-host>; connect-src 'self' https://api.yourdomain.com; font-src 'self'; frame-ancestors 'none'
```

Tighten CSP to exactly the origins you use — `img-src` needs your R2 public URL, `connect-src` needs the API host.

---

## Part D — .NET API → Oracle VM

### D1. Prepare the VM (once)

```bash
# On VM, as root:
sudo mkdir -p /opt/newsparser/api /etc/newsparser /var/log/newsparser
sudo chown -R deploy:deploy /opt/newsparser /var/log/newsparser
sudo chown root:deploy /etc/newsparser
sudo chmod 750 /etc/newsparser   # deploy can list, only root can write defaults
```

### D2. systemd unit for the API

`/etc/systemd/system/newsparser-api.service`:

```ini
[Unit]
Description=NewsParser API
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/newsparser/api
ExecStart=/usr/local/bin/dotnet /opt/newsparser/api/Api.dll
Restart=always
RestartSec=5
User=deploy
Group=deploy
EnvironmentFile=/etc/newsparser/api.env
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/newsparser/api /var/log/newsparser
CapabilityBoundingSet=
AmbientCapabilities=

[Install]
WantedBy=multi-user.target
```

Bind to `127.0.0.1:5000` — the tunnel picks it up from localhost, nothing else can.

```bash
sudo systemctl daemon-reload
sudo systemctl enable newsparser-api
# Don't start yet — binaries and env file land via CI below.
```

### D3. GitHub Actions workflow for API

`.github/workflows/deploy-api.yml`:

```yaml
name: Deploy API

on:
  push:
    branches: [main]
    paths:
      - 'Api/**'
      - 'Core/**'
      - 'Infrastructure/**'
      - 'Directory.Build.props'
      - 'NewsParser.slnx'
      - '.github/workflows/deploy-api.yml'
  workflow_dispatch:

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore & test
        run: |
          dotnet restore
          dotnet test Tests/Api.Tests/Api.Tests.csproj --no-restore

      - name: Publish (linux-arm64, framework-dependent)
        run: |
          dotnet publish Api/Api.csproj \
            -c Release \
            -r linux-arm64 \
            --self-contained false \
            -o publish/api

      - name: Write env file
        run: |
          umask 077
          cat > api.env <<'EOF'
          ConnectionStrings__NewsParserDbContext=${{ secrets.DB_CONNECTION_STRING }}
          Jwt__SecretKey=${{ secrets.JWT_SECRET_KEY }}
          Jwt__Issuer=${{ secrets.JWT_ISSUER }}
          Jwt__Audience=${{ secrets.JWT_AUDIENCE }}
          Jwt__ExpirationHours=24
          CloudflareR2__AccountId=${{ secrets.R2_ACCOUNT_ID }}
          CloudflareR2__AccessKeyId=${{ secrets.R2_ACCESS_KEY_ID }}
          CloudflareR2__SecretAccessKey=${{ secrets.R2_SECRET_ACCESS_KEY }}
          CloudflareR2__BucketName=${{ secrets.R2_BUCKET_NAME }}
          CloudflareR2__PublicBaseUrl=${{ secrets.R2_PUBLIC_BASE_URL }}
          EOF

      - name: Set up SSH
        run: |
          mkdir -p ~/.ssh
          echo "${{ secrets.OCI_SSH_KEY }}" > ~/.ssh/id_ed25519
          chmod 600 ~/.ssh/id_ed25519
          ssh-keyscan -H ${{ secrets.OCI_HOST }} >> ~/.ssh/known_hosts

      - name: Upload env file (root-owned, 0600)
        run: |
          scp -i ~/.ssh/id_ed25519 api.env \
            ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }}:/tmp/api.env
          ssh -i ~/.ssh/id_ed25519 ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }} \
            "sudo install -m 0600 -o root -g deploy /tmp/api.env /etc/newsparser/api.env && rm /tmp/api.env"

      - name: Upload binaries
        run: |
          tar czf api.tar.gz -C publish/api .
          scp -i ~/.ssh/id_ed25519 api.tar.gz \
            ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }}:/tmp/api.tar.gz

      - name: Swap binaries & restart
        run: |
          ssh -i ~/.ssh/id_ed25519 ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }} bash <<'EOF'
            set -euo pipefail
            sudo systemctl stop newsparser-api
            rm -rf /opt/newsparser/api/*
            tar xzf /tmp/api.tar.gz -C /opt/newsparser/api
            rm /tmp/api.tar.gz
            sudo systemctl start newsparser-api
            sleep 3
            sudo systemctl is-active newsparser-api
          EOF

      - name: Smoke test
        run: |
          curl -fsS --retry 5 --retry-delay 3 https://api.yourdomain.com/health || exit 1
```

### D4. CORS

Add `https://app.yourdomain.com` to the API's CORS allow-list in `Program.cs`. The reviewer agent will flag it if missing.

### D5. First deploy

Push a tiny change to `Api/`, watch the Actions tab. First run takes ~3 min. Verify:

```bash
curl -I https://api.yourdomain.com/health
# 200 OK → tunnel + service up
```

---

## Part E — .NET Worker → Oracle VM

Same machine, separate systemd service, no inbound traffic (worker doesn't listen on a port).

### E1. Prepare paths

```bash
sudo mkdir -p /opt/newsparser/worker
sudo chown deploy:deploy /opt/newsparser/worker
```

### E2. systemd unit

`/etc/systemd/system/newsparser-worker.service`:

```ini
[Unit]
Description=NewsParser Worker
After=network.target newsparser-api.service
Wants=network.target

[Service]
Type=notify
WorkingDirectory=/opt/newsparser/worker
ExecStart=/usr/local/bin/dotnet /opt/newsparser/worker/Worker.dll
Restart=always
RestartSec=10
User=deploy
Group=deploy
EnvironmentFile=/etc/newsparser/worker.env
Environment=DOTNET_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/newsparser/worker /var/log/newsparser
CapabilityBoundingSet=
AmbientCapabilities=

[Install]
WantedBy=multi-user.target
```

Note `DOTNET_ENVIRONMENT` (not `ASPNETCORE_ENVIRONMENT`) — Worker uses the Generic Host.

```bash
sudo systemctl daemon-reload
sudo systemctl enable newsparser-worker
```

### E3. GitHub Actions workflow for Worker

`.github/workflows/deploy-worker.yml`:

```yaml
name: Deploy Worker

on:
  push:
    branches: [main]
    paths:
      - 'Worker/**'
      - 'Core/**'
      - 'Infrastructure/**'
      - 'Directory.Build.props'
      - 'NewsParser.slnx'
      - '.github/workflows/deploy-worker.yml'
  workflow_dispatch:

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore & test
        run: |
          dotnet restore
          dotnet test Tests/Worker.Tests/Worker.Tests.csproj --no-restore

      - name: Publish (linux-arm64)
        run: |
          dotnet publish Worker/Worker.csproj \
            -c Release \
            -r linux-arm64 \
            --self-contained false \
            -o publish/worker

      - name: Write env file
        run: |
          umask 077
          cat > worker.env <<'EOF'
          ConnectionStrings__NewsParserDbContext=${{ secrets.DB_CONNECTION_STRING }}
          Ai__Anthropic__ApiKey=${{ secrets.ANTHROPIC_API_KEY }}
          Ai__Gemini__ApiKey=${{ secrets.GEMINI_API_KEY }}
          Telegram__BotToken=${{ secrets.TELEGRAM_BOT_TOKEN }}
          Telegram__ApiId=${{ secrets.TELEGRAM_API_ID }}
          Telegram__ApiHash=${{ secrets.TELEGRAM_API_HASH }}
          Telegram__PhoneNumber=${{ secrets.TELEGRAM_PHONE_NUMBER }}
          CloudflareR2__AccountId=${{ secrets.R2_ACCOUNT_ID }}
          CloudflareR2__AccessKeyId=${{ secrets.R2_ACCESS_KEY_ID }}
          CloudflareR2__SecretAccessKey=${{ secrets.R2_SECRET_ACCESS_KEY }}
          CloudflareR2__BucketName=${{ secrets.R2_BUCKET_NAME }}
          CloudflareR2__PublicBaseUrl=${{ secrets.R2_PUBLIC_BASE_URL }}
          EOF

      - name: Set up SSH
        run: |
          mkdir -p ~/.ssh
          echo "${{ secrets.OCI_SSH_KEY }}" > ~/.ssh/id_ed25519
          chmod 600 ~/.ssh/id_ed25519
          ssh-keyscan -H ${{ secrets.OCI_HOST }} >> ~/.ssh/known_hosts

      - name: Upload env file
        run: |
          scp -i ~/.ssh/id_ed25519 worker.env \
            ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }}:/tmp/worker.env
          ssh -i ~/.ssh/id_ed25519 ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }} \
            "sudo install -m 0600 -o root -g deploy /tmp/worker.env /etc/newsparser/worker.env && rm /tmp/worker.env"

      - name: Upload binaries & restart
        run: |
          tar czf worker.tar.gz -C publish/worker .
          scp -i ~/.ssh/id_ed25519 worker.tar.gz \
            ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }}:/tmp/worker.tar.gz
          ssh -i ~/.ssh/id_ed25519 ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }} bash <<'EOF'
            set -euo pipefail
            sudo systemctl stop newsparser-worker
            rm -rf /opt/newsparser/worker/*
            tar xzf /tmp/worker.tar.gz -C /opt/newsparser/worker
            rm /tmp/worker.tar.gz
            sudo systemctl start newsparser-worker
            sleep 3
            sudo systemctl is-active newsparser-worker
          EOF

      - name: Verify worker is processing
        run: |
          ssh -i ~/.ssh/id_ed25519 ${{ secrets.OCI_SSH_USER }}@${{ secrets.OCI_HOST }} \
            "journalctl -u newsparser-worker -n 50 --no-pager"
```

### E4. sudo permissions for the deploy user

The workflow needs the `deploy` user to run `systemctl stop/start` and `install` without a password prompt. Add `/etc/sudoers.d/newsparser-deploy`:

```
deploy ALL=(root) NOPASSWD: /bin/systemctl stop newsparser-api, /bin/systemctl start newsparser-api, /bin/systemctl stop newsparser-worker, /bin/systemctl start newsparser-worker, /usr/bin/install -m 0600 -o root -g deploy /tmp/api.env /etc/newsparser/api.env, /usr/bin/install -m 0600 -o root -g deploy /tmp/worker.env /etc/newsparser/worker.env
```

Lock down to **exactly these commands** — don't give the deploy user full sudo.

```bash
sudo visudo -cf /etc/sudoers.d/newsparser-deploy   # syntax-check
```

---

## Part F — UI CI/CD (deploy-on-merge)

Cloudflare Pages' Git integration (set up in C2) already auto-deploys on push. No GitHub Action needed unless you want test gating before deploy:

`.github/workflows/ui-tests.yml` (optional, runs before Pages builds):

```yaml
name: UI Tests

on:
  pull_request:
    paths: ['UI/**']
  push:
    branches: [main]
    paths: ['UI/**']

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
          cache-dependency-path: UI/package-lock.json
      - run: cd UI && npm ci
      - run: cd UI && npm run lint
      - run: cd UI && npm run typecheck
      - run: cd UI && npm test -- --run
      - run: cd UI && npm run build
```

Pair this with a **branch protection rule** on `main` requiring the `UI Tests` check to pass. Pages only deploys once `main` advances.

---

## Part G — Verification & rollback

### G1. Post-deploy smoke checklist

- `curl https://app.yourdomain.com` → React index loads.
- `curl https://api.yourdomain.com/health` → 200.
- Browser: open app, log in, confirm JWT round-trip works.
- `journalctl -u newsparser-api -n 100 --no-pager` → no exceptions.
- `journalctl -u newsparser-worker -n 100 --no-pager` → RSS fetch loop running, AI calls succeeding.
- Open a DB session, watch `Article` rows grow over the next few minutes.

### G2. Rollback

**Binaries:** each deploy keeps the previous tarball under `/opt/newsparser/<service>.previous.tar.gz` (add this line to the workflow: `cp /tmp/api.tar.gz /opt/newsparser/api.previous.tar.gz` before the swap). To roll back:

```bash
sudo systemctl stop newsparser-api
rm -rf /opt/newsparser/api/*
tar xzf /opt/newsparser/api.previous.tar.gz -C /opt/newsparser/api
sudo systemctl start newsparser-api
```

**DB migrations:** DbUp is forward-only. If a migration is bad, write a compensating migration — don't try to roll a DbUp migration backwards.

---

## Part H — Day-2 operations

### H1. Logs

For v1, `journalctl -u newsparser-api -f` is sufficient. When it isn't:

- Free option: **Grafana Cloud** free tier (50 GB logs/month, 14 days retention) — install Grafana Alloy as a systemd service, point it at `journalctl`.
- Alternative: **Better Stack** (ex-Logtail) 3 GB/mo free tier.

### H2. Uptime / health

Cloudflare's **Health Checks** (free on all plans) can ping `https://api.yourdomain.com/health` from multiple regions and email on failure. Set it up: Cloudflare dashboard → **Traffic → Health Checks**.

### H3. Backups

You said Postgres is already deployed elsewhere — confirm backups are on and tested. Run a restore drill on a throwaway DB within the first month of going live. A backup you haven't restored is a belief, not a backup.

### H4. Secret rotation cadence

- **Every 90 days:** JWT secret, Anthropic/Gemini keys, R2 keys.
- **On any suspected exposure:** immediately.
- Rotation = update GitHub Secret → re-run deploy workflow. See B4.

### H5. VM patching

`unattended-upgrades` handles security patches automatically. For kernel reboots:

```bash
# Check if reboot required:
[ -f /var/run/reboot-required ] && echo "REBOOT NEEDED"
```

Schedule a monthly reboot window. Both services will restart automatically via systemd.

---

## Execution order (checklist)

1. ☐ A1 — buy domain, enable Cloudflare 2FA
2. ☐ A2 — provision OCI VM
3. ☐ A3 — harden VM
4. ☐ A4 — install .NET 10 + cloudflared
5. ☐ A5 — create tunnel + DNS route, verify *Healthy* in Cloudflare dash
6. ☐ D1 — create directories on VM
7. ☐ D2 — install API systemd unit
8. ☐ E1, E2 — install Worker systemd unit
9. ☐ E4 — lock-down sudoers
10. ☐ B2 — register all GitHub Secrets under `production` environment
11. ☐ D3 — commit API workflow, push, verify first deploy
12. ☐ E3 — commit Worker workflow, push, verify first deploy
13. ☐ C2, C3, C4, C5 — set up Cloudflare Pages with custom domain, `_redirects`, `_headers`
14. ☐ F — optional UI test workflow + branch protection
15. ☐ G1 — full smoke test
16. ☐ B4 — rotate one secret end-to-end as a drill
17. ☐ H2 — enable Cloudflare Health Check + email alert
18. ☐ H3 — verify Postgres backup & run restore drill

---

## Open questions / decisions deferred

- **Logs:** Grafana Cloud vs Better Stack — decide once you actually need centralized logs (probably week 2 of running).
- **Staging:** plan covers production only. If you want a staging environment, clone the workflows with `environment: staging` + a second tunnel hostname (`api-staging.yourdomain.com`). No extra VM needed — staging can run on the same box on a different port.
- **Zero-downtime deploys:** current plan has ~5s downtime on each deploy (systemd stop → swap → start). For v1 this is fine. If it becomes a problem, switch to blue-green: run two copies on ports 5000/5001 behind the tunnel ingress and swap which one the tunnel points to.
- **OCI egress:** Always Free gives 10 TB/month outbound — more than enough, but worth watching if R2 calls generate heavy traffic to Cloudflare.
