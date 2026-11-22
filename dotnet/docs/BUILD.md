# CYBER HUB — Build & Verification Instructions (.NET 8)

## Prerequisites (Windows Build Machine / Server 2022)
- Windows 10 / Windows 11 / Windows Server 2022 (x64)
- [.NET 8 SDK (8.0.x)](https://dotnet.microsoft.com/download/dotnet/8.0) with Windows Desktop workload (for WPF)

## 1. Restore, Build, and Run Unit Tests
Open PowerShell in the `dotnet/` directory:

```powershell
dotnet restore CyberHub.sln
dotnet build CyberHub.sln -c Release
dotnet test CyberHub.sln -c Release --no-build --verbosity normal
```

The test suite (`CyberHub.Tests`) covers:
- Catalog CRUD, category multi-assignment, and Online/Offline classification
- Admin authentication, password changes, token epoch revocation, and workstation allowlisting
- Theme preset seeding, multi-theme CRUD, atomic publishing, branding customization, and asset magic-byte validation
- Game detection false-positive filtering, Steam/Riot manifest parsing, duplicate detection, confirmation-gated import, and SSRF artwork URL allowlisting

## 2. Publish Separate Artifacts (Strict Role Separation)

### Server Service (Windows Server 2022)
```powershell
dotnet publish src\CyberHub.ServerService\CyberHub.ServerService.csproj `
  -c Release -r win-x64 --self-contained false -o C:\CyberHub\ServerService
```

### Admin Panel (Designated Admin PC ONLY)
```powershell
dotnet publish src\CyberHub.AdminPanel\CyberHub.AdminPanel.csproj `
  -c Release -r win-x64 --self-contained false -o C:\CyberHub\AdminPanel
```
Then run `scripts\Install-CyberHubAdminPanel.ps1` **only on the designated administration workstation (`ADMIN-PC`)** to install `CyberHub.AdminPanel.exe` and create the `CYBER HUB ADMIN` desktop shortcut.

### Client Launcher (CCBoot Windows 10 Image)
```powershell
dotnet publish src\CyberHub.ClientLauncher\CyberHub.ClientLauncher.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o \\CCBOOT-SRV\Deploy$\CyberHubClient
```

> **Security Check**: Never copy `CyberHub.AdminPanel.exe` or `CyberHub.ServerService.exe` onto customer computers or the CCBoot golden image. Customer launchers strictly synchronize read-only catalog (`catalog.json`), theme (`theme.json`), covers (`/covers/`), and theme assets (`/theme-assets/`).
