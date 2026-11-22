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

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubClient.ps1"" -InstallDir ""{app}"" -RemoveAutoStart"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveCyberHubClientAutoStart"

[Code]
var
  ClientConfigPage: TInputQueryWizardPage;
  ClientStartupPage: TInputOptionWizardPage;
  InstallFailureCode: Integer;

procedure InitializeWizard;
begin
  InstallFailureCode := 0;

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
  AppDir, ClientArgs: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDir := ExpandConstant('{app}');
    ClientArgs :=
      '-InstallDir "' + AppDir + '" ' +
      '-ServerBaseUrl "' + GetServerUrl('') + '" ' +
      '-SmbShareRoot "' + GetSmbRoot('') + '"' +
      GetAutoStartFlag('');
    RunPowerShellStep(
      'scripts\Configure-CyberHubClient.ps1',
      ClientArgs,
      'Configure-CyberHubClient.log',
      'Configuring CYBER HUB Client Launcher and startup options...');
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := InstallFailureCode;
end;
