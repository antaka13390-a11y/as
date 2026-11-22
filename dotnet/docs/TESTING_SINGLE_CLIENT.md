# Safe Testing Procedure for One CCBoot Client (`PC-TEST-01`)

Follow this sequence strictly using a **single** test workstation (`PC-TEST-01`) so active customer stations and your golden CCBoot image are never impacted.

## Step 1: Verify Server Isolation, Schema Migration v3 & Permissions
1. On `CCBOOT-SRV`, confirm `CyberHubServerService` is running and SQLite schema migration v3 completed cleanly without losing existing `Games`, `Categories`, or `AdminConfig` records:
   ```powershell
   Get-Service CyberHubServerService
   ```
2. From `PC-TEST-01`, verify read-only catalog & published theme endpoints succeed:
   ```powershell
   Invoke-RestMethod -Uri "https://ccboot-srv.lan:8443/api/catalog" -SkipCertificateCheck
   Invoke-RestMethod -Uri "https://ccboot-srv.lan:8443/api/catalog/theme" -SkipCertificateCheck
   ```
3. **Negative Security Test (Critical)**: From `PC-TEST-01`, attempt to access Admin endpoints (`/api/admin/categories`, `/api/admin/themes`, `/api/admin/scanner/storage-locations`):
   ```powershell
   Invoke-WebRequest -Uri "https://ccboot-srv.lan:8443/api/admin/themes" -SkipCertificateCheck
   ```
   Expected result: **HTTP 403 Forbidden** (workstation/IP allowlist rejection).

## Step 2: Boot One Client in CCBoot Super Client (or Snapshot Mode)
1. Enable **Super Client** ONLY on `PC-TEST-01` in CCBoot Server Console and boot `PC-TEST-01`.
2. Deploy `CyberHub.ClientLauncher` using `scripts\Deploy-CCBootClient.ps1` and confirm `CyberHub.AdminPanel.exe` is **NOT** present anywhere on `PC-TEST-01`.

## Step 3: Verify Clean Customer UI, Dynamic Categories & AR/EN RTL
1. Launch `CyberHub.ClientLauncher.exe` on `PC-TEST-01`.
2. Confirm:
   - Custom loading screen title & logo appear briefly while catalog & theme synchronize.
   - **Zero** Admin Console, Solution/Dev, or Favorites items appear anywhere in the launcher.
   - Launcher opens in **Arabic (`ar`)** with full **RTL** layout by default.
   - Click the **EN / عربي** button in the top navigation: verify instant switch to **English (`en`) LTR** without restart, and confirm language persists after restarting the launcher.
   - Confirm dynamic categories (`All Games`, `Online Games`, `Offline Games`, `Action`, `Shooting`, `Sports`, `Racing`, `Adventure`) appear.

## Step 4: Verify Theme Customization, Branding & Live Client Sync
1. On `ADMIN-PC`, open **CYBER HUB ADMIN** -> **Appearance & Themes**:
   - Switch between presets (e.g., `Midnight Esports` -> `Neon Purple`) or customize colors, card radius/columns, and background.
   - In the **Branding** section, set a test English/Arabic Application Name and Cyber Cafe Name, upload a test logo (PNG/JPG), inspect the real-time AR/EN preview, and click **Publish Theme**.
2. Wait up to 30 seconds (or click **Sync Catalog**) on `PC-TEST-01` without restarting `CyberHub.ClientLauncher.exe`:
   - Verify window title, header/sidebar branding, custom logo, colors, card layout, and background update live.
   - Verify offline artwork & theme asset caches exist under `%LOCALAPPDATA%\CyberHub\covers` and `%LOCALAPPDATA%\CyberHub\theme-assets`.
   - Click **Restore CYBER HUB Branding** / **Restore Default Theme** on `ADMIN-PC` and publish to confirm instant rollback works cleanly.

## Step 5: Verify Automatic Game Detection & Cover Art Manager
1. On `ADMIN-PC`, open **CYBER HUB ADMIN** -> **Game Library** -> **Scan for Games**.
2. Select a shared CCBoot/Steam/Riot UNC folder (or add a custom folder) and click **Start Scan**:
   - Verify installers (`unins000.exe`, `vcredist*.exe`, crash handlers) and system directories are automatically ignored.
   - Verify already-imported games are flagged as **Duplicate** and unchecked by default so existing admin-edited metadata is never overwritten.
   - Select a detected game, inspect cover art candidates or upload a custom `.jpg`/`.png`/`.webp` cover, assign categories & Online/Offline mode, and click **Confirm & Import Selected**.
3. Confirm the newly imported game + cached cover appear on `PC-TEST-01` and removing its library record in Admin Panel does **not** touch the underlying game files.

## Step 6: Verify Security, Password Change & Single-Game Session Lock
1. On `ADMIN-PC`, go to **Settings -> Security & Account** and change the administrator password:
   - Verify all active admin tokens are invalidated immediately (`TokenEpoch` incremented) and `CyberHub.AdminPanel.exe` prompts for re-login with the new password.
2. On `PC-TEST-01`, launch a game (and verify video background pauses automatically in Performance-Friendly Mode if enabled):
   - Confirm launcher minimizes, creates `%LOCALAPPDATA%\CyberHub\active-session.lock`, blocks secondary launches, tracks child processes until exit, and supports station recovery codes.
