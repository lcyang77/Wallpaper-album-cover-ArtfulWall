#define MyAppName "ArtfulWall"
#define MyAppVersion "2.0"
#define MyAppPublisher "Linus Yang"
#define MyAppURL "https://github.com/lcyang77/ArtfulWall-Wallpaper-album-cover"
#define MyAppExeName "ArtfulWall.exe"

[Setup]
AppId={{A8D6D8F4-3DC2-465F-9E1B-61B6A8F29CD5}
AppName={#MyAppName}
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
OutputBaseFilename=ArtfulWall_Setup_RequiresDotNet
SetupIconFile=appicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Messages]
DotNetFrameworkNeeded=ArtfulWall需要.NET 7 Runtime才能运行。%n%n请从以下网址下载并安装.NET 7 Desktop Runtime:%n%nhttps://dotnet.microsoft.com/download/dotnet/7.0

[CustomMessages]
InstallNetRuntime=安装.NET 7 Desktop Runtime

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "开机自动启动"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\indie\ArtfulWall_project\MyWallpaperApp\ArtfulWall2.0_dev\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{group}\{cm:InstallNetRuntime}"; Filename: "https://dotnet.microsoft.com/download/dotnet/7.0"; IconFilename: "{sys}\shell32.dll"; IconIndex: 13
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: IsDotNet7Installed

[Code]
function IsDotNet7Installed(): Boolean;
var
  success: Boolean;
  installedVersion: String;
  runtimes: TArrayOfString;
  I: Integer;
  resultCode: Integer;
begin
  // 初始设置为未检测到
  Result := False;
  
  // 使用dotnet --list-runtimes命令检测安装的运行时
  // 创建临时批处理文件来执行命令并保存结果
  if not FileExists(ExpandConstant('{tmp}\check_dotnet.bat')) then
  begin
    SaveStringToFile(ExpandConstant('{tmp}\check_dotnet.bat'),
      'dotnet --list-runtimes > "' + ExpandConstant('{tmp}\dotnet_runtimes.txt') + '"' + #13#10 +
      'exit %ERRORLEVEL%', False);
  end;
  
  // 执行批处理文件
  if Exec(ExpandConstant('{tmp}\check_dotnet.bat'), '', '', SW_HIDE, ewWaitUntilTerminated, resultCode) then
  begin
    // 如果命令成功执行
    if resultCode = 0 then
    begin
      // 读取结果文件
      if LoadStringsFromFile(ExpandConstant('{tmp}\dotnet_runtimes.txt'), runtimes) then
      begin
        // 检查是否包含.NET 7.0
        for I := 0 to GetArrayLength(runtimes) - 1 do
        begin
          if Pos('Microsoft.NETCore.App 7.0.', runtimes[I]) > 0 then
          begin
            Result := True;
            break;
          end;
          
          // 也检查桌面运行时，因为我们的应用是WPF/WinForms
          if Pos('Microsoft.WindowsDesktop.App 7.0.', runtimes[I]) > 0 then
          begin
            Result := True;
            break;
          end;
        end;
      end;
    end;
  end;
  
  // 备选方法：如果上述方法失败，尝试检查注册表
  if not Result then
  begin
    // 检查.NET 7.0 Desktop Runtime是否安装
    success := RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', 'Version', installedVersion);
    if success and (Pos('7.0.', installedVersion) = 1) then
    begin
      Result := True;
    end
    else
    begin
      // 检查32位注册表
      success := RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App', 'Version', installedVersion);
      if success and (Pos('7.0.', installedVersion) = 1) then
      begin
        Result := True;
      end;
    end;
  end;
  
  // 最简单的备用方法：检查典型的.NET 7安装路径中的文件是否存在
  if not Result then
  begin
    if DirExists(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\7.0')) then
    begin
      Result := True;
    end;
  end;
  
  DeleteFile(ExpandConstant('{tmp}\check_dotnet.bat'));
  DeleteFile(ExpandConstant('{tmp}\dotnet_runtimes.txt'));
end;

function InitializeSetup(): Boolean;
begin
  Result := True; // 总是允许继续安装，因为我们有备选方案
end;