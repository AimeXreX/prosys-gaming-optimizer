#define AppName "ProSyS Gaming Optimizer"
#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#ifndef SourceRoot
  #define SourceRoot "..\artifacts\app"
#endif

[Setup]
AppId={{3D94C83C-D9C2-4C49-87C9-505D511B4C5F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ProSyS
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\ProSyS Gaming Optimizer
DefaultGroupName=ProSyS Gaming Optimizer
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=ProSyS-Gaming-Optimizer-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
PrivilegesRequired=admin
UninstallDisplayIcon={app}\ProSyS.App.exe
UninstallDisplayName={#AppName}
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Messages]
WinVersionTooLowError=ProSyS requires Windows 11 (build 22000 or newer).

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "perflog"; Description: "Allow FPS benchmarking without administrator rights (adds you to Performance Log Users; takes effect after your next sign-in)"; GroupDescription: "Benchmark:"

[Files]
; The app is published self-contained: the .NET runtime, PresentMon and the restore-point helper are all included.
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ProSyS.App.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ProSyS.App.exe"; Tasks: desktopicon

[Run]
; PresentMon's ETW capture needs administrator rights or the built-in Performance Log Users group (SID S-1-5-32-559, name is localized).
Filename: "powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ""try {{ Add-LocalGroupMember -SID 'S-1-5-32-559' -Member ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) -ErrorAction Stop } catch {{ }"""; Flags: runhidden waituntilterminated; Tasks: perflog; StatusMsg: "Configuring benchmark permissions..."
; Launch as the signed-in user (not elevated) so optimizations apply to that user's settings.
Filename: "{app}\ProSyS.App.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Tools"
