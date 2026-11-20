#ifndef PublishDir
#define PublishDir "publish"
#endif

[Setup]
AppId=com.ichiyotsu.tokilive
AppName=TokiLive
AppVersion=0.1.6
AppPublisher=ichiyotsu
DefaultDirName={autopf}\TokiLive
DisableDirPage=no
DisableProgramGroupPage=yes
OutputBaseFilename=TokiLive-0.1.6
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
Name: "{autoprograms}\TokiLive"; Filename: "{app}\Tokilive.exe"

[Run]
Filename: "{app}\Tokilive.exe"; Description: "启动 TokiLive"; Flags: nowait postinstall skipifsilent
