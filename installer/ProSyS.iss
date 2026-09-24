#define AppName "ProSyS Gaming Optimizer"
#define AppVersion "1.0.0"
#ifndef SourceRoot
  #define SourceRoot "..\artifacts\app"
#endif

[Setup]
AppId={{3D94C83C-D9C2-4C49-87C9-505D511B4C5F}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\ProSyS Gaming Optimizer
DefaultGroupName=ProSyS Gaming Optimizer
OutputDir=..\artifacts\installer
OutputBaseFilename=ProSyS-Gaming-Optimizer-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\ProSyS.App.exe
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ProSyS.App.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ProSyS.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\ProSyS.App.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
