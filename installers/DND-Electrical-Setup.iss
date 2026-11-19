; Inno Setup script for D&D Electrical Engineering Suite
; Build: scripts/publish-windows.ps1, then ISCC.exe installers\DND-Electrical-Setup.iss

#define AppName "D&D Electrical Engineering Suite"
#define AppVersion "0.4.0"
#define AppPublisher "D&D | Data&Development"
#define AppExe "DND.ElectricalSuite.exe"
#define Publish "..\artifacts\publish"

[Setup]
AppId={{6C3B0B1E-2E4F-4C1A-9E56-5D0D2A7B9E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 D&D | Data&Development. All rights reserved.
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} – Powered by D&D | Data&Development
DefaultDirName={autopf}\DND\Electrical Engineering Suite
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=DND-Electrical-Engineering-Suite-Setup
SetupIconFile=..\resources\Icons\dnd-app.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE.md
WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
ChangesAssociations=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "demos"; Description: "Install the demonstration projects (fictional data)"; GroupDescription: "Content:"

[Files]
Source: "{#Publish}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Publish}\demo\*.dndx"; DestDir: "{commondocs}\DND Electrical\Demo projects"; Tasks: demos; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Demo projects"; Filename: "{commondocs}\DND Electrical\Demo projects"; Tasks: demos
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.dndx"; ValueType: string; ValueName: ""; ValueData: "DND.ElectricalProject"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\DND.ElectricalProject"; ValueType: string; ValueName: ""; ValueData: "D&D Electrical project"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\DND.ElectricalProject\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\DND.ElectricalProject\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAuthor & Developer: D&D | Data&Development%n© 2026 D&D | Data&Development. All rights reserved.
