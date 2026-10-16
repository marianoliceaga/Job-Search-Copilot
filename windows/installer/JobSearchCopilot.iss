; Inno Setup script for JobSearch Copilot for Windows (per-user install, no administrator rights).
; Built by .github/workflows/windows.yml:
;   iscc /DAppVersion=1.0.N /DSourceDir=<publish folder> /DOutputDir=<output folder> windows\installer\JobSearchCopilot.iss
; <publish folder> holds the desktop host (JobSearchCopilot.exe) and the published web app in its "server" subfolder.
; The app's data (%LocalAppData%\JobPilot) is shared with the web app and is never removed by the uninstaller.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "bin"
#endif

#define AppName "JobSearch Copilot"
#define AppExe "JobSearchCopilot.exe"

[Setup]
; Never change AppId: Windows uses it to recognize updates and the uninstaller.
AppId={{311D5C07-9EDE-49E6-9585-E6E0183A1198}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; The running window holds this mutex (DesktopIdentity.MutexName): setup and uninstall ask to close it first.
AppMutex=JobSearchCopilotDesktop
CloseApplications=yes
OutputDir={#OutputDir}
OutputBaseFilename=JobSearchCopilot-Setup-{#AppVersion}
SetupIconFile=..\src\JobPilot.Desktop\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; The web app is replaced as a whole on every update, so no file from an older version is left behind.
Type: filesandordirs; Name: "{app}\server"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
