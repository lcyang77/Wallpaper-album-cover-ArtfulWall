#define MyAppName "ArtfulWall"
#define MyAppVersion "2.0"
#define MyAppPublisher "Linus Yang"
#define MyAppURL "https://github.com/lcyang77/ArtfulWall-Wallpaper-album-cover"
#define MyAppExeName "ArtfulWall.exe"

[Setup]
AppId={{A8D6D8F4-3DC2-465F-9E1B-61B6A8F29CD5}}
AppName={#MyAppName} (独立版)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\LICENSE
OutputDir=D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\install
OutputBaseFilename=ArtfulWall_Setup_Standalone
SetupIconFile=D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\appicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "开机自动启动"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\publish-standalone\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\publish-standalone\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent