; TaoSFV installer (Inno Setup 6). Built by build-release.ps1, which passes the version and the
; self-contained publish folder:
;   ISCC /DAppVersion=2.0.0 /DSourceDir=publish\win-x64 taosfv-setup.iss

#ifndef AppVersion
  #define AppVersion "2.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "publish\win-x64"
#endif
#define AppUrl "https://github.com/Taoism/TaoSFV"

[Setup]
; keep this AppId: it lets new versions upgrade older TaoSFV installs in place
AppId={{85FAAFF2-B9D3-4676-A2DA-E055F6C06258}
AppName=TaoSFV
AppVersion={#AppVersion}
AppVerName=TaoSFV {#AppVersion}
AppPublisher=TaoSFV
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\TaoSFV
DefaultGroupName=TaoSFV
DisableProgramGroupPage=yes
AllowNoIcons=yes
; installs for the current user without an administrator prompt; the dialog offers all users
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=TaoSFV\TaoSFV.ico
UninstallDisplayIcon={app}\TaoSFV.exe
UninstallDisplayName=TaoSFV
OutputDir=release
OutputBaseFilename=TaoSFV-{#AppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
; close a running TaoSFV before replacing its files
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add taosfv-cli to PATH (use it from any command prompt)"; GroupDescription: "Command line:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TaoSFV"; Filename: "{app}\TaoSFV.exe"
Name: "{group}\{cm:UninstallProgram,TaoSFV}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TaoSFV"; Filename: "{app}\TaoSFV.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TaoSFV.exe"; Description: "{cm:LaunchProgram,TaoSFV}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; removes the .sfv/.md5/... associations and the Explorer menu TaoSFV registered for this user
Filename: "{app}\TaoSFV.exe"; Parameters: "/unregister"; RunOnceId: "UnregisterFileTypes"; Flags: runhidden waituntilterminated

[Code]
// PATH entry for taosfv-cli: the user's PATH for a per-user install, the system PATH for all users
function EnvironmentKey(var Root: Integer): String;
begin
  if IsAdminInstallMode then
  begin
    Root := HKEY_LOCAL_MACHINE;
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  end
  else
  begin
    Root := HKEY_CURRENT_USER;
    Result := 'Environment';
  end;
end;

function PathContains(Paths, Dir: String): Boolean;
begin
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';') > 0;
end;

procedure AddToPath(Dir: String);
var
  Root: Integer;
  Key, Paths: String;
begin
  Key := EnvironmentKey(Root);
  if not RegQueryStringValue(Root, Key, 'Path', Paths) then
    Paths := '';
  if PathContains(Paths, Dir) then
    exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(Root, Key, 'Path', Paths + Dir);
end;

procedure RemoveFromPath(Dir: String);
var
  Root, P: Integer;
  Key, Paths: String;
begin
  Key := EnvironmentKey(Root);
  if not RegQueryStringValue(Root, Key, 'Path', Paths) then
    exit;
  Paths := ';' + Paths + ';';
  P := Pos(';' + Uppercase(Dir) + ';', Uppercase(Paths));
  if P = 0 then
    exit;
  Delete(Paths, P, Length(Dir) + 1);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  RegWriteExpandStringValue(Root, Key, 'Path', Paths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
