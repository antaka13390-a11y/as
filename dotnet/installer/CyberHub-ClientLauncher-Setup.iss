#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\ClientLauncher"
#endif

[Setup]
AppId={{8D4F6C21-5C12-4F8B-9B7A-100000000003}
AppName=CYBER HUB Client Launcher
AppVersion={#AppVersion}
AppPublisher=CyberHub Systems
DefaultDirName={autopf}\CyberHub\ClientLauncher
DisableDirPage=no
DefaultGroupName=CYBER HUB
OutputDir=..\..\artifacts\installers
OutputBaseFilename=CyberHub-ClientLauncher-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\CyberHub.ClientLauncher.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Configure-CyberHubClient.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{group}\CYBER HUB"; Filename: "{app}\CyberHub.ClientLauncher.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall CYBER HUB"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubClient.ps1"" -InstallDir ""{app}"" -ServerBaseUrl ""{code:GetServerUrl}"" -SmbShareRoot ""{code:GetSmbRoot}""{code:GetAutoStartFlag}"; StatusMsg: "Configuring CYBER HUB Client Launcher and startup options..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubClient.ps1"" -InstallDir ""{app}"" -RemoveAutoStart"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCyberHubClientAutoStart"

[Code]
var
  ClientConfigPage: TInputQueryWizardPage;
  ClientStartupPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  ClientConfigPage := CreateInputQueryPage(
    wpSelectDir,
    'CYBER HUB Server & CCBoot Share Settings',
    'Connect this gaming station to your CYBER HUB Server.',
    'Games, dynamic categories, themes, and cafe branding synchronize automatically in read-only mode.');
  ClientConfigPage.Add('Server Base URL:', False);
  ClientConfigPage.Add('Read-Only Published SMB Share:', False);
  ClientConfigPage.Values[0] := ExpandConstant('{param:SERVERURL|https://ccboot-srv.lan:8443}');
  ClientConfigPage.Values[1] := ExpandConstant('{param:SMBROOT|\\CCBOOT-SRV\CyberHubReadOnly$}');

  ClientStartupPage := CreateInputOptionPage(
    ClientConfigPage.ID,
    'Customer Startup Options',
    'Configure fullscreen launcher startup on customer logon.',
    'Recommended for CCBoot diskless gaming stations.',
    False, False);
  ClientStartupPage.Add('Automatically start CYBER HUB in fullscreen when customers log on');
  ClientStartupPage.Values[0] := (ExpandConstant('{param:AUTOSTART|1}') = '1');
end;

function GetServerUrl(Param: String): String;
begin
  Result := Trim(ClientConfigPage.Values[0]);
  if Result = '' then Result := 'https://ccboot-srv.lan:8443';
end;

function GetSmbRoot(Param: String): String;
begin
  Result := Trim(ClientConfigPage.Values[1]);
  if Result = '' then Result := '\\CCBOOT-SRV\CyberHubReadOnly$';
end;

function GetAutoStartFlag(Param: String): String;
begin
  if ClientStartupPage.Values[0] then
    Result := ' -RegisterAutoStart'
  else
    Result := '';
end;
