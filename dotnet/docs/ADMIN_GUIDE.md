# CYBER HUB — Administrator Panel Guide (`CyberHub.AdminPanel.exe`)

## 1. Where to Find & Install the Admin Panel
The administration console is a **separate standalone application** (`CyberHub.AdminPanel.exe`) and must **never** be installed on customer PCs or included in the CCBoot diskless image.

### Installation on the Designated Admin Computer (`ADMIN-PC`)
1. Publish `CyberHub.AdminPanel` on your build/server machine (`BUILD.md`).
2. Copy the published output folder onto `ADMIN-PC` (e.g., `C:\CyberHub\Deploy\AdminPanel`) or run from a restricted admin share.
3. On `ADMIN-PC`, run PowerShell as Administrator in `dotnet\scripts\`:
   ```powershell
   .\Install-CyberHubAdminPanel.ps1 -AdminPublishFolder "C:\CyberHub\Deploy\AdminPanel" -ServerBaseUrl "https://ccboot-srv.lan:8443"
   ```
4. The installer copies binaries to `C:\Program Files\CyberHub\AdminPanel\CyberHub.AdminPanel.exe` and creates a desktop shortcut named **CYBER HUB ADMIN**.
5. Confirm `ADMIN-PC`'s static LAN IP and hostname are listed in `CCBOOT-SRV`'s `appsettings.json` under `AllowedAdminIps` and `AllowedAdminWorkstations`.

---

## 2. Administrator Account & Password Management
Navigate to **Settings -> Security & Account** inside **CYBER HUB ADMIN**:
- **First-Run Setup**: On first launch, claim the initial administrator password (minimum 10 characters; PBKDF2-SHA512 salted & stored in SQLite `AdminConfig`).
- **Change Password**:
  1. Enter **Current Password**, **New Password**, and **Confirm New Password**.
  2. Monitor the built-in **Password Strength Indicator** (requires length + character variety).
  3. Click **Save Changes**.
  4. Saving revokes **all** existing administrator session tokens server-side (`TokenEpoch` incremented), clears local credentials, and opens the login dialog so you re-authenticate with the new password.
- **Secure Logout**: Use the **Secure Logout** button in the header or **Security & Account** tab to revoke tokens server-side and immediately return to the login prompt.

---

## 3. Appearance, Themes & Branding (`Appearance & Themes`)
### Managing Themes
- Choose from **6 built-in presets** (`Neon Purple`, `Midnight Esports`, `Crimson Arena`, `Arctic Cyber`, `Minimal Obsidian`, `Gold VIP Lounge`), or **Create**, **Save Draft**, **Duplicate**, **Rename**, and **Delete** custom themes (active/published default themes are protected from accidental deletion).
- **Publish Theme** writes an atomic `theme.json` snapshot to `CyberHubReadOnly$` and exposes `GET /api/catalog/theme`. Customer launchers auto-apply updates within 30 seconds without restarting.

### Customizing Visuals & Performance
- **Background**: Solid color, linear gradient (angle + start/end hex), custom uploaded image (`JPG`/`PNG`/`WebP`, Cover/Contain, blur, brightness, overlay dim), or optional looping MP4 video with mandatory static fallback image. Enable **Performance-Friendly Mode** to pause background video playback automatically whenever a customer launches a game.
- **Colors**: Customize Primary/Secondary Accent, Main/Sidebar/Card Backgrounds, Primary/Secondary Text, Button Background/Text, Hover State, Borders, and Active Navigation highlight.
- **Game Cards & Layout**: Configure card shape (`Rounded`/`Sharp`/`CutCorner`), radius, card width/height, cover height, spacing, shadow, hover animation (`ScaleUp`/`Glow`/`Lift`/`None`), border effect, grid columns, sidebar position (`Start`/`Left`/`Right`) & width, navigation style, and header/content spacing.

### Custom Application Name & Branding (`Branding` Sub-Section)
- Configure **English** and **Arabic** display titles independently:
  - **Application Name** (replaces default "CYBER HUB" / "سايبر هب" in header, sidebar, and welcome screen)
  - **Cyber Cafe Name**
  - **Window Title**
  - **Welcome Screen Title**
  - **Loading Screen Title**
- Upload custom **Application Logo**, **App Icon (`.ico`/`.png`)**, and **Loading Screen Logo**.
- Inspect the live **Arabic RTL / English LTR preview** before clicking **Publish Theme**.
- Click **Restore CYBER HUB Branding** at any time to revert display text/logo settings safely. Internal `.exe` filenames, Windows services, SQLite DB paths, and API endpoints remain unchanged.

---

## 4. Automatic Game Detection & Cover Art Manager (`Game Library -> Scan for Games`)
1. **Configure Storage Locations**: Manage CCBoot primary volumes, Server Steam shares, Riot shares, or add custom UNC/server folders directly inside the **Scan for Games** tab.
2. **Background Scan**: Click **Start Scan** to recursively scan selected locations asynchronously (with progress and **Cancel** button). The scanner:
   - Parses Steam `appmanifest_*.acf` manifests, Riot client metadata, and `.exe` PE product metadata / folder names.
   - Automatically skips Windows system folders, installers/uninstallers (`setup.exe`, `unins000.exe`), redistributables (`vcredist*`), and crash handlers.
   - Flags duplicates against already-configured games (by path, AppID, or normalized name) and leaves them unchecked so existing admin-edited records are **never** overwritten.
   - Never launches or modifies any game executable or folder.
3. **Cover Art Lookup & Manual Upload**:
   - Click **Search Cover Art** on any candidate to fetch permitted metadata & thumbnail candidates (Steam Store/AppDetails + optional RAWG API via `ServerOptions.RawgApiKey`), click a candidate thumbnail to select it, upload a local `.jpg`/`.png`/`.webp` cover file, or clear the cover.
4. **Confirm Import**: Check the verified games you want, assign Categories and `Online` / `Offline` network mode, and click **Confirm & Import Selected** to commit them to SQLite and publish `catalog.json`.
