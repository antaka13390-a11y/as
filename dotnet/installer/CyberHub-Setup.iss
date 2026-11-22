#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishRoot
  #define PublishRoot "..\artifacts\publish"
#endif

[Setup]
AppId={{8D4F6C21-5C12-4F8B-9B7A-100000000000}
AppName=CYBER HUB
AppVersion={#AppVersion}
AppPublisher=CyberHub Systems
DefaultDirName={autopf}\CyberHub
DisableDirPage=yes
DefaultGroupName=CYBER HUB
OutputDir=..\..\artifacts\installers
OutputBaseFilename=CyberHub-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Server Service ONLY
Source: "{#PublishRoot}\ServerService\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsRoleServer
Source: "..\scripts\Install-CyberHubServer.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion; Check: IsRoleServer
Source: "..\scripts\Configure-NtfsSmbPermissions.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion; Check: IsRoleServer

; Admin Panel ONLY
Source: "{#PublishRoot}\AdminPanel\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsRoleAdmin
Source: "..\scripts\Configure-CyberHubAdmin.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion; Check: IsRoleAdmin

; Client Launcher ONLY (zero Admin/Server binaries)
Source: "{#PublishRoot}\ClientLauncher\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsRoleClient
Source: "..\scripts\Configure-CyberHubClient.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion; Check: IsRoleClient

[Icons]
Name: "{commondesktop}\CYBER HUB ADMIN"; Filename: "{app}\CyberHub.AdminPanel.exe"; WorkingDir: "{app}"; Comment: "CYBER HUB Central Administration Console"; Check: IsRoleAdmin
Name: "{group}\CYBER HUB ADMIN"; Filename: "{app}\CyberHub.AdminPanel.exe"; WorkingDir: "{app}"; Check: IsRoleAdmin
Name: "{group}\CYBER HUB"; Filename: "{app}\CyberHub.ClientLauncher.exe"; WorkingDir: "{app}"; Check: IsRoleClient
Name: "{group}\Uninstall CYBER HUB"; Filename: "{uninstallexe}"

[Run]
; Server setup tasks
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Install-CyberHubServer.ps1"" -PublishPath ""{app}"" -AdminWorkstationIp ""{code:GetAdminIp}"" -AdminWorkstationName ""{code:GetAdminHost}"" -CertificateDnsName ""{code:GetServerDns}"""; StatusMsg: "Configuring CYBER HUB Server Service, LAN HTTPS certificate, and firewall..."; Flags: runhidden waituntilterminated; Check: IsRoleServer
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-NtfsSmbPermissions.ps1"""; StatusMsg: "Applying NTFS & Read-Only SMB share permissions..."; Flags: runhidden waituntilterminated; Check: IsRoleServer

; Admin setup tasks
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubAdmin.ps1"" -InstallDir ""{app}"" -ServerBaseUrl ""{code:GetAdminServerUrl}"""; StatusMsg: "Saving Admin Panel server connection..."; Flags: runhidden waituntilterminated; Check: IsRoleAdmin
Filename: "{app}\CyberHub.AdminPanel.exe"; Description: "Launch CYBER HUB ADMIN now to create your initial Admin password"; Flags: nowait postinstall skipifsilent; Check: IsRoleAdmin

; Client setup tasks
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubClient.ps1"" -InstallDir ""{app}"" -ServerBaseUrl ""{code:GetClientServerUrl}"" -SmbShareRoot ""{code:GetClientSmbRoot}""{code:GetClientAutoStartFlag}"; StatusMsg: "Configuring CYBER HUB Client Launcher and logon startup..."; Flags: runhidden waituntilterminated; Check: IsRoleClient

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Stop-Service -Name 'CyberHubServerService' -Force -ErrorAction SilentlyContinue; sc.exe delete 'CyberHubServerService' | Out-Null; $i = 0; while ((Get-Process -Name 'CyberHub.ServerService' -ErrorAction SilentlyContinue) -and $i -lt 30) {{ Start-Sleep -Milliseconds 300; $i++ }; Remove-NetFirewallRule -DisplayName 'CYBER HUB Server HTTPS (LAN)' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; Check: IsRoleServer; RunOnceId: "UninstallCyberHubServerService"
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubClient.ps1"" -InstallDir ""{app}"" -RemoveAutoStart"; Flags: runhidden waituntilterminated; Check: IsRoleClient; RunOnceId: "RemoveCyberHubClientAutoStart"

[Code]
var
  RolePage: TInputOptionWizardPage;
  ServerConfigPage: TInputQueryWizardPage;
  AdminConfigPage: TInputQueryWizardPage;
  ClientConfigPage: TInputQueryWizardPage;

function SelectedRoleIndex: Integer;
begin
  if RolePage = nil then
    Result := 0
  else
    Result := RolePage.SelectedValueIndex;
end;

function IsRoleServer: Boolean;
begin
  Result := (SelectedRoleIndex = 0);
end;

function IsRoleAdmin: Boolean;
begin
  Result := (SelectedRoleIndex = 1);
end;

function IsRoleClient: Boolean;
begin
  Result := (SelectedRoleIndex = 2);
end;

function GetInstallDir(Param: String): String;
begin
  if IsRoleServer then
    Result := 'C:\CyberHub\ServerService'
  else if IsRoleAdmin then
    Result := ExpandConstant('{autopf}\CyberHub\AdminPanel')
  else
    Result := ExpandConstant('{autopf}\CyberHub\ClientLauncher');
end;

procedure InitializeWizard;
var
  RoleParam: String;
begin
  RolePage := CreateInputOptionPage(
    wpWelcome,
    'Select Computer Role',
    'Choose the CYBER HUB component for this specific computer.',
    'IMPORTANT: Install Server ONLY on CCBOOT-SRV, Admin Panel ONLY on ADMIN-PC, and Client Launcher ONLY on customer stations (e.g. PC-TEST-01).',
    True, False);
  RolePage.Add('Server Service (CCBOOT-SRV — Windows Server 2022)');
  RolePage.Add('Admin Panel (ADMIN-PC ONLY — Never install on customer stations)');
  RolePage.Add('Client Launcher (Customer PC / CCBoot Image — Fullscreen game launcher)');

  RoleParam := Lowercase(ExpandConstant('{param:ROLE|server}'));
  if RoleParam = 'admin' then
    RolePage.SelectedValueIndex := 1
  else if RoleParam = 'client' then
    RolePage.SelectedValueIndex := 2
  else
    RolePage.SelectedValueIndex := 0;

  WizardForm.DirEdit.Text := GetInstallDir('');

  ServerConfigPage := CreateInputQueryPage(
    RolePage.ID,
    'Server & Admin Allowlist Settings',
    'Configure which Admin computer can manage CYBER HUB.',
    'All other computers will receive HTTP 403 Forbidden if they attempt to open admin routes.');
  ServerConfigPage.Add('Admin PC Static IP Address:', False);
  ServerConfigPage.Add('Admin PC Computer Name:', False);
  ServerConfigPage.Add('Server LAN Hostname (for HTTPS Certificate):', False);
  ServerConfigPage.Values[0] := ExpandConstant('{param:ADMINIP|192.168.10.50}');
  ServerConfigPage.Values[1] := ExpandConstant('{param:ADMINHOST|ADMIN-PC}');
  ServerConfigPage.Values[2] := ExpandConstant('{param:SERVERDNS|ccboot-srv.lan}');

  AdminConfigPage := CreateInputQueryPage(
    ServerConfigPage.ID,
    'Admin Panel Connection Settings',
    'Connect this Administration workstation to CCBOOT-SRV.',
    'On first launch, you will create your initial 10+ character Administrator password.');
  AdminConfigPage.Add('Server Base URL:', False);
  AdminConfigPage.Values[0] := ExpandConstant('{param:SERVERURL|https://ccboot-srv.lan:8443}');

  ClientConfigPage := CreateInputQueryPage(
    AdminConfigPage.ID,
    'Client Launcher Connection Settings',
    'Connect this gaming PC to your CYBER HUB Server.',
    'Customer stations synchronize read-only games, categories, and themes automatically.');
  ClientConfigPage.Add('Server Base URL:', False);
  ClientConfigPage.Add('Read-Only SMB Share Path:', False);
  ClientConfigPage.Add('Register automatic fullscreen logon startup (1 = Yes, 0 = No):', False);
  ClientConfigPage.Values[0] := ExpandConstant('{param:SERVERURL|https://ccboot-srv.lan:8443}');
  ClientConfigPage.Values[1] := ExpandConstant('{param:SMBROOT|\\CCBOOT-SRV\CyberHubReadOnly$}');
  ClientConfigPage.Values[2] := ExpandConstant('{param:AUTOSTART|1}');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ServerConfigPage.ID then Result := not IsRoleServer
  else if PageID = AdminConfigPage.ID then Result := not IsRoleAdmin
  else if PageID = ClientConfigPage.ID then Result := not IsRoleClient;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = RolePage.ID then
  begin
    WizardForm.DirEdit.Text := GetInstallDir('');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    WizardForm.DirEdit.Text := GetInstallDir('');
  end;
end;

function GetAdminIp(Param: String): String;
begin
  Result := Trim(ServerConfigPage.Values[0]);
  if Result = '' then Result := '192.168.10.50';
end;

function GetAdminHost(Param: String): String;
begin
  Result := Trim(ServerConfigPage.Values[1]);
  if Result = '' then Result := 'ADMIN-PC';
end;

function GetServerDns(Param: String): String;
begin
  Result := Trim(ServerConfigPage.Values[2]);
  if Result = '' then Result := 'ccboot-srv.lan';
end;

function GetAdminServerUrl(Param: String): String;
begin
  Result := Trim(AdminConfigPage.Values[0]);
  if Result = '' then Result := 'https://ccboot-srv.lan:8443';
end;

function GetClientServerUrl(Param: String): String;
begin
  Result := Trim(ClientConfigPage.Values[0]);
  if Result = '' then Result := 'https://ccboot-srv.lan:8443';
end;

function GetClientSmbRoot(Param: String): String;
begin
  Result := Trim(ClientConfigPage.Values[1]);
  if Result = '' then Result := '\\CCBOOT-SRV\CyberHubReadOnly$';
end;

function GetClientAutoStartFlag(Param: String): String;
begin
  if Trim(ClientConfigPage.Values[2]) = '1' then
    Result := ' -RegisterAutoStart'
  else
    Result := '';
end;
