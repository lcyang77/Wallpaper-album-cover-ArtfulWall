using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;

class Program
{
    private static NotifyIcon? trayIcon;
    private static ToolStripMenuItem? autoStartMenuItem;
    private static string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string appFolder = Path.Combine(appDataPath, "MyWallpaperApp");
    private static string configPath = Path.Combine(appFolder, "config.json");
    private static WallpaperUpdater? updater;
    private static Icon? appIcon; // 移动到此处以保持图标的引用

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        InitializeTrayIcon();

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        if (!File.Exists(configPath))
        {
            if (!RestoreBackupConfigFile()) // 尝试恢复备份
            {
                var defaultConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (File.Exists(defaultConfigPath))
                {
                    File.Copy(defaultConfigPath, configPath);
                }
                else
                {
                    MessageBox.Show("默认配置文件未找到。请确保应用程序目录中存在 config.json 文件。", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
        }

        try
        {
            var configText = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Configuration>(configText);

            if (config == null)
            {
                MessageBox.Show("配置文件无法解析。请确保 config.json 的格式正确。", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ValidateConfiguration(config);

            // 创建 ImageManager 实例
            ImageManager imageManager = new ImageManager(10); // Assuming max cache size is 10

            // 创建并启动 WallpaperUpdater
            updater = new WallpaperUpdater(
                config.FolderPath, 
                config.DestFolder, 
                config.Width, 
                config.Height, 
                config.Rows, 
                config.Cols, 
                imageManager,
                config.MinInterval, // 从配置中读取 MinInterval
                config.MaxInterval // 从配置中读取 MaxInterval
            );
            updater.Start();

            // 配置文件读取和处理成功后备份
            BackupConfigFile();
        }
        catch (JsonException)
        {
            MessageBox.Show("配置文件格式错误。请确保 config.json 的格式正确。", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        Application.Run();
    }

    private static void BackupConfigFile()
    {
        string backupConfigPath = Path.Combine(appFolder, "config_backup.json");
        File.Copy(configPath, backupConfigPath, true);
    }

    private static bool RestoreBackupConfigFile()
    {
        string backupConfigPath = Path.Combine(appFolder, "config_backup.json");
        if (File.Exists(backupConfigPath))
        {
            File.Copy(backupConfigPath, configPath, true);
            return true;
        }
        return false;
    }

    private static void InitializeTrayIcon()
    {
        try
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(appDirectory, "appicon.ico");

            appIcon = new Icon(iconPath); // 直接创建图标

            trayIcon = new NotifyIcon()
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
            MessageBox.Show($"初始化系统托盘图标时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenConfigEditor(object? sender, EventArgs e)
    {
        ConfigEditorForm editor = new ConfigEditorForm(configPath);
        editor.ShowDialog();
    }

    private static void OpenImageFolder(object? sender, EventArgs e)
    {
        if (File.Exists(configPath))
        {
            var configText = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<Configuration>(configText);
            if (config != null && !string.IsNullOrWhiteSpace(config.FolderPath) && Directory.Exists(config.FolderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", config.FolderPath);
            }
            else
            {
                MessageBox.Show("图片文件夹路径无效或未设置。请先在配置编辑器中设置正确的路径。", "路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            MessageBox.Show("未找到配置文件。请先创建配置文件。", "配置文件不存在", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void ToggleAutoStart(object? sender, EventArgs e)
    {
        bool enabled = !IsAutoStartEnabled();
        SetAutoStart(enabled);
        autoStartMenuItem.Checked = enabled;
    }

    private static bool IsAutoStartEnabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
        {
            return key.GetValue("MyWallpaperApp") != null;
        }
    }

    private static void SetAutoStart(bool enable)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
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
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        // Dispose of WallpaperUpdater before exiting
        updater?.Dispose();

        // 释放图标资源
        appIcon?.Dispose();
        trayIcon?.Dispose();
        Application.Exit();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        // Ensure that WallpaperUpdater is disposed properly when the application exits
        updater?.Dispose();

        // 释放图标资源
        appIcon?.Dispose();
        trayIcon?.Dispose();
    }


    static void ValidateConfiguration(Configuration config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        
        if (string.IsNullOrWhiteSpace(config.FolderPath))
            throw new InvalidOperationException("源文件夹路径不能为空或 null。");
        
        if (!Directory.Exists(config.FolderPath))
            throw new InvalidOperationException("源文件夹路径无效或不存在。");

        if (string.IsNullOrWhiteSpace(config.DestFolder) || !Directory.Exists(config.DestFolder))
            throw new InvalidOperationException("目标文件夹路径无效或不存在。");

        if (config.Width <= 0 || config.Height <= 0)
            throw new InvalidOperationException("图片宽度和高度必须大于 0。");

        if (config.Rows <= 0 || config.Cols <= 0)
            throw new InvalidOperationException("行数和列数必须大于 0。");
    }
}

public class Configuration
{
    public string? FolderPath { get; set; }
    public string? DestFolder { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Rows { get; set; }
    public int Cols { get; set; }
    public int MinInterval { get; set; } = 3;  // 默认值为 3
    public int MaxInterval { get; set; } = 10; // 默认值为 10
}
