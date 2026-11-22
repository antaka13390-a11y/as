# Safe Testing Procedure for One CCBoot Client (Before Full Rollout)

Follow this sequence strictly using a **single** test workstation (`PC-TEST-01`) so active customer stations and your golden CCBoot image are never impacted.

## Step 1: Verify Server Isolation & Permissions
1. On `CCBOOT-SRV`, verify `CyberHubServerService` is running:
   ```powershell
   Get-Service CyberHubServerService
   ```
2. Verify read-only client endpoint responds from `PC-TEST-01`:
   ```powershell
   Invoke-RestMethod -Uri "https://ccboot-srv.lan:8443/api/catalog" -SkipCertificateCheck
   ```
3. **Negative Security Test (Critical)**: From `PC-TEST-01`, attempt to call an admin endpoint:
   ```powershell
   Invoke-WebRequest -Uri "https://ccboot-srv.lan:8443/api/admin/games" -SkipCertificateCheck
   ```
   Expected result: **HTTP 403 Forbidden** (`Administrative access is strictly restricted...`).
4. **SMB Write Protection Test**: From `PC-TEST-01`, attempt to create a dummy file inside `\\CCBOOT-SRV\CyberHubReadOnly$\test.txt`.
   Expected result: **Access Denied**.

## Step 2: Boot One Client in CCBoot Super Client (or Snapshot/Restore Mode)
1. In **CCBoot Server Console**, select ONLY `PC-TEST-01` and enable **Super Client** (or create a temporary Client Writeback Snapshot so you can discard/rollback cleanly).
2. Boot `PC-TEST-01` cleanly.
3. Run `scripts\Deploy-CCBootClient.ps1` on `PC-TEST-01` (without `-RegisterAutoStart` first for manual inspection).

## Step 3: Functional & Game Execution Verification on `PC-TEST-01`
1. Launch `C:\Program Files\CyberHub\ClientLauncher\CyberHub.ClientLauncher.exe`.
2. Confirm:
   - Launcher opens fullscreen (`1920x1080`) with zero admin buttons/menus.
   - Home, All Games, Favorites, Recently Played, Search, and Game Details work smoothly.
3. Test all configured `LaunchType` modes:
   - **Direct Executable**: Launch a UNC game (`\\CCBOOT-SRV\Games\...`), verify launcher minimizes, click Play again quickly (verify duplicate launch is blocked), close game, verify launcher automatically restores fullscreen focus.
   - **Steam**: Launch a Steam AppID game, close game, verify launcher returns.
   - **Riot Client**: Launch a Riot product (`valorant`), close game, verify launcher returns.
4. **Offline Fallback Test**: Temporarily stop `CyberHubServerService` on `CCBOOT-SRV` for 30 seconds, click **Sync Catalog** in the client, and confirm it gracefully falls back to `\\CCBOOT-SRV\CyberHubReadOnly$\catalog.json` or local cache with a non-crashing banner. Restart `CyberHubServerService` afterward.
5. **Non-Destructive Removal Test**: From `ADMIN-PC`, remove a test game record in `CyberHub.AdminPanel.exe`, confirm it disappears from `PC-TEST-01` after sync, and verify the game folder on `\\CCBOOT-SRV\Games\...` is completely untouched.

## Step 4: Finalize Golden Image
1. Once all checks pass, enable `-RegisterAutoStart`, shut down `PC-TEST-01`, disable Super Client in CCBoot, and save the CCBoot image restore point before rebooting the remaining cafe stations.
