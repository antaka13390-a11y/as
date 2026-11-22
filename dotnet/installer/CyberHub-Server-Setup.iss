#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\ServerService"
#endif

[Setup]
AppId={{8D4F6C21-5C12-4F8B-9B7A-100000000001}
AppName=CYBER HUB Server Service
AppVersion={#AppVersion}
AppPublisher=CyberHub Systems
DefaultDirName=C:\CyberHub\ServerService
DisableDirPage=no
DefaultGroupName=CYBER HUB Server
OutputDir=..\..\artifacts\installers
OutputBaseFilename=CyberHub-Server-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\CyberHub.ServerService.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Install-CyberHubServer.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Configure-NtfsSmbPermissions.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Stop-Service -Name 'CyberHubServerService' -Force -ErrorAction SilentlyContinue; sc.exe delete 'CyberHubServerService' | Out-Null; $i = 0; while ((Get-Process -Name 'CyberHub.ServerService' -ErrorAction SilentlyContinue) -and $i -lt 30) {{ Start-Sleep -Milliseconds 300; $i++ }; Remove-NetFirewallRule -DisplayName 'CYBER HUB Server HTTPS (LAN)' -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated; RunOnceId: "UninstallCyberHubServerService"

[Code]
var
  ServerConfigPage: TInputQueryWizardPage;
  InstallFailureCode: Integer;

procedure InitializeWizard;
begin
  InstallFailureCode := 0;

  ServerConfigPage := CreateInputQueryPage(
    wpSelectDir,
    'Designated Admin Workstation & LAN Settings',
    'Specify which workstation is allowed to administer CYBER HUB.',
    'Only the Admin IP and Computer Name listed below will be permitted to connect with CyberHub.AdminPanel.exe. Customer PCs will remain blocked.');
  ServerConfigPage.Add('Admin PC Static IP Address:', False);
  ServerConfigPage.Add('Admin PC Computer Name:', False);
  ServerConfigPage.Add('Server LAN Hostname (for HTTPS Certificate):', False);

  ServerConfigPage.Values[0] := ExpandConstant('{param:ADMINIP|192.168.10.50}');
  ServerConfigPage.Values[1] := ExpandConstant('{param:ADMINHOST|ADMIN-PC}');
  ServerConfigPage.Values[2] := ExpandConstant('{param:SERVERDNS|ccboot-srv.lan}');
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

function RunPowerShellStep(const ScriptRelativePath, ScriptArgs, LogFileName, StatusMsg: String): Boolean;
var
  PsExe, CmdExe, ScriptPath, LogDir, LogPath, InnerCmd, CmdParams: String;
  ResultCode: Integer;
begin
  Result := True;
  WizardForm.StatusLabel.Caption := StatusMsg;
  LogDir := ExpandConstant('{commonappdata}\CyberHub\Logs');
  ForceDirectories(LogDir);
  LogPath := LogDir + '\' + LogFileName;
  PsExe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  CmdExe := ExpandConstant('{cmd}');
  ScriptPath := ExpandConstant('{app}\') + ScriptRelativePath;

  InnerCmd := '"' + PsExe + '" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"';
  if ScriptArgs <> '' then
    InnerCmd := InnerCmd + ' ' + ScriptArgs;
  CmdParams := '/d /c "' + InnerCmd + ' > "' + LogPath + '" 2>&1"';

  Log('Executing setup script: ' + CmdExe + ' ' + CmdParams);
  if not Exec(CmdExe, CmdParams, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    InstallFailureCode := ResultCode;
    if InstallFailureCode = 0 then InstallFailureCode := 1;
    Log('FAILED to launch PowerShell configuration script (' + ScriptPath + '). ExitCode=' + IntToStr(InstallFailureCode));
    if not WizardSilent then
      MsgBox('Setup failed to launch ' + ScriptRelativePath + ' (code ' + IntToStr(InstallFailureCode) + '). See log: ' + LogPath, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Log('Script completed with exit code ' + IntToStr(ResultCode) + ': ' + ScriptPath);
  if ResultCode <> 0 then
  begin
    InstallFailureCode := ResultCode;
    Log('FAILED configuration script (' + ScriptPath + ') with ExitCode=' + IntToStr(ResultCode) + '. Log: ' + LogPath);
    if not WizardSilent then
      MsgBox('Configuration step failed in ' + ScriptRelativePath + ' (exit code ' + IntToStr(ResultCode) + '). See log: ' + LogPath, mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir, ServerArgs: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDir := ExpandConstant('{app}');
    ServerArgs :=
      '-PublishPath "' + AppDir + '" ' +
      '-AdminWorkstationIp "' + GetAdminIp('') + '" ' +
      '-AdminWorkstationName "' + GetAdminHost('') + '" ' +
      '-CertificateDnsName "' + GetServerDns('') + '"';

    if not RunPowerShellStep(
      'scripts\Install-CyberHubServer.ps1',
      ServerArgs,
      'Install-CyberHubServer.log',
      'Configuring CYBER HUB Windows Service, LAN HTTPS certificate, and firewall...') then
      Exit;

    RunPowerShellStep(
      'scripts\Configure-NtfsSmbPermissions.ps1',
      '',
      'Configure-NtfsSmbPermissions.log',
      'Configuring read-only SMB share (CyberHubReadOnly$) and database permissions...');
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := InstallFailureCode;
end;
