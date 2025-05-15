using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.Win32;
using ArtfulWall.Models;
using ArtfulWall.Services;
using ArtfulWall.UI;

namespace ArtfulWall.Core
{
    public static class Program
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
                                updater = new WallpaperUpdater(                    config.FolderPath!,                    config.DestFolder!,                    config.Width,                    config.Height,                    config.Rows,                    config.Cols,                    imageManager,                    config.MinInterval,                    config.MaxInterval,                    config);
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

                var contextMenu = new ContextMenuStrip();
                
                trayIcon = new NotifyIcon
                {
                    Icon = appIcon,
                    Visible = true,
                    ContextMenuStrip = contextMenu
                };
                
                // 添加MouseUp事件处理以在鼠标位置显示菜单
                trayIcon.MouseUp += (s, e) => 
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        // 获取鼠标的屏幕坐标
                        contextMenu.Show(Cursor.Position);
                    }
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
            if (updater != null)
            {
                editor.SetWallpaperUpdater(updater);
            }
            editor.ShowDialog();

            if (editor.ConfigChanged && updater == null)
            {
                // 如果配置已更改但无法立即应用，则重启应用程序
                RestartApplication();
            }
        }

        private static void OpenImageFolder(object? sender, EventArgs e)
        {
            try
            {
                var configText = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Configuration>(configText);

                if (config != null && !string.IsNullOrWhiteSpace(config.FolderPath) && Directory.Exists(config.FolderPath))
                {
                    Process.Start("explorer.exe", config.FolderPath);
                }
                else
                {
                    MessageBox.Show(
                        "图片文件夹路径无效或不存在。请先设置有效的图片文件夹路径。",
                        "错误",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"打开图片文件夹时发生错误：{ex.Message}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void ToggleAutoStart(object? sender, EventArgs e)
        {
            if (autoStartMenuItem != null)
            {
                bool currentState = autoStartMenuItem.Checked;
                SetAutoStart(!currentState);
                autoStartMenuItem.Checked = !currentState;
            }
        }

        private static bool IsAutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            return key?.GetValue("MyWallpaperApp") != null;
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                        key.SetValue("MyWallpaperApp", $"\"{appPath}\"");
                    }
                    else
                    {
                        key.DeleteValue("MyWallpaperApp", false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"设置开机自启动时发生错误：{ex.Message}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void OnExit(object? sender, EventArgs e)
        {
            updater?.Dispose();
            trayIcon?.Dispose();
            appIcon?.Dispose();
            Application.Exit();
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            updater?.Dispose();
            trayIcon?.Dispose();
            appIcon?.Dispose();
        }

        static void ValidateConfiguration(Configuration config)
        {
            if (string.IsNullOrWhiteSpace(config.FolderPath))
                throw new ArgumentException("必须设置源图片文件夹路径。");

            if (!Directory.Exists(config.FolderPath))
                throw new DirectoryNotFoundException($"指定的源图片文件夹 '{config.FolderPath}' 不存在。");

            if (config.Width <= 0 || config.Height <= 0)
                throw new ArgumentException("壁纸宽度和高度必须为正数。");

            if (config.Rows <= 0 || config.Cols <= 0)
                throw new ArgumentException("行数和列数必须为正数。");

            if (config.MinInterval <= 0)
                throw new ArgumentException("最小更新间隔必须为正数。");

            if (config.MaxInterval < config.MinInterval)
                throw new ArgumentException("最大更新间隔不能小于最小更新间隔。");
        }

        public static void RestartApplication()
        {
            try
            {
                string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(appPath))
                {
                    Process.Start(appPath);
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"重启应用程序时发生错误：{ex.Message}",
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
} 