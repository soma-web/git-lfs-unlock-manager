; ============================================================
;  Git LFS Lock Manager - Inno Setup Script
;  Requires: Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
;  Build steps:
;    1. From the project root, run:
;         build-installer.bat
;       (This publishes the app and then calls ISCC on this file)
;
;    Or manually:
;    1. dotnet publish -c Release -r win-x64 --self-contained -o publish\
;    2. iscc setup.iss
; ============================================================

#define AppName      "Git LFS Lock Manager"
#define AppVersion   "1.0.0"
#define AppPublisher "Your Name"
#define AppExeName   "GitLFSUnlocker.exe"
#define PublishDir   "publish"

[Setup]
AppId={{A7C4D2E8-3F1B-4A56-9D0E-8B2F5C6E7D4A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=dist
OutputBaseFilename=GitLFSLockManager-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Allow per-user or per-machine install (shows UAC dialog for machine-wide)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
MinVersion=10.0

; Uninstaller
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; Copy everything from the dotnet publish output folder
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

; Optional desktop shortcut
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch immediately after install
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Warn if git is not detected on PATH
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not Exec('git.exe', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if MsgBox('Git was not detected on your PATH.' + #13#10 +
              'Git LFS Lock Manager requires Git and git-lfs to be installed and on PATH.' + #13#10#13#10 +
              'Continue installation anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
