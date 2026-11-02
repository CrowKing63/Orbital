; Orbital Inno Setup Script
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php

#define MyAppName "Orbital"
; Allow version to be overridden via /DMyAppVersion=x.y.z on the command line
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
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
; Per-user install to %LocalAppData% — no elevation required, which keeps
; silent auto-updates (launched by the app) fully unattended.
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
; Output
OutputDir=..\dist
OutputBaseFilename=Orbital-{#MyAppVersion}-Setup
; Uninstaller
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; Privileges
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Registry]
; Remove the auto-start registry entry on uninstall.
Root: HKCU; Subkey: Software\Microsoft\Windows\CurrentVersion\Run; KeyName: Orbital; Flags: uninsdeletevalue

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
// Auto-update launches the installer, then the app shuts itself down.
// Wait here so the installer never copies over a running executable.
// (Also covers manual reinstalls while the app is open.)
procedure InitializeWizard();
begin
  while DoWaitForProcess('Orbital.exe', 1000) do
    Sleep(500);
end;

// After a silent auto-update, relaunch the app using the flag file the app
// wrote before starting the installer.
procedure CurInstallChanged(CurInstallStep: TUninstallStep);
var
  RestartFlag: String;
begin
  if CurInstallStep = usPostInstall then
  begin
    RestartFlag := ExpandConstant('{tmp}\orbital-update-restart.flag');
    if FileExists(RestartFlag) then
    begin
      Exec(ExpandConstant('{app}\Orbital.exe'));
      DelFile(RestartFlag);
    end;
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
