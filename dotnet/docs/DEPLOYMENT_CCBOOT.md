# CYBER HUB — CCBoot Diskless & Windows Server 2022 Deployment Guide

## Architecture Overview
1. **Windows Server 2022 (`CCBOOT-SRV`)**
   - Runs `CyberHub.ServerService` as a Windows Service on HTTPS port `8443`.
   - Owns the SQLite database (`C:\ProgramData\CyberHub\Data\cyberhub.db`) in WAL mode.
   - Publishes an atomic read-only JSON snapshot + cover artwork to `C:\CyberHubShares\PublishedCatalog` (`\\CCBOOT-SRV\CyberHubReadOnly$`).
   - Enforces IP + Workstation allowlist (`AllowedAdminIps`, `AllowedAdminWorkstations`) and PBKDF2-SHA512 + HMAC-SHA256 bearer authentication on `/api/admin/*`.

2. **Designated Administration Workstation (`ADMIN-PC` / `192.168.10.50`)**
   - Runs `CyberHub.AdminPanel.exe` (never installed on customer PCs).
   - Configures games with UNC paths (`\\CCBOOT-SRV\Games\CCBoot\...`, `\\CCBOOT-SRV\Games\SteamLibrary\...`) so drive letters never mismatch across stations.

3. **CCBoot Windows 10 Diskless Clients (`192.168.10.101–254`)**
   - Run `CyberHub.ClientLauncher.exe` in fullscreen borderless mode (`1920x1080`).
   - Fetch read-only catalog via `GET https://ccboot-srv.lan:8443/api/catalog`.
   - Fall back automatically to `\\CCBOOT-SRV\CyberHubReadOnly$\catalog.json`, then `%LOCALAPPDATA%\CyberHub\catalog-cache.json`.
   - Zero write permissions on SQLite DB, published catalog, or game shares.

## Deployment Steps
1. Publish all three projects using `BUILD.md`.
2. On `CCBOOT-SRV`, install a LAN TLS certificate (internal CA or self-signed SAN cert for `ccboot-srv.lan`) and bind to Kestrel HTTPS port `8443`.
3. Update `C:\CyberHub\ServerService\appsettings.json` with your exact `AllowedAdminIps` and `AllowedAdminWorkstations`.
4. Run `scripts\Install-CyberHubServer.ps1` as Administrator.
5. Run `scripts\Configure-NtfsSmbPermissions.ps1` as Administrator to isolate SQLite DB and set `CyberHubReadOnly$` to Read-Only for gaming clients.
6. Launch `CyberHub.AdminPanel.exe` from `ADMIN-PC` and claim your initial administrator password (10+ characters) on first run.
7. Follow `TESTING_SINGLE_CLIENT.md` on ONE CCBoot Super Client before merging changes into the production CCBoot image.
