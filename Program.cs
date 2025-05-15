// Program.cs
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using System.Collections.Generic;

static class Program
{
    private static NotifyIcon? trayIcon;
    private static ToolStripMenuItem? autoStartMenuItem;
    private static string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string appFolder = Path.Combine(appDataPath, "MyWallpaperApp");
    private static string configPath = "";
    private static string baseConfigPath = "";
    private static bool isPortable = false;
    private static WallpaperUpdater? updater;
    private static Icon? appIcon;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // 1. 确定配置路径：可移植模式 vs. Roaming 模式
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        baseConfigPath = Path.Combine(exeDir, "config.json");
        string roamingConfigPath = Path.Combine(appFolder, "config.json");

        bool inProgramFiles = exeDir.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            StringComparison.OrdinalIgnoreCase)
            || exeDir.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            StringComparison.OrdinalIgnoreCase);

        if (args.Contains("--portable") ||
            (File.Exists(baseConfigPath) && !inProgramFiles))
        {
            isPortable = true;
            configPath = baseConfigPath;
        }
        else
        {
            isPortable = false;
            configPath = roamingConfigPath;

            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            if (!File.Exists(configPath))
            {
                if (!RestoreBackupConfigFile())
                {
                    if (File.Exists(baseConfigPath))
                        File.Copy(baseConfigPath, configPath);
                    else
                    {
                        MessageBox.Show(
                            "默认配置文件未找到。请确保应用程序目录中存在 config.json。",
                            "配置错误",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }
            }
        }

        // 2. 初始化托盘图标
        InitializeTrayIcon();

        // 3. 读取并应用配置
        try
        {
            var configText = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Configuration>(configText);
            if (config == null)
            {
                MessageBox.Show(
                    "配置文件无法解析。请确保 config.json 的格式正确。",
                    "配置错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // 自动创建 my_wallpaper 子目录作为 DestFolder
            if (string.IsNullOrWhiteSpace(config.DestFolder) || !Directory.Exists(config.DestFolder))
            {
                string autoDest = Path.Combine(config.FolderPath!, "my_wallpaper");
                Directory.CreateDirectory(autoDest);
                config.DestFolder = autoDest;

                // 写回更新后的配置
                File.WriteAllText(configPath,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }

            ValidateConfiguration(config);
            
            // 检查源文件夹是否包含图片文件
            var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
            bool hasImageFiles = Directory
                .EnumerateFiles(config.FolderPath!, "*.*", SearchOption.TopDirectoryOnly)
                .Any(file => 
                    allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                    !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase));
                    
            if (!hasImageFiles)
            {
                MessageBox.Show(
                    $"警告：在设定的文件夹 \"{config.FolderPath}\" 中未找到任何图片文件。\n\n请添加JPG、PNG或BMP格式的图片到该文件夹，或修改配置指向一个包含图片的文件夹。",
                    "未找到图片",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var imageManager = new ImageManager(10);
            updater = new WallpaperUpdater(
                config.FolderPath!,
                config.DestFolder!,
                config.Width,
                config.Height,
                config.Rows,
                config.Cols,
                imageManager,
                config.MinInterval,
                config.MaxInterval);
            updater.Start();

            BackupConfigFile();
        }
        catch (JsonException)
        {
            MessageBox.Show(
                "配置文件格式错误。请确保 config.json 的格式正确。",
                "配置错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (UnauthorizedAccessException uae)
        {
            MessageBox.Show(
                $"无法访问配置文件路径，请检查文件权限：{uae.Message}",
                "权限错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"发生错误：{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        Application.Run();
    }

    static bool RestoreBackupConfigFile()
    {
        string backupConfigPath = Path.Combine(
            Path.GetDirectoryName(configPath)!,
            "config_backup.json");
        if (File.Exists(backupConfigPath))
        {
            try
            {
                File.Copy(backupConfigPath, configPath, true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"恢复备份配置文件失败：{ex.Message}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        return false;
    }

    static void BackupConfigFile()
    {
        string backupConfigPath = Path.Combine(
            Path.GetDirectoryName(configPath)!,
            "config_backup.json");
        try
        {
            File.Copy(configPath, backupConfigPath, true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"备份配置文件失败：{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    static void InitializeTrayIcon()
    {
        try
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(appDirectory, "appicon.ico");
            appIcon = new Icon(iconPath);

            trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            autoStartMenuItem = new ToolStripMenuItem("开机自启动", null, ToggleAutoStart)
            {
                Checked = IsAutoStartEnabled()
            };
            trayIcon.ContextMenuStrip.Items.Add(autoStartMenuItem);
            trayIcon.ContextMenuStrip.Items.Add("配置编辑器", null, OpenConfigEditor);
            trayIcon.ContextMenuStrip.Items.Add("打开图片文件夹", null, OpenImageFolder);
            trayIcon.ContextMenuStrip.Items.Add("退出", null, OnExit);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"初始化系统托盘图标时发生错误：{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void OpenConfigEditor(object? sender, EventArgs e)
    {
        var editor = new ConfigEditorForm(configPath);
        var result = editor.ShowDialog();
        
        // 处理配置变更
        if (result == DialogResult.OK)
        {
            // 用户选择了立即应用更改
            try
            {
                var configText = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Configuration>(configText);
                if (config == null)
                {
                    MessageBox.Show(
                        "配置文件格式错误，无法应用更改。",
                        "配置错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                
                // 验证新配置
                try
                {
                    ValidateConfiguration(config);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"新配置无效: {ex.Message}",
                        "配置错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
                
                // 动态更新WallpaperUpdater配置
                if (updater != null)
                {
                    updater.UpdateConfig(config);
                    MessageBox.Show(
                        "已应用新配置。",
                        "配置已应用",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"应用新配置时发生错误: {ex.Message}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        else if (result == DialogResult.Yes)
        {
            // 用户修改了配置但选择不立即应用
            MessageBox.Show(
                "配置已保存，重启应用后生效。",
                "需要重启",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static void OpenImageFolder(object? sender, EventArgs e)
    {
        if (File.Exists(configPath))
        {
            var configText = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Configuration>(configText);
            if (config != null
                && !string.IsNullOrWhiteSpace(config.FolderPath)
                && Directory.Exists(config.FolderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", config.FolderPath);
            }
            else
            {
                MessageBox.Show(
                    "图片文件夹路径无效或未设置。请先在配置编辑器中设置正确的路径。",
                    "路径无效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        else
        {
            MessageBox.Show(
                "未找到配置文件。请先创建配置文件。",
                "配置文件不存在",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void ToggleAutoStart(object? sender, EventArgs e)
    {
        bool enabled = !IsAutoStartEnabled();
        SetAutoStart(enabled);
        if (autoStartMenuItem != null)
            autoStartMenuItem.Checked = enabled;
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue("MyWallpaperApp") != null;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enable)
        {
            string exePath = Application.ExecutablePath;
            key.SetValue("MyWallpaperApp", exePath);
        }
        else
        {
            key.DeleteValue("MyWallpaperApp", false);
        }
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        updater?.Dispose();
        appIcon?.Dispose();
        trayIcon?.Dispose();
        Application.Exit();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        updater?.Dispose();
        appIcon?.Dispose();
        trayIcon?.Dispose();
    }

    static void ValidateConfiguration(Configuration config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.FolderPath) || !Directory.Exists(config.FolderPath))
            throw new InvalidOperationException("源文件夹路径无效或不存在。");
        
        // 检查文件夹是否包含图片文件
        var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
        var hasImageFiles = Directory
            .EnumerateFiles(config.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Any(file => 
                allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase));
        
        // 此处仅检查，不抛出异常，因为提醒会在 WallpaperUpdater 中显示
                
        // 不再检查 DestFolder，因为自动创建
        if (config.Width <= 0 || config.Height <= 0)
            throw new InvalidOperationException("图片宽度和高度必须大于 0。");
        if (config.Rows <= 0 || config.Cols <= 0)
            throw new InvalidOperationException("行数和列数必须大于 0。");
    }

    public static void RestartApplication()
    {
        Application.Restart();
        Environment.Exit(0);
    }
}
