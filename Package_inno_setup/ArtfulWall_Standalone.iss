;=== ArtfulWall_Standalone.iss ===
; 独立版安装脚本
#define STANDALONE
#include "Common.iss"

[Setup]
AppId={{A8D6D8F4-3DC2-465F-9E1B-61B6A8F29CD5}}
AppName={#MyAppName} (独立版)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#ProjectDir}\\LICENSE
OutputDir={#ProjectDir}\\install
OutputBaseFilename=ArtfulWall_Setup_Standalone
SetupIconFile=D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\appicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#ProjectDir}\\publish-standalone\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs