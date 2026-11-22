#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\AdminPanel"
#endif

[Setup]
AppId={{8D4F6C21-5C12-4F8B-9B7A-100000000002}
AppName=CYBER HUB Admin Panel
AppVersion={#AppVersion}
AppPublisher=CyberHub Systems
DefaultDirName={autopf}\CyberHub\AdminPanel
DisableDirPage=no
DefaultGroupName=CYBER HUB Admin
OutputDir=..\..\artifacts\installers
OutputBaseFilename=CyberHub-AdminPanel-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\CyberHub.AdminPanel.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Configure-CyberHubAdmin.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{commondesktop}\CYBER HUB ADMIN"; Filename: "{app}\CyberHub.AdminPanel.exe"; WorkingDir: "{app}"; Comment: "CYBER HUB Central Administration Console"
Name: "{group}\CYBER HUB ADMIN"; Filename: "{app}\CyberHub.AdminPanel.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall CYBER HUB Admin Panel"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\CyberHub.AdminPanel.exe"; Description: "Launch CYBER HUB ADMIN now to create your initial Admin password"; Flags: nowait postinstall skipifsilent

[Code]
var
  AdminConfigPage: TInputQueryWizardPage;
  AdminConfirmPage: TInputOptionWizardPage;
  InstallFailureCode: Integer;

procedure InitializeWizard;
begin
  InstallFailureCode := 0;

  AdminConfirmPage := CreateInputOptionPage(
    wpWelcome,
    'Administration Workstation Verification',
    'IMPORTANT: Install ONLY on your designated Administration computer.',
    'NEVER install the Admin Panel on customer gaming computers or inside the CCBoot client image.',
    False, False);
  AdminConfirmPage.Add('I confirm this computer is the designated Administration PC (NOT a customer station)');
  AdminConfirmPage.Values[0] := False;

  AdminConfigPage := CreateInputQueryPage(
    wpSelectDir,
    'CYBER HUB Server Connection',
    'Enter the HTTPS address of your CYBER HUB Server (CCBOOT-SRV).',
    'On first launch, CYBER HUB ADMIN will prompt you to create your initial 10+ character Administrator password.');
  AdminConfigPage.Add('Server Base URL:', False);
  AdminConfigPage.Values[0] := ExpandConstant('{param:SERVERURL|https://ccboot-srv.lan:8443}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = AdminConfirmPage.ID then
  begin
    if not AdminConfirmPage.Values[0] then
    begin
      MsgBox('Please confirm this is your designated Administration PC before continuing. Never install Admin Panel on customer computers.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function GetServerUrl(Param: String): String;
begin
  Result := Trim(AdminConfigPage.Values[0]);
  if Result = '' then Result := 'https://ccboot-srv.lan:8443';
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
  AppDir, AdminArgs: String;
begin
  if CurStep = ssPostInstall then
  begin
    AppDir := ExpandConstant('{app}');
    AdminArgs := '-InstallDir "' + AppDir + '" -ServerBaseUrl "' + GetServerUrl('') + '"';
    RunPowerShellStep(
      'scripts\Configure-CyberHubAdmin.ps1',
      AdminArgs,
      'Configure-CyberHubAdmin.log',
      'Saving CYBER HUB Server connection settings...');
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := InstallFailureCode;
end;
