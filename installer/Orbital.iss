; Orbital Inno Setup Script
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php

#define MyAppName "Orbital"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CrowKing63"
#define MyAppURL "https://github.com/CrowKing63/Orbital"
#define MyAppExeName "Orbital.exe"
#define MyAppGUID "{{A3F2B5C1-8D4E-4F2A-9B6C-7E3D1A5F8C2B}"

[Setup]
AppId={#MyAppGUID}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Requires .NET 8 Desktop Runtime (x64)
; Check is done via registry (see [Code] section)
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
; Output
OutputDir=..\dist
OutputBaseFilename=Orbital-{#MyAppVersion}-Setup
; Uninstaller
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; Privilege
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill running instance before uninstall
Filename: "taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillOrbital"

[Code]
const
  DotNetDesktopRuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  DotNetDownloadURL = 'https://dotnet.microsoft.com/download/dotnet/8.0';
  RequiredMajor = 8;

function IsDotNet8DesktopInstalled(): Boolean;
var
  SubKeys: TArrayOfString;
  I: Integer;
  Major: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64, DotNetDesktopRuntimeKey, SubKeys) then
  begin
    for I := 0 to GetArrayLength(SubKeys) - 1 do
    begin
      Major := StrToIntDef(Copy(SubKeys[I], 1, Pos('.', SubKeys[I]) - 1), 0);
      if Major >= RequiredMajor then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    if MsgBox(
      '.NET 8 Desktop Runtime (x64) is required but was not found on this computer.' + #13#10 + #13#10 +
      'Click OK to open the download page, then re-run this installer after installing .NET 8.' + #13#10 +
      'Click Cancel to abort installation.',
      mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      ShellExec('open', DotNetDownloadURL, '', '', SW_SHOWNORMAL, ewNoWait, Result);
    end;
    Result := False;
  end;
end;

// Ask whether to remove AppData on uninstall
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\Orbital');
    if DirExists(AppDataDir) then
    begin
      if MsgBox(
        'Do you want to remove Orbital''s settings and data?' + #13#10 +
        '(' + AppDataDir + ')' + #13#10 + #13#10 +
        'Select No to keep your settings for a future reinstall.',
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;
