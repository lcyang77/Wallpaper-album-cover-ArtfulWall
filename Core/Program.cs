// Program.cs
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        public const int DefaultImageCacheSize = 10;
        public const string BackupFileName     = "config_backup.json";
        public const string AutoStartKey       = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string AutoStartValue     = "ArtfulWall";
        public static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".bmp" };
    }

    public static class Program
    {
        // Single-instance mutex name
        private const string MutexName = "Global\\ArtfulWallSingleton";

        [STAThread]
        private static void Main()
        {
            // Single-instance check
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("ArtfulWall is already running.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // WinForms high DPI and style configuration
            ApplicationConfiguration.Initialize();

            // Create a unified cancellation token source for all background tasks
            var cts = new CancellationTokenSource();

            // Start the message loop immediately; further initialization runs asynchronously in TrayAppContext
            Application.Run(new TrayAppContext(cts));
        }
    }

    /// <summary>
    /// Tray application context responsible for:
    /// showing the tray icon → asynchronous initialization →
    /// exit on initialization failure or continue loading menu/business →
    /// unified cancellation & cleanup on exit.
    /// </summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly CancellationTokenSource _cts;
        private readonly string _configPath;
        private readonly string _backupPath;
        private NotifyIcon?       _trayIcon;
        private WallpaperUpdater? _updater;

        public TrayAppContext(CancellationTokenSource cts)
        {
            _cts = cts;

            // Configuration file path (fixed location under Roaming)
            string exeDir   = AppDomain.CurrentDomain.BaseDirectory;
            string roaming  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ArtfulWall");
            Directory.CreateDirectory(roaming);

            _configPath = Path.Combine(roaming, "config.json");
            _backupPath = Path.Combine(roaming, Constants.BackupFileName);

            // 1️⃣ Immediately show a "starting..." tray icon so UI is responsive
            InitializeTrayIconLoading(exeDir);

            // 2️⃣ Run real initialization asynchronously, never blocking the UI thread
            _ = InitializeAsync(exeDir);
        }

        /// <summary>
        /// Create tray icon and display "starting..." tooltip
        /// </summary>
        private void InitializeTrayIconLoading(string exeDir)
        {
            _trayIcon = new NotifyIcon
            {
                Icon    = SystemIcons.Application,
                Text    = "ArtfulWall is starting...",
                Visible = true
            };
        }

        /// <summary>
        /// The real initialization flow:
        /// prepare config → load & validate → start business → build menu
        /// </summary>
        private async Task InitializeAsync(string exeDir)
        {
            try
            {
                // 2.1 Ensure config file is present (async copy, zero blocking)
                await EnsureConfigFilePresentAsync(_cts.Token).ConfigureAwait(false);

                // 2.2 Load & validate config (including atomic write of DestFolder)
                var cfg = await LoadAndValidateConfigAsync(_cts.Token).ConfigureAwait(false);

                // 2.3 Async scan and warn if no images
                _ = ScanAndWarnIfNoImagesAsync(cfg.FolderPath, _cts.Token);

                // 2.4 Start WallpaperUpdater and backup config asynchronously
                if (!StartUpdater(cfg))
                    throw new InvalidOperationException("Failed to start wallpaper updater");

                // 2.5 Switch tray icon to normal state and build context menu
                CreateContextMenu(exeDir);
                _trayIcon!.Icon = new Icon(Path.Combine(exeDir, "appicon.ico"));
                _trayIcon.Text = "ArtfulWall";
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // User requested exit; no error dialog
                ExitThread();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        #region Configuration Preparation

        private static async Task EnsureConfigFilePresentAsync(CancellationToken token)
        {
            string exeDir   = AppDomain.CurrentDomain.BaseDirectory;
            string roaming  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ArtfulWall");
            string config   = Path.Combine(roaming, "config.json");
            string backup   = Path.Combine(roaming, Constants.BackupFileName);
            string baseCfg  = Path.Combine(exeDir, "config.json");

            if (File.Exists(config))
                return;

            string src = File.Exists(backup) ? backup
                       : File.Exists(baseCfg) ? baseCfg
                       : throw new FileNotFoundException("No valid configuration file found!");
            await CopyFileAsync(src, config, token).ConfigureAwait(false);
        }

        private static async Task CopyFileAsync(string src, string dst, CancellationToken token, bool overwrite = false)
        {
            const int bufSize = 81920;
            await using var srcFs = new FileStream(
                src, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufSize, useAsync: true);
            await using var dstFs = new FileStream(
                dst,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write, FileShare.None,
                bufSize, useAsync: true);
            await srcFs.CopyToAsync(dstFs, bufSize, token).ConfigureAwait(false);
        }

        #endregion

        #region Load & Validate Configuration (with Atomic Write)

        private async Task<Configuration> LoadAndValidateConfigAsync(CancellationToken token)
        {
            // Read
            string json = await Task.Run(() => File.ReadAllText(_configPath), token)
                                   .ConfigureAwait(false);
            var cfg = JsonSerializer.Deserialize<Configuration>(json)
                      ?? throw new JsonException("Deserialization returned null");

            // Auto-complete DestFolder
            if (string.IsNullOrWhiteSpace(cfg.DestFolder) || !Directory.Exists(cfg.DestFolder))
            {
                cfg.DestFolder = Path.Combine(cfg.FolderPath, "my_wallpaper");
                Directory.CreateDirectory(cfg.DestFolder);

                string tmp = _configPath + ".tmp";
                var opts = new JsonSerializerOptions { WriteIndented = true };
                await Task.Run(() =>
                {
                    File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, opts));
                    AtomicReplace(tmp, _configPath);
                }, token).ConfigureAwait(false);
            }

            // Validate
            ValidateConfiguration(cfg);
            return cfg;
        }

        private static void AtomicReplace(string tmpPath, string targetPath)
        {
            try
            {
                File.Replace(tmpPath, targetPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(targetPath);
                File.Move(tmpPath, targetPath);
            }
        }

        private static void ValidateConfiguration(Configuration c)
        {
            if (!Directory.Exists(c.FolderPath))
                throw new DirectoryNotFoundException($"Source folder '{c.FolderPath}' does not exist.");
            if (c.Width  <= 0 || c.Height <= 0)
                throw new ArgumentException("Wallpaper width and height must be positive numbers.");
            if (c.Rows   <= 0 || c.Cols   <= 0)
                throw new ArgumentException("Rows and columns must be positive numbers.");
            if (c.MinInterval <= 0)
                throw new ArgumentException("Minimum interval must be positive.");
            if (c.MaxInterval < c.MinInterval)
                throw new ArgumentException("Maximum interval cannot be less than minimum interval.");
        }

        #endregion

        #region Async Scan for No Images

        private static async Task ScanAndWarnIfNoImagesAsync(string folder, CancellationToken token)
        {
            bool hasImages = await Task.Run(() =>
                Directory.Exists(folder) &&
                Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                         .Any(f => Constants.AllowedExt.Contains(Path.GetExtension(f).ToLowerInvariant())
                                && !f.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase))
            , token).ConfigureAwait(false);

            if (!hasImages)
            {
                MessageBox.Show(
                    $"No images (jpg/png/bmp) were found in the folder \"{folder}\".",
                    "No Images Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion

        #region Start WallpaperUpdater

        private bool StartUpdater(Configuration c)
        {
            try
            {
                _updater = new WallpaperUpdater(
                    c.FolderPath, c.DestFolder,
                    c.Width, c.Height,
                    c.Rows, c.Cols,
                    new ImageManager(Constants.DefaultImageCacheSize),
                    c.MinInterval, c.MaxInterval,
                    c);

                _updater.Start();

                // Backup config asynchronously
                _ = Task.Run(() =>
                {
                    try { File.Copy(_configPath, _backupPath, overwrite: true); }
                    catch (Exception ex) { Debug.WriteLine($"[ArtfulWall] Backup config failed: {ex}"); }
                }, _cts.Token);

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Tray Menu (including Auto Start)

        private void CreateContextMenu(string exeDir)
        {
            if (_trayIcon == null) return;

            var menu     = new ContextMenuStrip();
            var autoItem = new ToolStripMenuItem("Run at Startup", null, ToggleAutoStart)
            {
                Checked = IsAutoStartEnabled()
            };
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripMenuItem("Configuration Editor",     null, OpenConfigEditor));
            menu.Items.Add(new ToolStripMenuItem("Open Image Folder",        null, OpenImageFolder));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit",                     null, ExitApp));

            // Refresh status before each opening
            menu.Opening += (s, e) => autoItem.Checked = IsAutoStartEnabled();

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Visible          = true;
        }

        private void ToggleAutoStart(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem item) return;

            bool wantEnable    = !item.Checked;
            bool previousState = item.Checked;

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.AutoStartKey)
                             ?? throw new InvalidOperationException("Cannot create or open registry key");

                var existingVal        = key.GetValue(Constants.AutoStartValue) as string;
                var existingNormalized = existingVal?.Trim('"') ?? string.Empty;
                var existingFullPath   = string.IsNullOrEmpty(existingNormalized)
                    ? string.Empty
                    : Path.GetFullPath(existingNormalized);

                var exePath     = Process.GetCurrentProcess().MainModule?.FileName
                                      ?? Application.ExecutablePath;
                var exeFullPath = Path.GetFullPath(exePath);
                var valueToSet  = exeFullPath.Contains(" ") ? $"\"{exeFullPath}\"" : exeFullPath;

                // Idempotent checks
                if (wantEnable && existingFullPath.Equals(exeFullPath, StringComparison.OrdinalIgnoreCase))
                    return;
                if (!wantEnable && existingVal == null)
                    return;

                if (wantEnable)
                    key.SetValue(Constants.AutoStartValue, valueToSet);
                else
                    key.DeleteValue(Constants.AutoStartValue, throwOnMissingValue: false);

                item.Checked = wantEnable;
            }
            catch (Exception ex)
            {
                item.Checked = previousState;
                MessageBox.Show($"Failed to set run on startup: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsAutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.AutoStartKey);
            var val = key?.GetValue(Constants.AutoStartValue) as string;
            if (string.IsNullOrEmpty(val)) return false;

            var normalized = val.Trim('"');
            try
            {
                var full = Path.GetFullPath(normalized);
                var exe  = Process.GetCurrentProcess().MainModule?.FileName
                           ?? Application.ExecutablePath;
                return string.Equals(full, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void OpenConfigEditor(object? sender, EventArgs e)
        {
            using var dlg = new ConfigEditorForm(_configPath);
            if (_updater != null) dlg.SetWallpaperUpdater(_updater);

            dlg.ShowDialog();
            if (dlg.ConfigChanged && _updater == null)
                Application.Restart();
        }

        private void OpenImageFolder(object? sender, EventArgs e)
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<Configuration>(
                              File.ReadAllText(_configPath));
                if (cfg is null || !Directory.Exists(cfg.FolderPath))
                    throw new DirectoryNotFoundException();

                Process.Start("explorer.exe", cfg.FolderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot open image folder: {ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApp(object? sender, EventArgs e) => ExitThread();

        #endregion

        /// <summary>
        /// Cancels all asynchronous operations and releases resources on exit.
        /// </summary>
        protected override void ExitThreadCore()
        {
            _cts.Cancel();

            _updater?.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            base.ExitThreadCore();
        }
    }
}
