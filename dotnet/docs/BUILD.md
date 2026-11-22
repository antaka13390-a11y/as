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

### Client Launcher (CCBoot Windows 10 Image)
```powershell
dotnet publish src\CyberHub.ClientLauncher\CyberHub.ClientLauncher.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o \\CCBOOT-SRV\Deploy$\CyberHubClient
```

> **Security Check**: Never copy `CyberHub.AdminPanel.exe` or `CyberHub.ServerService.exe` onto the CCBoot client image.
