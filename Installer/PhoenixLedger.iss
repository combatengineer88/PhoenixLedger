#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif

[Setup]
; The stable AppId preserves upgrades, the chosen directory and the uninstall record.
AppId=PhoenixLedgerCSharpLab
AppName=PhoenixLedger C# Lab
AppVersion={#AppVersion}
AppVerName=PhoenixLedger C# Lab {#AppVersion}
AppPublisher=PhoenixLedger
AppPublisherURL=https://github.com/combatengineer88/PhoenixLedger
AppUpdatesURL=https://github.com/combatengineer88/PhoenixLedger/releases/latest
DefaultDirName={localappdata}\Programs\PhoenixLedger CSharpLab
DefaultGroupName=PhoenixLedger C# Lab
DisableDirPage=no
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
WizardStyle=modern
SetupIconFile=..\PhoenixLedger.CSharpLab\Assets\phoenixledger.ico
UninstallDisplayIcon={app}\PhoenixLedger.CSharpLab.exe
CloseApplications=yes
RestartApplications=no
OutputDir=..\dist\setup
OutputBaseFilename=PhoenixLedger-CSharpLab-Setup
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PhoenixLedger C# Lab"; Filename: "{app}\PhoenixLedger.CSharpLab.exe"
Name: "{autodesktop}\PhoenixLedger C# Lab"; Filename: "{app}\PhoenixLedger.CSharpLab.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\PhoenixLedger\CSharpLab"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\PhoenixLedger.CSharpLab.exe"; Flags: nowait runasoriginaluser
