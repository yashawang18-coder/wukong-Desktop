#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\.publish-check\wukong-desktop-release"
#endif
#ifndef OutputDir
  #define OutputDir "..\.publish-check\installers"
#endif

[Setup]
AppId={{D9A4F0FD-1E68-4B50-94F6-4A66F9413D6E}
AppName=Wukong Desktop
AppVersion={#AppVersion}
AppPublisher=Wukong Desktop
DefaultDirName={localappdata}\Programs\Wukong Desktop
DefaultGroupName=Wukong Desktop
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Wukong-Desktop-Setup-{#AppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Wukong.Desktop.exe
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\悟空桌宠"; Filename: "{app}\Wukong.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\悟空桌宠"; Filename: "{app}\Wukong.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Wukong.Desktop.exe"; Description: "启动悟空桌宠"; Flags: nowait postinstall skipifsilent
