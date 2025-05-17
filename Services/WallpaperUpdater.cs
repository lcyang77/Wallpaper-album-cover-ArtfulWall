// WallpaperUpdater.cs
using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ArtfulWall.Models;
using ArtfulWall.Utils;
using Timer = System.Threading.Timer;
using Point = SixLabors.ImageSharp.Point;
using PointF = SixLabors.ImageSharp.PointF;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;

// To resolve Configuration conflict, alias ArtfulWall.Models.Configuration
using AWConfig = ArtfulWall.Models.Configuration;

namespace ArtfulWall.Services
{
    /// <summary>
    /// Handles wallpaper updates by dividing images into grids and periodically replacing grid covers.
    /// Supports single or per-monitor modes, DPI scaling, and dynamic display changes.
    /// </summary>
    public class WallpaperUpdater : IDisposable
    {
        private const int MaxLoadRetries = 3;
        private const double GridUpdateIntervalSeconds = 10;

        // Allowed image file extensions
        private readonly HashSet<string> _allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };

        // Configuration fields
        private int _minInterval;
        private int _maxInterval;
        private string _folderPath;
        private string _destFolder;
        private int _width;
        private int _height;
        private int _rows;
        private int _cols;
        private AWConfig.WallpaperMode _wallpaperMode;
        private bool _autoAdjustToDisplayChanges;
        private bool _adaptToDpiScaling;

        private readonly ImageManager _imageManager;
        private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<Grid, DateTime> _lastUpdateTimes;

        private List<string> _coverPaths;
        private List<Grid> _grids;
        private Image<Rgba32>? _wallpaper;
        private Timer? _timer;
        private bool _isFirstUpdate = true;
        private bool _disposed = false;

        // Fields for multi-monitor support
        private Dictionary<string, List<Grid>> _monitorGrids;
        private Dictionary<string, Image<Rgba32>> _monitorWallpapers;
        private List<DisplayInfo> _displays;
        private Dictionary<string, string> _monitorToDevicePathMap;

        // Stores current configuration
        private AWConfig? _currentConfig;
        private CancellationTokenSource? _displaySettingsChangeCts;

        /// <summary>
        /// Constructor to initialize WallpaperUpdater with configuration parameters.
        /// </summary>
        public WallpaperUpdater(
            string folderPath,
            string destFolder,
            int width,
            int height,
            int rows,
            int cols,
            ImageManager imageManager,
            int minInterval,
            int maxInterval,
            AWConfig? config = null
        )
        {
            _folderPath   = folderPath   ?? throw new ArgumentNullException(nameof(folderPath));
            _destFolder   = destFolder   ?? throw new ArgumentNullException(nameof(destFolder));
            _width        = width;
            _height       = height;
            _rows         = rows;
            _cols         = cols;
            _imageManager = imageManager ?? throw new ArgumentNullException(nameof(imageManager));
            _minInterval  = minInterval;
            _maxInterval  = maxInterval;
            _wallpaperMode = AWConfig.WallpaperMode.PerMonitor; // Default to per-monitor mode
            _autoAdjustToDisplayChanges = true;
            _adaptToDpiScaling = true;
            _currentConfig = config;

            _lastUpdateTimes      = new ConcurrentDictionary<Grid, DateTime>();
            _coverPaths           = new List<string>();
            _grids                = new List<Grid>();
            _monitorGrids         = new Dictionary<string, List<Grid>>();
            _monitorWallpapers    = new Dictionary<string, Image<Rgba32>>();
            _displays             = new List<DisplayInfo>();
            _monitorToDevicePathMap = new Dictionary<string, string>();
        }

        /// <summary>
        /// Starts the updater: validates folder, loads covers, detects displays, initializes grids, and schedules updates.
        /// </summary>
        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_folderPath) || !Directory.Exists(_folderPath))
            {
                Console.WriteLine("Invalid or non-existent folder path.");
                return;
            }

            LoadAlbumCovers();
            DetectDisplays();
            InitializeWallpaperAndGrids();

            if (_autoAdjustToDisplayChanges)
            {
                DisplayManager.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }

            ScheduleUpdate();
        }

        /// <summary>
        /// Updates runtime configuration and reinitializes related components.
        /// </summary>
        public void UpdateConfig(AWConfig cfg)
        {
            _folderPath  = cfg.FolderPath   ?? throw new ArgumentNullException(nameof(cfg.FolderPath));
            _destFolder  = cfg.DestFolder   ?? throw new ArgumentNullException(nameof(cfg.DestFolder));
            _width       = cfg.Width;
            _height      = cfg.Height;
            _rows        = cfg.Rows;
            _cols        = cfg.Cols;
            _minInterval = cfg.MinInterval;
            _maxInterval = cfg.MaxInterval;
            _wallpaperMode = cfg.Mode;
            _autoAdjustToDisplayChanges = cfg.AutoAdjustToDisplayChanges;
            _adaptToDpiScaling = cfg.AdaptToDpiScaling;
            
            _currentConfig = cfg.Clone(); // Clone to prevent external modifications

            Console.WriteLine($"Configuration updated - DPI Scaling: {_adaptToDpiScaling}, Wallpaper Mode: {_wallpaperMode}");

            LoadAlbumCovers();

            if (_autoAdjustToDisplayChanges)
            {
                DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                DisplayManager.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            else
            {
                DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }

            DetectDisplays();
            InitializeWallpaperAndGrids();

            _lastUpdateTimes.Clear();
            _isFirstUpdate = true;
            _timer?.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Detects active displays and updates internal display configuration.
        /// </summary>
        private void DetectDisplays()
        {
            try
            {
                var task = Task.Run(() => DisplayManager.GetDisplays());
                if (task.Wait(3000))
                {
                    _displays = task.Result;
                }
                else
                {
                    Console.WriteLine("Display detection timed out, using default display settings.");
                    _displays = GetDefaultDisplays();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error detecting displays: {ex.Message}");
                _displays = GetDefaultDisplays();
            }

            if (_displays == null || _displays.Count == 0)
            {
                _displays = GetDefaultDisplays();
            }

            UpdateDisplayConfigInfo();
            UpdateMonitorToDevicePathMap();
        }

        /// <summary>
        /// Synchronizes current display properties with the stored configuration.
        /// </summary>
        private void UpdateDisplayConfigInfo()
        {
            if (_currentConfig?.MonitorConfigurations == null)
                return;

            foreach (var display in _displays)
            {
                var monitorConfig = _currentConfig.MonitorConfigurations
                    .FirstOrDefault(mc => mc.DisplayNumber == display.DisplayNumber);

                if (monitorConfig != null)
                {
                    monitorConfig.Width       = display.Width;
                    monitorConfig.Height      = display.Height;
                    monitorConfig.DpiScaling  = display.DpiScaling;
                    monitorConfig.MonitorId   = display.DeviceName;
                    monitorConfig.IsPortrait  = display.Orientation == DisplayInfo.OrientationType.Portrait ||
                                                display.Orientation == DisplayInfo.OrientationType.PortraitFlipped;

                    Console.WriteLine($"Updated monitor {display.DisplayNumber} settings: Resolution={display.Width}x{display.Height}, Rows={monitorConfig.Rows}, Columns={monitorConfig.Cols}");
                }
            }
        }

        /// <summary>
        /// Maps display identifiers to device paths for per-monitor wallpaper support.
        /// </summary>
        private void UpdateMonitorToDevicePathMap()
        {
            _monitorToDevicePathMap.Clear();
            if (!DesktopWallpaperApi.IsPerMonitorWallpaperSupported())
                return;

            try
            {
                var wpInstance = (DesktopWallpaperApi.IDesktopWallpaper)new DesktopWallpaperApi.DesktopWallpaper();
                uint monitorCount = wpInstance.GetMonitorDevicePathCount();
                if (monitorCount == 0)
                    return;

                var monitorInfo = DesktopWallpaperApi.GetAllMonitorInfo();
                var mapped = new HashSet<string>();

                // Match by bounding rectangle
                foreach (var d in _displays)
                {
                    foreach (var kv in monitorInfo)
                    {
                        var rect = kv.Value;
                        if (rect.Left == d.Bounds.Left && rect.Top == d.Bounds.Top &&
                            rect.Right == d.Bounds.Right && rect.Bottom == d.Bounds.Bottom)
                        {
                            _monitorToDevicePathMap[d.DeviceName] = kv.Key;
                            mapped.Add(d.DeviceName);
                            Console.WriteLine($"Rectangle match: Display {d.DisplayNumber} -> {kv.Key}");
                            break;
                        }
                    }
                }

                // Match by index if needed
                for (int i = 0; i < Math.Min(_displays.Count, (int)monitorCount); i++)
                {
                    var d = _displays[i];
                    if (mapped.Contains(d.DeviceName))
                        continue;

                    try
                    {
                        string path = wpInstance.GetMonitorDevicePathAt((uint)i);
                        if (!string.IsNullOrEmpty(path))
                        {
                            _monitorToDevicePathMap[d.DeviceName] = path;
                            Console.WriteLine($"Index match: Display {i} -> {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error mapping index for display {i}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error mapping device paths: {ex.Message}");
            }

            Console.WriteLine($"Successfully mapped {_monitorToDevicePathMap.Count} displays");
        }

        /// <summary>
        /// Event handler for display setting changes, reconfigures wallpaper asynchronously.
        /// </summary>
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            _displaySettingsChangeCts?.Cancel();
            _displaySettingsChangeCts = new CancellationTokenSource();
            var token = _displaySettingsChangeCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    if (!await _updateLock.WaitAsync(TimeSpan.FromSeconds(5), token))
                    {
                        Console.WriteLine("Could not acquire update lock, skipping display change handling");
                        return;
                    }

                    try
                    {
                        if (token.IsCancellationRequested) return;
                        Console.WriteLine("Display settings changed, reinitializing wallpaper...");
                        DetectDisplays();
                        if (token.IsCancellationRequested) return;
                        InitializeWallpaperAndGrids();
                        if (token.IsCancellationRequested) return;
                        _lastUpdateTimes.Clear();
                        _isFirstUpdate = true;
                        await UpdateWallpaper();
                    }
                    finally
                    {
                        _updateLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Change handling cancelled");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling display change: {ex.Message}");
                }
            }, token);
        }

        /// <summary>
        /// Loads all valid image files from the specified folder into the cover list.
        /// </summary>
        private void LoadAlbumCovers()
        {
            int retries = 0;
            while (retries < MaxLoadRetries)
            {
                try
                {
                    _coverPaths = Directory
                        .EnumerateFiles(_folderPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(file =>
                            _allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                            !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase) &&
                            !file.Contains("_monitor_", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (_coverPaths.Count == 0)
                    {
                        MessageBox.Show(
                            $"No image files found in the specified folder \"{_folderPath}\".\n\nPlease ensure it contains JPG, PNG, or BMP images.",
                            "No Images Found",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading cover paths: {ex.Message}");
                    retries++;
                    if (retries >= MaxLoadRetries)
                    {
                        Console.WriteLine("Unable to load covers after retries, skipping load.");
                    }
                }
            }
        }

        /// <summary>
        /// Initializes wallpaper images and grid layouts based on the current mode.
        /// </summary>
        private void InitializeWallpaperAndGrids()
        {
            try
            {
                DisposeWallpapers();
                _grids.Clear();
                _monitorGrids.Clear();

                if (_wallpaperMode == AWConfig.WallpaperMode.Single)
                {
                    InitializeSingleWallpaper();
                }
                else
                {
                    InitializePerMonitorWallpapers();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during initialization: {ex.Message}");
                FallbackInitialize();
            }
        }

        /// <summary>
        /// Sets up a single wallpaper image and grid layout.
        /// </summary>
        private void InitializeSingleWallpaper()
        {
            _wallpaper = new Image<Rgba32>(_width, _height);
            _grids = CreateGrids(_width, _height, _rows, _cols);
        }

        /// <summary>
        /// Sets up wallpaper images and grids for each monitor.
        /// </summary>
        private void InitializePerMonitorWallpapers()
        {
            foreach (var display in _displays)
            {
                int rows = _rows, cols = _cols;
                var monCfg = _currentConfig?.MonitorConfigurations?
                    .FirstOrDefault(mc => mc.DisplayNumber == display.DisplayNumber);
                if (monCfg != null)
                {
                    Console.WriteLine($"Using monitor-specific config for display {display.DisplayNumber}: Rows={monCfg.Rows}, Columns={monCfg.Cols}");
                    rows = monCfg.Rows;
                    cols = monCfg.Cols;
                }

                int width = _adaptToDpiScaling ? display.Width : (int)(display.Width / display.DpiScaling);
                int height = _adaptToDpiScaling ? display.Height : (int)(display.Height / display.DpiScaling);
                Console.WriteLine($"Display {display.DisplayNumber} resolution: {width}x{height}, DPI scale: {display.DpiScaling:F2}");

                var wallpaper = new Image<Rgba32>(width, height);
                _monitorWallpapers[display.DeviceName] = wallpaper;
                _monitorGrids[display.DeviceName] = CreateGrids(width, height, rows, cols);
            }
        }

        /// <summary>
        /// Schedules the first wallpaper update immediately and further updates based on random intervals.
        /// </summary>
        private void ScheduleUpdate()
        {
            _timer = new Timer(async _ => await UpdateWallpaper(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Main update loop for applying new images to grids and saving the result as wallpaper.
        /// </summary>
        private async Task UpdateWallpaper()
        {
            bool lockAcquired = false;
            try
            {
                lockAcquired = await _updateLock.WaitAsync(TimeSpan.FromSeconds(3));
                if (!lockAcquired)
                {
                    Console.WriteLine("Could not acquire update lock, skipping this cycle");
                    return;
                }

                var now = DateTime.Now;
                try
                {
                    if (_wallpaperMode == AWConfig.WallpaperMode.Single)
                    {
                        await UpdateSingleWallpaper(now);
                    }
                    else
                    {
                        await UpdatePerMonitorWallpapers(now);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating wallpaper: {ex.Message}");
                }
            }
            finally
            {
                if (lockAcquired) _updateLock.Release();

                if (!_disposed)
                {
                    try
                    {
                        int nextSec = Random.Shared.Next(_minInterval, _maxInterval + 1);
                        _timer?.Change(TimeSpan.FromSeconds(nextSec), Timeout.InfiniteTimeSpan);
                    }
                    catch
                    {
                        _timer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        /// <summary>
        /// Processes grid updates and writes a single wallpaper image to disk and applies it.
        /// </summary>
        private async Task UpdateSingleWallpaper(DateTime now)
        {
            if (_wallpaper == null) return;

            var candidates = GetUpdateCandidates(_grids, now);
            var used       = _grids.Select(g => g.CurrentCoverPath).ToList();
            var available  = _coverPaths.Except(used).ToList();

            foreach (var grid in candidates)
            {
                if (available.Count == 0)
                {
                    Console.WriteLine("No more covers available.");
                    break;
                }
                var path = available[Random.Shared.Next(available.Count)];
                available.Remove(path);
                await grid.UpdateCoverAsync(path, _wallpaper);
                _lastUpdateTimes[grid] = now;
            }

            string outPath = Path.Combine(_destFolder, "wallpaper.jpg");
            await _wallpaper.SaveAsJpegAsync(outPath);
            ApplySingleWallpaper(outPath);
            _isFirstUpdate = false;
        }

        /// <summary>
        /// Processes grid updates and writes per-monitor wallpaper images, then applies them accordingly.
        /// </summary>
        private async Task UpdatePerMonitorWallpapers(DateTime now)
        {
            bool supportPerMonitor = DesktopWallpaperApi.IsPerMonitorWallpaperSupported();
            var wallpaperPaths = new Dictionary<string, string>();

            Console.WriteLine($"Starting per-monitor update for {_displays.Count} displays, per-monitor supported: {supportPerMonitor}");

            foreach (var display in _displays)
            {
                if (!_monitorGrids.TryGetValue(display.DeviceName, out var grids) ||
                    !_monitorWallpapers.TryGetValue(display.DeviceName, out var wallpaper))
                {
                    Console.WriteLine($"Skipping display {display.DisplayNumber}: no grids or wallpaper found");
                    continue;
                }

                Console.WriteLine($"Updating display {display.DisplayNumber}, grid count: {grids.Count}");
                var candidates = GetUpdateCandidates(grids, now);
                var used       = grids.Select(g => g.CurrentCoverPath).ToList();
                var available  = _coverPaths.Except(used).ToList();

                foreach (var grid in candidates)
                {
                    if (available.Count == 0)
                    {
                        Console.WriteLine($"No more covers for display {display.DisplayNumber}.");
                        break;
                    }
                    var path = available[Random.Shared.Next(available.Count)];
                    available.Remove(path);
                    await grid.UpdateCoverAsync(path, wallpaper);
                    _lastUpdateTimes[grid] = now;
                }

                string outPath = Path.Combine(_destFolder, $"wallpaper_monitor_{display.DisplayNumber}.jpg");
                await wallpaper.SaveAsJpegAsync(outPath);

                if (supportPerMonitor && _monitorToDevicePathMap.TryGetValue(display.DeviceName, out var devicePath))
                {
                    wallpaperPaths[devicePath] = outPath;
                }
                else if (display.IsPrimary)
                {
                    WallpaperSetter.Set(outPath);
                    break;
                }
            }

            if (supportPerMonitor && wallpaperPaths.Count > 0)
            {
                try
                {
                    DesktopWallpaperApi.SetWallpaperForAllMonitors(wallpaperPaths);
                    Console.WriteLine("Per-monitor wallpaper applied successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Batch apply failed: {ex.Message}, attempting individual application");
                    bool anySuccess = false;
                    foreach (var kv in wallpaperPaths)
                    {
                        try
                        {
                            DesktopWallpaperApi.SetWallpaperForAllMonitors(new Dictionary<string, string> { { kv.Key, kv.Value } });
                            anySuccess = true;
                        }
                        catch (Exception iex)
                        {
                            Console.WriteLine($"Failed to set for {kv.Key}: {iex.Message}");
                        }
                    }
                    if (!anySuccess)
                    {
                        var primary = _displays.FirstOrDefault(d => d.IsPrimary);
                        if (primary != null
                            && _monitorToDevicePathMap.TryGetValue(primary.DeviceName, out var pd)
                            && wallpaperPaths.TryGetValue(pd, out var ppath))
                        {
                            WallpaperSetter.Set(ppath);
                            Console.WriteLine("Fallback to primary monitor wallpaper");
                        }
                    }
                }
            }
            else if (wallpaperPaths.Count == 0)
            {
                Console.WriteLine("Warning: no wallpaper paths generated");
            }

            _isFirstUpdate = false;
        }

        /// <summary>
        /// Disposes existing wallpaper images to free resources.
        /// </summary>
        private void DisposeWallpapers()
        {
            try
            {
                if (_wallpaper != null)
                {
                    var temp = _wallpaper;
                    _wallpaper = null;
                    temp.Dispose();
                }

                var copy = new Dictionary<string, Image<Rgba32>>(_monitorWallpapers);
                _monitorWallpapers.Clear();
                foreach (var wp in copy.Values)
                {
                    try { wp.Dispose(); }
                    catch (Exception ex) { Console.WriteLine($"Error disposing wallpaper: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error releasing resources: {ex.Message}");
            }
        }

        /// <summary>
        /// Releases all resources and unregisters event handlers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _displaySettingsChangeCts?.Cancel();
            _displaySettingsChangeCts?.Dispose();
            _displaySettingsChangeCts = null;

            _timer?.Dispose();
            _timer = null;

            DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            Task.Run(() =>
            {
                DisposeWallpapers();

                foreach (var g in _grids.ToList())
                {
                    try { g.CurrentCover?.Dispose(); }
                    catch (Exception ex) { Console.WriteLine($"Error disposing grid resources: {ex.Message}"); }
                }
                _grids.Clear();

                foreach (var list in _monitorGrids.Values)
                {
                    foreach (var g in list.ToList())
                    {
                        try { g.CurrentCover?.Dispose(); }
                        catch (Exception ex) { Console.WriteLine($"Error disposing monitor grid resources: {ex.Message}"); }
                    }
                }
                _monitorGrids.Clear();

                _lastUpdateTimes.Clear();
                _monitorToDevicePathMap.Clear();
            });
        }

        // ==== Private helper methods ====

        /// <summary>
        /// Creates grid definitions for the wallpaper by dividing the specified
        /// width and height into the given number of rows and columns.
        /// Accounts for leftover pixels by distributing gaps evenly between grids.
        /// </summary>
        /// <param name="width">Total width of the wallpaper or monitor.</param>
        /// <param name="height">Total height of the wallpaper or monitor.</param>
        /// <param name="rows">Number of rows in the grid layout.</param>
        /// <param name="cols">Number of columns in the grid layout.</param>
        /// <returns>List of Grid objects defining position and size for each cell.</returns>
        private List<Grid> CreateGrids(int width, int height, int rows, int cols)
        {
            var grids = new List<Grid>();
            // Calculate base cell size and total coverage
            int baseSize = Math.Min(width / cols, height / rows);
            int totalW = baseSize * cols;
            int totalH = baseSize * rows;
            int remW = width - totalW;
            int remH = height - totalH;
            // If there is negative remainder, reduce base size
            if (remW < 0 || remH < 0)
            {
                baseSize = Math.Max(1, baseSize - 1);
                totalW = baseSize * cols;
                totalH = baseSize * rows;
                remW = width - totalW;
                remH = height - totalH;
            }
            // Compute gaps between cells
            int gapW = cols > 1 ? remW / (cols - 1) : 0;
            int gapH = rows > 1 ? remH / (rows - 1) : 0;

            // Create grid cells row by row
            for (int i = 0; i < rows * cols; i++)
            {
                int c = i % cols, r = i / cols;
                float x = c * (baseSize + gapW);
                float y = r * (baseSize + gapH);
                grids.Add(new Grid(new PointF(x, y), new SizeF(baseSize, baseSize), _imageManager));
            }
            return grids;
        }

        /// <summary>
        /// Provides a default single display configuration based on the initial
        /// width and height when no actual display information is available.
        /// </summary>
        /// <returns>List containing a single default DisplayInfo object.</returns>
        private List<DisplayInfo> GetDefaultDisplays()
        {
            return new List<DisplayInfo>
            {
                new DisplayInfo
                {
                    DisplayNumber = 0,
                    Bounds = new System.Drawing.Rectangle(0, 0, _width, _height),
                    IsPrimary = true,
                    Orientation = DisplayInfo.OrientationType.Landscape,
                    DpiScaling = 1.0f
                }
            };
        }

        /// <summary>
        /// Selects which grid cells are due for an image update based on the last
        /// update timestamp and a fixed interval. Randomly chooses a subset.
        /// </summary>
        /// <param name="grids">List of all grid cells.</param>
        /// <param name="now">Current timestamp for comparison.</param>
        /// <returns>Subset of grids to update in this cycle.</returns>
        private List<Grid> GetUpdateCandidates(List<Grid> grids, DateTime now)
        {
            if (_isFirstUpdate)
                return new List<Grid>(grids);

            // Find grids that are overdue for update
            var due = grids.Where(g =>
                        !_lastUpdateTimes.ContainsKey(g) ||
                        (now - _lastUpdateTimes[g]).TotalSeconds >= GridUpdateIntervalSeconds
                      ).ToList();
            // Determine how many to update (at most a quarter of total + 1)
            int maxCnt = Math.Min(due.Count, grids.Count / 4) + 1;
            int cnt = due.Count <= 3 ? due.Count : Random.Shared.Next(3, maxCnt);
            // Randomly select the candidates
            return due.OrderBy(_ => Guid.NewGuid()).Take(cnt).ToList();
        }

        /// <summary>
        /// Applies a single-image wallpaper using the API, falling back to a setter utility on failure.
        /// </summary>
        /// <param name="path">Path to the generated wallpaper image file.</param>
        private void ApplySingleWallpaper(string path)
        {
            try { DesktopWallpaperApi.SetSingleWallpaper(path); }
            catch { WallpaperSetter.Set(path); }
        }

        /// <summary>
        /// Fallback initialization logic when normal setup fails.
        /// Creates a default wallpaper and grid layout based on either single mode
        /// or the primary display configuration.
        /// </summary>
        private void FallbackInitialize()
        {
            if (_wallpaperMode == AWConfig.WallpaperMode.Single)
            {
                // Ensure minimum resolution and create grids
                _wallpaper = new Image<Rgba32>(Math.Max(1920, _width), Math.Max(1080, _height));
                if (_grids.Count == 0)
                {
                    _grids = CreateGrids(_wallpaper.Width, _wallpaper.Height, _rows, _cols);
                }
            }
            else
            {
                // Use primary display or first available for fallback
                var primary = _displays.FirstOrDefault(d => d.IsPrimary) ?? _displays.FirstOrDefault();
                if (primary != null)
                {
                    int w = primary.Bounds.Width;
                    int h = primary.Bounds.Height;
                    var wp = new Image<Rgba32>(w, h);
                    _monitorWallpapers[primary.DeviceName] = wp;
                    if (!_monitorGrids.ContainsKey(primary.DeviceName))
                    {
                        _monitorGrids[primary.DeviceName] = CreateGrids(w, h, _rows, _cols);
                    }
                }
            }
        }
    }
}
