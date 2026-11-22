# CYBER HUB — CCBoot Diskless & Windows Server 2022 Deployment Guide

## Architecture Overview
1. **Windows Server 2022 (`CCBOOT-SRV`)**
   - Runs `CyberHub.ServerService` as a Windows Service on HTTPS port `8443`.
   - Owns the SQLite database (`C:\ProgramData\CyberHub\Data\cyberhub.db`) in WAL mode (automatically runs schema migrations v1 -> v2 -> v3 safely without touching existing game records).
   - Publishes atomic read-only JSON snapshots and media assets to `C:\CyberHubShares\PublishedCatalog` (`\\CCBOOT-SRV\CyberHubReadOnly$`):
     - `catalog.json` (dynamic categories + visible games + storage metadata)
     - `theme.json` (published theme, colors, layout, card design, AR/EN app name & cafe branding)
     - `covers\` (cached cover & hero artwork uploaded by admins or downloaded from permitted metadata sources)
     - `theme-assets\` (uploaded logo, app icon, loading logo, background image, and optional MP4 loop)
   - Enforces IP + Workstation allowlist (`AllowedAdminIps`, `AllowedAdminWorkstations`), PBKDF2-SHA512 password hashing, HMAC-SHA256 bearer authentication with persistent `TokenEpoch` revocation on `/api/admin/*`, and magic-byte + SSRF upload validation.

2. **Designated Administration Workstation (`ADMIN-PC` / `192.168.10.50`)**
   - Runs `CyberHub.AdminPanel.exe` (installed via `scripts\Install-CyberHubAdminPanel.ps1` with the `CYBER HUB ADMIN` desktop shortcut).
   - **Never** installed on customer computers.
   - Configures games with UNC paths (`\\CCBOOT-SRV\Games\CCBoot\...`, `\\CCBOOT-SRV\Games\SteamLibrary\...`, `\\CCBOOT-SRV\Games\Riot Games\...`) so drive letters never mismatch across stations.
   - Provides:
     - **Game Library -> Manage Games & Scan for Games** (automatic Steam/Riot/PE detection & cover art manager)
     - **Categories** (dynamic creation, ordering, icons, banners)
     - **Appearance & Themes -> Branding** (6 built-in presets, custom themes, video/image/gradient backgrounds, AR/EN application name & cyber cafe name, live preview, instant publish)
     - **Settings -> Security & Account** (password change with strength indicator, global token revocation, Secure Logout)
     - **Session Recovery** & **Audit Logs**

3. **CCBoot Windows 10 Diskless Clients (`192.168.10.101–254`)**
   - Run `CyberHub.ClientLauncher.exe` in fullscreen borderless mode (`1920x1080`).
   - Fetch read-only catalog (`GET /api/catalog`) and published theme (`GET /api/catalog/theme`) via HTTPS with automatic 30-second live synchronization (zero launcher restart required when branding or themes are published).
   - Fall back automatically to `\\CCBOOT-SRV\CyberHubReadOnly$\catalog.json` / `theme.json`, then `%LOCALAPPDATA%\CyberHub\`.
   - Cache game covers (`%LOCALAPPDATA%\CyberHub\covers\`) and theme media (`%LOCALAPPDATA%\CyberHub\theme-assets\`) locally, skipping unchanged files for fast CCBoot cold boots.
   - Zero administration controls, zero developer navigation, and zero write permissions on SQLite DB, published shares, or game directories.

## Deployment Steps
1. Publish all three projects using `BUILD.md`.
2. On `CCBOOT-SRV`:
   - Install a LAN TLS certificate (internal CA or self-signed SAN cert for `ccboot-srv.lan`) and bind to Kestrel HTTPS port `8443`.
   - Update `C:\CyberHub\ServerService\appsettings.json` with your exact `AllowedAdminIps`, `AllowedAdminWorkstations`, and optional `RawgApiKey` (if using RAWG artwork fallback).
   - Run `scripts\Install-CyberHubServer.ps1` as Administrator.
   - Run `scripts\Configure-NtfsSmbPermissions.ps1` as Administrator to isolate SQLite DB and set `CyberHubReadOnly$` (`covers` and `theme-assets` included) to Read-Only for gaming clients.
3. On `ADMIN-PC` **ONLY**:
   - Run `scripts\Install-CyberHubAdminPanel.ps1 -AdminPublishFolder "C:\CyberHub\Deploy\AdminPanel" -ServerBaseUrl "https://ccboot-srv.lan:8443"` to deploy `CyberHub.AdminPanel.exe` to `C:\Program Files\CyberHub\AdminPanel\` and create the **CYBER HUB ADMIN** desktop shortcut.
   - Launch **CYBER HUB ADMIN**, claim your initial administrator password (10+ characters) on first run, and verify theme/game publishing.
4. Follow `TESTING_SINGLE_CLIENT.md` on ONE CCBoot Super Client before merging changes into the production CCBoot image.
