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
        /// <summary>是否以“便携模式”运行 —— 供外部组件读取。</summary>
        public static bool IsPortable { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayAppContext(args));
        }

        /// <summary>
        /// WinForms 托盘应用上下文：负责消息循环、资源管理和退出逻辑。
        /// </summary>
        private sealed class TrayAppContext : ApplicationContext
        {
            // ————— 字段 —————
            private NotifyIcon?       _trayIcon;
            private WallpaperUpdater? _updater;

            private readonly string _configPath;
            private readonly string _baseConfigPath;
            private readonly string _backupPath;

            // ———————————————————— 构造 & 初始化 ————————————————————
            public TrayAppContext(string[] args)
            {
                // 1️⃣ 解析配置路径（含便携模式判定） ---------------------------
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

                IsPortable = args.Contains("--portable") ||
                             (File.Exists(_baseConfigPath) && !inProgramFiles);

                _configPath = IsPortable ? _baseConfigPath : roamingConfigPath;
                _backupPath = Path.Combine(Path.GetDirectoryName(_configPath)!, Constants.BackupFileName);

                // ⏱️ 2️⃣ 确保配置文件存在 —— 取消同步等待死锁风险
                try
                {
                    EnsureConfigFilePresentAsync()
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    ExitThread();
                    return;
                }

                // 3️⃣ 读取 & 校验配置 ------------------------------------------
                if (!TryLoadConfiguration(out Configuration cfg)) { ExitThread(); return; }

                // 无图片预警
                if (!HasImageFiles(cfg.FolderPath))
                {
                    MessageBox.Show($"在目录 \"{cfg.FolderPath}\" 中未找到任何图片（jpg/png/bmp）。",
                                    "未找到图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // 4️⃣ 启动业务核心（WallpaperUpdater） -------------------------
                if (!StartUpdater(cfg))  { ExitThread(); return; }

                // 5️⃣ 创建 NotifyIcon & 菜单 -----------------------------------
                CreateNotifyIcon(exeDir);
            }

            // ———————————————————— 配置 / 启动 ————————————————————

            /// <summary>
            /// 异步确保配置文件存在。使用 ConfigureAwait(false) 消除 UI 线程死锁风险。
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
                        throw new FileNotFoundException("未找到任何有效配置文件！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"准备配置文件失败：{ex.Message}",
                                    "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    throw;
                }
            }

            private bool TryLoadConfiguration(out Configuration cfg)
            {
                cfg = default!;
                try
                {
                    string json = File.ReadAllText(_configPath);
                    cfg = JsonSerializer.Deserialize<Configuration>(json)
                          ?? throw new JsonException("反序列化返回 null");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"读取或解析配置失败：{ex.Message}",
                                    "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // 自动补全 DestFolder
                if (string.IsNullOrWhiteSpace(cfg.DestFolder) || !Directory.Exists(cfg.DestFolder))
                {
                    cfg.DestFolder = Path.Combine(cfg.FolderPath, "my_wallpaper");
                    Directory.CreateDirectory(cfg.DestFolder);

                    File.WriteAllText(_configPath,
                        JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                }

                // 严格校验字段
                try
                {
                    ValidateConfiguration(cfg);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"配置校验失败：{ex.Message}",
                                    "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                return true;
            }

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

                    // 启动成功后异步备份
                    _ = Task.Run(() => File.Copy(_configPath, _backupPath, true));
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"启动壁纸更新失败：{ex.Message}",
                                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            // ———————————————————— NotifyIcon & 菜单 ————————————————————
            private void CreateNotifyIcon(string exeDir)
            {
                var menu = new ContextMenuStrip
                {
                    Items =
                    {
                        new ToolStripMenuItem("开机自启动", null, ToggleAutoStart)
                        {
                            Checked = IsAutoStartEnabled()
                        },
                        new ToolStripMenuItem("配置编辑器",   null, OpenConfigEditor),
                        new ToolStripMenuItem("打开图片文件夹", null, OpenImageFolder),
                        new ToolStripSeparator(),
                        new ToolStripMenuItem("退出", null, ExitApp)
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

            // ———————————————————— 菜单回调 ————————————————————
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
                    MessageBox.Show($"设置开机自启动失败：{ex.Message}",
                                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    MessageBox.Show($"无法打开图片文件夹：{ex.Message}",
                                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            private void ExitApp(object? s, EventArgs e) => ExitThread();

            // ———————————————————— ApplicationContext 生命周期 ————————————————————
            protected override void ExitThreadCore()
            {
                _updater?.Dispose();

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }

                base.ExitThreadCore();
            }

            // ———————————————————— 辅助静态方法 ————————————————————
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
                    throw new DirectoryNotFoundException($"源文件夹 '{c.FolderPath}' 不存在。");
                if (c.Width  <= 0 || c.Height <= 0)
                    throw new ArgumentException("壁纸宽高必须为正数。");
                if (c.Rows   <= 0 || c.Cols   <= 0)
                    throw new ArgumentException("行列数必须为正数。");
                if (c.MinInterval <= 0)
                    throw new ArgumentException("最小间隔必须为正数。");
                if (c.MaxInterval < c.MinInterval)
                    throw new ArgumentException("最大间隔不能小于最小间隔。");
            }

            /// <summary>异步复制文件。全部 ConfigureAwait(false) 以消除 UI 死锁风险。</summary>
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
