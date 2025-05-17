// Program_v3.cs (Dead-Lock-Free Edition)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ArtfulWall.Models;
using ArtfulWall.Services;
using ArtfulWall.UI;

namespace ArtfulWall.Core
{
    internal static class Constants
    {
        public const int    DefaultImageCacheSize = 10;
        public const string BackupFileName        = "config_backup.json";
        public const string AutoStartKey          = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string AutoStartValue        = "ArtfulWall";
        public static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".bmp" };
    }

    public static class Program
    {
        /// <summary>
        /// Indicates whether the application is running in portable mode -- for external components to read.
        /// </summary>
        public static bool IsPortable { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            // Initialize application configuration for high DPI, default font, etc.
            ApplicationConfiguration.Initialize();
            // Start the tray application context with command-line arguments
            Application.Run(new TrayAppContext(args));
        }

        /// <summary>
        /// WinForms tray application context: manages the message loop, resources, and exit logic.
        /// </summary>
        private sealed class TrayAppContext : ApplicationContext
        {
            // -------------------- Fields --------------------
            private NotifyIcon?       _trayIcon;
            private WallpaperUpdater? _updater;

            private readonly string _configPath;
            private readonly string _baseConfigPath;
            private readonly string _backupPath;

            // ---------------- Constructor & Initialization ----------------
            public TrayAppContext(string[] args)
            {
                // 1. Parse configuration paths and determine portable mode
                string exeDir   = AppDomain.CurrentDomain.BaseDirectory;
                _baseConfigPath = Path.Combine(exeDir, "config.json");

                string roamingRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ArtfulWall");
                Directory.CreateDirectory(roamingRoot);
                string roamingConfigPath = Path.Combine(roamingRoot, "config.json");

                bool inProgramFiles =
                       exeDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                         StringComparison.OrdinalIgnoreCase) ||
                       exeDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                         StringComparison.OrdinalIgnoreCase);

                // Determine portable mode by argument or presence of base config outside Program Files or Windows
                IsPortable = args.Contains("--portable") ||
                             (File.Exists(_baseConfigPath) && !inProgramFiles);

                _configPath = IsPortable ? _baseConfigPath : roamingConfigPath;
                _backupPath = Path.Combine(Path.GetDirectoryName(_configPath)!, Constants.BackupFileName);

                // 2. Ensure configuration file exists asynchronously to avoid deadlocks
                try
                {
                    EnsureConfigFilePresentAsync()
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    // Exit if configuration cannot be prepared
                    ExitThread();
                    return;
                }

                // 3. Load and validate configuration
                if (!TryLoadConfiguration(out Configuration cfg)) { ExitThread(); return; }

                // Warn if no image files are found in source folder
                if (!HasImageFiles(cfg.FolderPath))
                {
                    MessageBox.Show(
                        $"No images (jpg/png/bmp) found in directory \"{cfg.FolderPath}\".",
                        "No Images Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // 4. Start the core wallpaper updater service
                if (!StartUpdater(cfg))  { ExitThread(); return; }

                // 5. Create system tray icon and context menu
                CreateNotifyIcon(exeDir);
            }

            // ---------------- Configuration / Startup ----------------

            /// <summary>
            /// Asynchronously ensure the configuration file exists. Uses ConfigureAwait(false) to avoid UI thread deadlock.
            /// </summary>
            private async Task EnsureConfigFilePresentAsync()
            {
                if (File.Exists(_configPath)) return;

                try
                {
                    if (File.Exists(_backupPath))
                        await CopyFileAsync(_backupPath, _configPath).ConfigureAwait(false);
                    else if (File.Exists(_baseConfigPath))
                        await CopyFileAsync(_baseConfigPath, _configPath).ConfigureAwait(false);
                    else
                        throw new FileNotFoundException("No valid configuration file found!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to prepare configuration file: {ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    throw;
                }
            }

            /// <summary>
            /// Attempt to load and validate the configuration.
            /// </summary>
            private bool TryLoadConfiguration(out Configuration cfg)
            {
                cfg = default!;
                try
                {
                    string json = File.ReadAllText(_configPath);
                    cfg = JsonSerializer.Deserialize<Configuration>(json)
                          ?? throw new JsonException("Deserialization returned null");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to read or parse configuration: {ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // Auto-complete destination folder if missing or invalid
                if (string.IsNullOrWhiteSpace(cfg.DestFolder) || !Directory.Exists(cfg.DestFolder))
                {
                    cfg.DestFolder = Path.Combine(cfg.FolderPath, "my_wallpaper");
                    Directory.CreateDirectory(cfg.DestFolder);

                    File.WriteAllText(_configPath,
                        JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                }

                // Strictly validate all configuration fields
                try
                {
                    ValidateConfiguration(cfg);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Configuration validation failed: {ex.Message}",
                        "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Start the wallpaper updater service and schedule a backup of the configuration.
            /// </summary>
            private bool StartUpdater(Configuration c)
            {
                try
                {
                    _updater = new WallpaperUpdater(
                        c.FolderPath, c.DestFolder,
                        c.Width, c.Height,
                        c.Rows,  c.Cols,
                        new ImageManager(Constants.DefaultImageCacheSize),
                        c.MinInterval, c.MaxInterval,
                        c);
                    _updater.Start();

                    // After successful start, back up configuration asynchronously
                    _ = Task.Run(() => File.Copy(_configPath, _backupPath, true));
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to start wallpaper updater: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            // ---------------- NotifyIcon & Menu ----------------
            private void CreateNotifyIcon(string exeDir)
            {
                var menu = new ContextMenuStrip
                {
                    Items =
                    {
                        new ToolStripMenuItem("Auto-start on boot", null, ToggleAutoStart)
                        {
                            Checked = IsAutoStartEnabled()
                        },
                        new ToolStripMenuItem("Configuration Editor",   null, OpenConfigEditor),
                        new ToolStripMenuItem("Open Image Folder", null, OpenImageFolder),
                        new ToolStripSeparator(),
                        new ToolStripMenuItem("Exit", null, ExitApp)
                    }
                };

                _trayIcon = new NotifyIcon
                {
                    Icon             = new Icon(Path.Combine(exeDir, "appicon.ico")),
                    ContextMenuStrip = menu,
                    Text             = "ArtfulWall",
                    Visible          = true
                };
            }

            // ---------------- Menu Callbacks ----------------
            private void ToggleAutoStart(object? sender, EventArgs e)
            {
                if (sender is not ToolStripMenuItem item) return;

                bool enable = !item.Checked;
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(Constants.AutoStartKey, true);
                    if (key != null)
                    {
                        if (enable)
                            key.SetValue(Constants.AutoStartValue,
                                         $"\"{Process.GetCurrentProcess().MainModule?.FileName}\"");
                        else
                            key.DeleteValue(Constants.AutoStartValue, false);

                        item.Checked = enable;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to set auto-start on boot: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            private static bool IsAutoStartEnabled()
            {
                using var key = Registry.CurrentUser.OpenSubKey(Constants.AutoStartKey);
                return key?.GetValue(Constants.AutoStartValue) != null;
            }

            private void OpenConfigEditor(object? s, EventArgs e)
            {
                using var dlg = new ConfigEditorForm(_configPath);
                if (_updater != null) dlg.SetWallpaperUpdater(_updater);

                dlg.ShowDialog();

                // If config changed before updater start, restart application
                if (dlg.ConfigChanged && _updater == null)
                    Application.Restart();
            }

            private void OpenImageFolder(object? s, EventArgs e)
            {
                try
                {
                    var cfg = JsonSerializer.Deserialize<Configuration>(
                                  File.ReadAllText(_configPath));

                    if (cfg != null && Directory.Exists(cfg.FolderPath))
                        Process.Start("explorer.exe", cfg.FolderPath);
                    else
                        throw new DirectoryNotFoundException();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to open image folder: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            private void ExitApp(object? s, EventArgs e) => ExitThread();

            // ---------------- ApplicationContext Lifecycle ----------------
            protected override void ExitThreadCore()
            {
                // Dispose updater service
                _updater?.Dispose();

                // Hide and dispose tray icon
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }

                base.ExitThreadCore();
            }

            // ---------------- Helper Static Methods ----------------
            private static bool HasImageFiles(string folder)
            {
                return Directory.Exists(folder) &&
                       Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                                .Any(f => Constants.AllowedExt
                                              .Contains(Path.GetExtension(f).ToLowerInvariant()) &&
                                          !f.EndsWith("wallpaper.jpg",
                                                      StringComparison.OrdinalIgnoreCase));
            }

            private static void ValidateConfiguration(Configuration c)
            {
                if (!Directory.Exists(c.FolderPath))
                    throw new DirectoryNotFoundException($"Source folder '{c.FolderPath}' does not exist.");
                if (c.Width  <= 0 || c.Height <= 0)
                    throw new ArgumentException("Wallpaper width and height must be positive.");
                if (c.Rows   <= 0 || c.Cols   <= 0)
                    throw new ArgumentException("Rows and columns must be positive.");
                if (c.MinInterval <= 0)
                    throw new ArgumentException("Minimum interval must be positive.");
                if (c.MaxInterval < c.MinInterval)
                    throw new ArgumentException("Maximum interval cannot be less than minimum interval.");
            }

            /// <summary>
            /// Asynchronously copy files. All ConfigureAwait(false) calls eliminate UI thread deadlock risk.
            /// </summary>
            private static async Task CopyFileAsync(string src, string dst, bool overwrite = false)
            {
                const int bufferSize = 81920;
                await using var source = new FileStream(
                    src, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize, useAsync: true);

                await using var dest = new FileStream(
                    dst,
                    overwrite ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None,
                    bufferSize, useAsync: true);

                await source.CopyToAsync(dest, bufferSize).ConfigureAwait(false);
            }
        }
    }
}
