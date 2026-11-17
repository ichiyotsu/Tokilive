#ifndef PublishDir
#define PublishDir "publish"
#endif

[Setup]
AppId=com.ichiyotsu.tokilive
AppName=tokilive
AppVersion=0.2.0-b1
AppPublisher=ichiyotsu
DefaultDirName={autopf}\tokilive
DisableDirPage=no
DisableProgramGroupPage=yes
OutputBaseFilename=tokilive-0.2.0-b1-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Tokilive.exe
SetupIconFile=..\Tokilive\appicon.ico

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\tokilive"; Filename: "{app}\Tokilive.exe"

[Run]
Filename: "{app}\Tokilive.exe"; Description: "启动 tokilive"; Flags: nowait postinstall skipifsilent
