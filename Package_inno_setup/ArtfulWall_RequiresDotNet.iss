;=== ArtfulWall_RequiresDotNet.iss ===
; 需要 .NET 7 运行时的安装脚本
#define REQUIRES_DOTNET
#include "Common.iss"

[Setup]
AppId={{A8D6D8F4-3DC2-465F-9E1B-61B6A8F29CD5}}
AppName={#MyAppName}
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
OutputBaseFilename=ArtfulWall_Setup_RequiresDotNet
SetupIconFile=D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\appicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Messages]

[CustomMessages]
InstallNetRuntime=安装.NET 7 Desktop Runtime
DotNetFrameworkNeeded=ArtfulWall需要.NET 7 Runtime才能运行。%n%n请从以下网址下载并安装.NET 7 Desktop Runtime:%n%nhttps://dotnet.microsoft.com/download/dotnet/7.0

[Files]
Source: "{#ProjectDir}\\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
function IsDotNet7Installed(): Boolean;
var
  success: Boolean;
  installedVersion: String;
  runtimes: TArrayOfString;
  I: Integer;
  resultCode: Integer;
begin
  Result := False; // Assume .NET 7 is not installed initially.
  
  // Method 1: Use 'dotnet --list-runtimes' command.
  SaveStringToFile(ExpandConstant('{tmp}\\check_dotnet.bat'),
    'dotnet --list-runtimes > "' + ExpandConstant('{tmp}\\dotnet_runtimes.txt') + '"' + #13#10 +
    'exit %ERRORLEVEL%', False);

  if Exec(ExpandConstant('{tmp}\\check_dotnet.bat'), '', '', SW_HIDE, ewWaitUntilTerminated, resultCode) then
  begin
    if resultCode = 0 then
    begin
      if LoadStringsFromFile(ExpandConstant('{tmp}\\dotnet_runtimes.txt'), runtimes) then
      begin
        for I := 0 to GetArrayLength(runtimes) - 1 do
        begin
          if (Pos('Microsoft.NETCore.App 7.0.', runtimes[I]) > 0) or
             (Pos('Microsoft.WindowsDesktop.App 7.0.', runtimes[I]) > 0) then
          begin
            Result := True;
            Break;
          end;
        end;
      end;
    end;
  end;

  // Method 2: Fallback to registry.
  if not Result then
  begin
    success := RegQueryStringValue(HKLM, 'SOFTWARE\\Microsoft\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.WindowsDesktop.App', 'Version', installedVersion);
    if success and (Pos('7.0.', installedVersion) = 1) then
      Result := True
    else
      begin
        success := RegQueryStringValue(HKLM32, 'SOFTWARE\\Microsoft\\dotnet\\Setup\\InstalledVersions\\x86\\sharedfx\\Microsoft.WindowsDesktop.App', 'Version', installedVersion);
        if success and (Pos('7.0.', installedVersion) = 1) then
          Result := True;
      end;
  end;

  // Method 3: Fallback to directory check.
  if not Result then
  begin
    if DirExists(ExpandConstant('{commonpf}\\dotnet\\shared\\Microsoft.WindowsDesktop.App\\7.0')) or
       DirExists(ExpandConstant('{commonpf32}\\dotnet\\shared\\Microsoft.WindowsDesktop.App\\7.0')) then
      Result := True;
  end;

  // Cleanup.
  DeleteFile(ExpandConstant('{tmp}\\check_dotnet.bat'));
  DeleteFile(ExpandConstant('{tmp}\\dotnet_runtimes.txt'));

  // If .NET 7 is not found, show custom message.
  if not Result then
    MsgBox(ExpandConstant('{cm:DotNetFrameworkNeeded}'), mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;
