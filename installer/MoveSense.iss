#ifndef MyAppVersion
  #define MyAppVersion "0.2.1"
#endif
#ifndef MyBuildSource
  #define MyBuildSource "..\artifacts\portable-0.2.1"
#endif
[Setup]
AppId={{0F664E16-15A5-40CB-B6D9-971F870B94C2}
AppName=MoveSense
AppVersion={#MyAppVersion}
AppPublisher=DOPIAS
AppPublisherURL=https://github.com/DOPIAS/MoveSense
DefaultDirName={localappdata}\Programs\MoveSense
DefaultGroupName=MoveSense
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=MoveSense-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\assets\MoveSense.ico
UninstallDisplayIcon={app}\MoveSense.exe
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut (drop a file onto it to move)
russian.DesktopShortcut=Создать ярлык на рабочем столе (перетаскивайте на него файлы)
english.ExplorerTask=Add Move with MoveSense to the classic right-click menu
russian.ExplorerTask=Добавить «Перенести через MoveSense» в меню правой кнопки
english.ExplorerLabel=Move with MoveSense
russian.ExplorerLabel=Перенести через MoveSense
[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"
Name: "explorermenu"; Description: "{cm:ExplorerTask}"
[Files]
Source: "{#MyBuildSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\MoveSense"; Filename: "{app}\MoveSense.exe"
Name: "{autodesktop}\MoveSense"; Filename: "{app}\MoveSense.exe"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\MoveSense"; ValueType: string; ValueData: "{cm:ExplorerLabel}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\*\shell\MoveSense"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\MoveSense.exe"; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\*\shell\MoveSense\command"; ValueType: string; ValueData: """{app}\MoveSense.exe"" --source ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MoveSense"; ValueType: string; ValueData: "{cm:ExplorerLabel}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MoveSense"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\MoveSense.exe"; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MoveSense\command"; ValueType: string; ValueData: """{app}\MoveSense.exe"" --source ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\*\shell\MoveSense"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MoveSense"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Flags: uninsdeletekey; Tasks: explorermenu
[Run]
Filename: "{app}\MoveSense.exe"; Description: "Open MoveSense"; Flags: nowait postinstall skipifsilent
; Local history and original reference files are preserved on uninstall.

