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
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-CyberHubAdmin.ps1"" -InstallDir ""{app}"" -ServerBaseUrl ""{code:GetServerUrl}"""; StatusMsg: "Saving CYBER HUB Server connection settings..."; Flags: runhidden waituntilterminated
Filename: "{app}\CyberHub.AdminPanel.exe"; Description: "Launch CYBER HUB ADMIN now to create your initial Admin password"; Flags: nowait postinstall skipifsilent

[Code]
var
  AdminConfigPage: TInputQueryWizardPage;
  AdminConfirmPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
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
