//  WallpaperUpdater.cs
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

namespace ArtfulWall.Services
{
    public class WallpaperUpdater : IDisposable
    {
        private const int MaxLoadRetries = 3;
        private const double GridUpdateIntervalSeconds = 10;

        private readonly HashSet<string> _allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };

        private int _minInterval;
        private int _maxInterval;
        private string _folderPath;
        private string _destFolder;
        private int _width;
        private int _height;
        private int _rows;
        private int _cols;
        private ArtfulWall.Models.Configuration.WallpaperMode _wallpaperMode;
        private bool _autoAdjustToDisplayChanges;
        private bool _adaptToDpiScaling;

        private ImageManager _imageManager;
        private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private List<string> _coverPaths;
        private List<Grid> _grids; // 用于单一壁纸模式
        private Image<Rgba32>? _wallpaper; // 用于单一壁纸模式
        private Timer? _timer;
        private readonly ConcurrentDictionary<Grid, DateTime> _lastUpdateTimes;
        private bool _isFirstUpdate = true;
        private bool _disposed = false;

        // 多显示器支持字段
        private Dictionary<string, List<Grid>> _monitorGrids; // 每个显示器的网格
        private Dictionary<string, Image<Rgba32>> _monitorWallpapers; // 每个显示器的壁纸
        private List<DisplayInfo> _displays; // 所有检测到的显示器
        private Dictionary<string, string> _monitorToDevicePathMap; // 显示器编号到设备路径的映射

        // 添加私有字段用于存储当前配置
        private ArtfulWall.Models.Configuration? _currentConfig;

        // 添加用于取消操作的CancellationTokenSource
        private CancellationTokenSource? _displaySettingsChangeCts;

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
            ArtfulWall.Models.Configuration? config = null
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
            _wallpaperMode = ArtfulWall.Models.Configuration.WallpaperMode.PerMonitor; // 默认为每显示器模式
            _autoAdjustToDisplayChanges = true;
            _adaptToDpiScaling = true;
            _currentConfig = config; // 存储完整配置对象

            _lastUpdateTimes = new ConcurrentDictionary<Grid, DateTime>();
            _coverPaths      = new List<string>();
            _grids           = new List<Grid>();
            _monitorGrids    = new Dictionary<string, List<Grid>>();
            _monitorWallpapers = new Dictionary<string, Image<Rgba32>>();
            _displays        = new List<DisplayInfo>();
            _monitorToDevicePathMap = new Dictionary<string, string>();
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_folderPath) || !Directory.Exists(_folderPath))
            {
                Console.WriteLine("文件夹路径无效或不存在。");
                return;
            }

            LoadAlbumCovers();
            DetectDisplays();
            InitializeWallpaperAndGrids();
            
            // 注册显示设置变更事件
            if (_autoAdjustToDisplayChanges)
            {
                DisplayManager.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            
            ScheduleUpdate();
        }

        public void UpdateConfig(ArtfulWall.Models.Configuration cfg)
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
            
            // 在更新前备份当前配置中的监视器配置，确保它们不会丢失
            var oldConfig = _currentConfig;
            _currentConfig = cfg.Clone(); // 使用克隆而不是直接引用，避免外部对象修改
            
            // 输出每个显示器的特定配置信息，便于调试
            if (_currentConfig.MonitorConfigurations.Count > 0)
            {
                Console.WriteLine("显示器特定配置:");
                foreach (var monConfig in _currentConfig.MonitorConfigurations)
                {
                    Console.WriteLine($"  显示器 {monConfig.DisplayNumber}: {monConfig.Width}x{monConfig.Height}, " +
                                     $"DPI: {monConfig.DpiScaling:F2}, " +
                                     $"行: {monConfig.Rows}, 列: {monConfig.Cols}");
                }
            }
            
            Console.WriteLine($"配置更新 - 已设置适配DPI: {_adaptToDpiScaling}, 壁纸模式: {_wallpaperMode}");

            LoadAlbumCovers();
            
            // 如果自动调整选项更改，更新事件订阅
            if (_autoAdjustToDisplayChanges)
            {
                DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged; // 防止重复订阅
                DisplayManager.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }
            else
            {
                DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            }
            
            DetectDisplays();

            // 打印检测到的显示信息
            foreach (var display in _displays)
            {
                Console.WriteLine($"检测到显示器 {display.DisplayNumber}: 分辨率={display.Width}x{display.Height}, " +
                                 $"DPI缩放={display.DpiScaling:F2}");
            }

            InitializeWallpaperAndGrids();
            _lastUpdateTimes.Clear();
            _isFirstUpdate = true;
            _timer?.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        // 检测所有显示器
        private void DetectDisplays()
        {
            try
            {
                // 使用超时处理，防止获取显示器信息时阻塞太长时间
                var task = Task.Run(() => DisplayManager.GetDisplays());
                if (task.Wait(3000)) // 3秒超时
                {
                    _displays = task.Result;
                }
                else
                {
                    Console.WriteLine("获取显示器信息超时，使用上次检测到的显示器信息");
                    // 如果超时且没有先前的显示器信息，则创建默认显示器
                    if (_displays == null || _displays.Count == 0)
                    {
                        _displays = new List<DisplayInfo> {
                            new DisplayInfo {
                                DisplayNumber = 0,
                                Bounds = new System.Drawing.Rectangle(0, 0, _width, _height),
                                IsPrimary = true,
                                Orientation = DisplayInfo.OrientationType.Landscape,
                                DpiScaling = 1.0f
                            }
                        };
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测显示器时出错: {ex.Message}");
                // 出错时使用上次检测到的显示器信息或创建默认显示器
                if (_displays == null || _displays.Count == 0)
                {
                    _displays = new List<DisplayInfo> {
                        new DisplayInfo {
                            DisplayNumber = 0,
                            Bounds = new System.Drawing.Rectangle(0, 0, _width, _height),
                            IsPrimary = true,
                            Orientation = DisplayInfo.OrientationType.Landscape,
                            DpiScaling = 1.0f
                        }
                    };
                }
                return;
            }
            
            if (_displays.Count == 0)
            {
                // 如果没有检测到显示器，添加一个默认显示器
                _displays.Add(new DisplayInfo
                {
                    DisplayNumber = 0,
                    Bounds = new System.Drawing.Rectangle(0, 0, _width, _height),
                    IsPrimary = true,
                    Orientation = DisplayInfo.OrientationType.Landscape,
                    DpiScaling = 1.0f
                });
            }
            
            // 更新配置中的显示器信息
            UpdateDisplayConfigInfo();
            
            // 获取显示器ID到设备路径的映射
            UpdateMonitorToDevicePathMap();
        }

        // 更新配置中的显示器信息（从DetectDisplays中提取）
        private void UpdateDisplayConfigInfo()
        {
            if (_currentConfig?.MonitorConfigurations == null)
                return;
                
            // 更新显示器配置中的实际分辨率和设备名称
            foreach (var display in _displays)
            {
                var monitorConfig = _currentConfig.MonitorConfigurations
                    .FirstOrDefault(mc => mc.DisplayNumber == display.DisplayNumber);
                
                if (monitorConfig != null)
                {
                    // 更新检测到的信息
                    monitorConfig.Width = display.Width;
                    monitorConfig.Height = display.Height;
                    monitorConfig.DpiScaling = display.DpiScaling;
                    monitorConfig.MonitorId = display.DeviceName;
                    monitorConfig.IsPortrait = display.Orientation == DisplayInfo.OrientationType.Portrait || 
                                              display.Orientation == DisplayInfo.OrientationType.PortraitFlipped;
                    
                    Console.WriteLine($"更新显示器 {display.DisplayNumber} 配置信息: " +
                                     $"分辨率={display.Width}x{display.Height}, " +
                                     $"行={monitorConfig.Rows}, 列={monitorConfig.Cols}");
                }
            }
        }

        // 更新显示器ID到设备路径的映射（从DetectDisplays中提取）
        private void UpdateMonitorToDevicePathMap()
        {
            _monitorToDevicePathMap.Clear();
            
            if (!DesktopWallpaperApi.IsPerMonitorWallpaperSupported())
                return;
                
            try
            {
                // 方式1: 使用简化的映射匹配
                var wpInstance = (DesktopWallpaperApi.IDesktopWallpaper)new DesktopWallpaperApi.DesktopWallpaper();
                uint monitorCount = wpInstance.GetMonitorDevicePathCount();
                
                if (monitorCount == 0)
                    return;
                    
                // 首先从Windows API获取显示器信息
                var monitorInfo = DesktopWallpaperApi.GetAllMonitorInfo();
                
                // 用于存储已映射的显示器
                var mappedDisplays = new HashSet<string>();
                
                // 第一次尝试: 使用矩形位置和大小匹配
                foreach (var display in _displays)
                {
                    foreach (var monitor in monitorInfo)
                    {
                        var rect = monitor.Value;
                        // 比较位置和大小
                        if (rect.Left == display.Bounds.Left && 
                            rect.Top == display.Bounds.Top &&
                            rect.Right == display.Bounds.Right &&
                            rect.Bottom == display.Bounds.Bottom)
                        {
                            _monitorToDevicePathMap[display.DeviceName] = monitor.Key;
                            mappedDisplays.Add(display.DeviceName);
                            Console.WriteLine($"矩形匹配: 显示器 {display.DisplayNumber} ({display.DeviceName}) -> 设备路径 {monitor.Key}");
                            break;
                        }
                    }
                }
                
                // 第二次尝试: 对未匹配的显示器使用序号匹配
                for (int i = 0; i < Math.Min(_displays.Count, (int)monitorCount); i++)
                {
                    if (mappedDisplays.Contains(_displays[i].DeviceName))
                        continue;
                        
                    try
                    {
                        string devicePath = wpInstance.GetMonitorDevicePathAt((uint)i);
                        if (!string.IsNullOrEmpty(devicePath))
                        {
                            _monitorToDevicePathMap[_displays[i].DeviceName] = devicePath;
                            Console.WriteLine($"序号匹配: 显示器 {i} ({_displays[i].DeviceName}) -> 设备路径 {devicePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"序号映射时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"映射显示器设备路径时出错: {ex.Message}");
            }
            
            // 显示匹配结果
            Console.WriteLine($"成功映射 {_monitorToDevicePathMap.Count} 个显示器");
        }

        // 在显示设置变更时处理
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            // 取消上一个正在进行的操作(如果有)
            _displaySettingsChangeCts?.Cancel();
            _displaySettingsChangeCts = new CancellationTokenSource();
            var token = _displaySettingsChangeCts.Token;
            
            Task.Run(async () =>
            {
                try
                {
                    // 添加超时机制
                    bool lockAcquired = await _updateLock.WaitAsync(TimeSpan.FromSeconds(5), token);
                    if (!lockAcquired)
                    {
                        Console.WriteLine("无法获取更新锁，放弃处理显示设置变更");
                        return;
                    }
                    
                    try
                    {
                        if (token.IsCancellationRequested) return;
                        
                        Console.WriteLine("检测到显示设置变更，正在重新配置壁纸...");
                        
                        DetectDisplays();
                        if (token.IsCancellationRequested) return;
                        
                        InitializeWallpaperAndGrids();
                        if (token.IsCancellationRequested) return;
                        
                        _lastUpdateTimes.Clear();
                        _isFirstUpdate = true;
                        
                        // 立即触发壁纸更新
                        await UpdateWallpaper();
                    }
                    finally
                    {
                        _updateLock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("显示设置变更处理被取消");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理显示设置变更时出错: {ex.Message}");
                }
            }, token);
        }

        // 检查指定文件夹是否包含图片文件的辅助方法
        private bool CheckForImageFiles(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return false;
                
            try
            {
                return Directory
                    .EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(file => 
                        _allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                        !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

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
                            !file.Contains("_monitor_", StringComparison.OrdinalIgnoreCase)) // 排除已生成的显示器壁纸
                        .ToList();
                    
                    // 检查是否没有找到图片文件，并通知用户
                    if (_coverPaths.Count == 0)
                    {
                        MessageBox.Show(
                            $"在指定的文件夹 \"{_folderPath}\" 中未找到任何图片文件。\n\n请确保该文件夹包含JPG、PNG或BMP格式的图片。",
                            "未找到图片",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载封面路径时发生错误：{ex.Message}");
                    retries++;
                    if (retries >= MaxLoadRetries)
                    {
                        Console.WriteLine("无法加载封面图片，将跳过加载。");
                    }
                }
            }
        }

        private void InitializeWallpaperAndGrids()
        {
            try
            {
                // 清理旧资源
                DisposeWallpapers();
                _grids.Clear();
                _monitorGrids.Clear();
                
                if (_wallpaperMode == ArtfulWall.Models.Configuration.WallpaperMode.Single)
                {
                    // 单一壁纸模式
                    InitializeSingleWallpaper();
                }
                else
                {
                    // 每显示器壁纸模式
                    InitializePerMonitorWallpapers();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化壁纸和网格时出错: {ex.Message}");
                
                // 确保至少有最小化功能的应急初始化
                try 
                {
                    if (_wallpaperMode == ArtfulWall.Models.Configuration.WallpaperMode.Single && _wallpaper == null)
                    {
                        _wallpaper = new Image<Rgba32>(Math.Max(1920, _width), Math.Max(1080, _height));
                        if (_grids.Count == 0)
                        {
                            _grids.Add(new Grid(
                                new PointF(0, 0),
                                new SizeF(Math.Max(1920, _width), Math.Max(1080, _height)),
                                _imageManager));
                        }
                    }
                    else if (_monitorWallpapers.Count == 0)
                    {
                        // 应急创建至少一个显示器壁纸
                        var display = _displays.FirstOrDefault(d => d.IsPrimary) ?? _displays.FirstOrDefault();
                        if (display != null)
                        {
                            var wallpaper = new Image<Rgba32>(display.Width, display.Height);
                            _monitorWallpapers[display.DeviceName] = wallpaper;
                            
                            if (!_monitorGrids.ContainsKey(display.DeviceName))
                            {
                                var grid = new Grid(
                                    new PointF(0, 0),
                                    new SizeF(display.Width, display.Height),
                                    _imageManager);
                                _monitorGrids[display.DeviceName] = new List<Grid> { grid };
                            }
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"应急初始化也失败: {fallbackEx.Message}");
                }
            }
        }

        // 初始化单一壁纸模式
        private void InitializeSingleWallpaper()
        {
            int baseGridSize = Math.Min(_width / _cols, _height / _rows);
            int totalGridW   = baseGridSize * _cols;
            int totalGridH   = baseGridSize * _rows;
            int remW         = _width  - totalGridW;
            int remH         = _height - totalGridH;
            
            if (remW < 0 || remH < 0)
            {
                baseGridSize = Math.Max(1, baseGridSize - 1);
                totalGridW   = baseGridSize * _cols;
                totalGridH   = baseGridSize * _rows;
                remW         = _width  - totalGridW;
                remH         = _height - totalGridH;
            }
            
            int gapW = _cols > 1 ? remW / (_cols - 1) : 0;
            int gapH = _rows > 1 ? remH / (_rows - 1) : 0;

            _wallpaper = new Image<Rgba32>(_width, _height);
            for (int i = 0; i < _rows * _cols; i++)
            {
                int col = i % _cols, row = i / _cols;
                float x = col * (baseGridSize + gapW);
                float y = row * (baseGridSize + gapH);
                var grid = new Grid(
                    new PointF(x, y),
                    new SizeF(baseGridSize, baseGridSize),
                    _imageManager);
                _grids.Add(grid);
            }
        }

        // 初始化每显示器壁纸模式
        private void InitializePerMonitorWallpapers()
        {
            foreach (var display in _displays)
            {
                int width, height;
                int rows = _rows; // 默认行数
                int cols = _cols; // 默认列数
                
                // 查找该显示器的特定配置
                var monitorConfig = _currentConfig?.MonitorConfigurations?
                    .FirstOrDefault(mc => mc.DisplayNumber == display.DisplayNumber);
                    
                if (monitorConfig != null)
                {
                    // 使用显示器特定配置
                    Console.WriteLine($"使用显示器 {display.DisplayNumber} 特定配置: 行数={monitorConfig.Rows}, 列数={monitorConfig.Cols}");
                    rows = monitorConfig.Rows;
                    cols = monitorConfig.Cols;
                }
                
                // 使用物理分辨率
                width = display.Width;
                height = display.Height;
                
                if (_adaptToDpiScaling)
                {
                    Console.WriteLine($"显示器 {display.DisplayNumber} 使用分辨率: {width}x{height}, DPI缩放: {display.DpiScaling}");
                }
                else
                {
                    // DPI缩放调整（只在不适应DPI缩放时使用）
                    width = (int)(width / display.DpiScaling);
                    height = (int)(height / display.DpiScaling);
                    Console.WriteLine($"显示器 {display.DisplayNumber} 使用缩放调整后的分辨率: {width}x{height}");
                }
                
                // 处理方向
                bool isPortrait = display.Orientation == DisplayInfo.OrientationType.Portrait ||
                                display.Orientation == DisplayInfo.OrientationType.PortraitFlipped;
                
                // 如果是纵向显示器，需要调整网格排列
                if (isPortrait)
                {
                    // 交换宽高
                    if (width < height)
                    {
                        // 如果没有特定配置并且是纵向显示器，可能需要调整行列数
                        if (monitorConfig == null)
                        {
                            // 在纵向显示器上，可以调整行列数以适应形状
                            int temp = rows;
                            rows = cols;
                            cols = temp;
                        }
                    }
                }
                
                // 创建此显示器的壁纸
                var wallpaper = new Image<Rgba32>(width, height);
                _monitorWallpapers[display.DeviceName] = wallpaper;
                
                // 为此显示器计算网格尺寸
                int baseGridSize = Math.Min(width / cols, height / rows);
                int totalGridW = baseGridSize * cols;
                int totalGridH = baseGridSize * rows;
                int remW = width - totalGridW;
                int remH = height - totalGridH;
                
                if (remW < 0 || remH < 0)
                {
                    baseGridSize = Math.Max(1, baseGridSize - 1);
                    totalGridW = baseGridSize * cols;
                    totalGridH = baseGridSize * rows;
                    remW = width - totalGridW;
                    remH = height - totalGridH;
                }
                
                int gapW = cols > 1 ? remW / (cols - 1) : 0;
                int gapH = rows > 1 ? remH / (rows - 1) : 0;
                
                // 创建此显示器的网格
                var displayGrids = new List<Grid>();
                
                for (int i = 0; i < rows * cols; i++)
                {
                    int col = i % cols, row = i / cols;
                    float x = col * (baseGridSize + gapW);
                    float y = row * (baseGridSize + gapH);
                    
                    var grid = new Grid(
                        new PointF(x, y),
                        new SizeF(baseGridSize, baseGridSize),
                        _imageManager);
                    
                    displayGrids.Add(grid);
                }
                
                _monitorGrids[display.DeviceName] = displayGrids;
            }
        }

        private void ScheduleUpdate()
        {
            _timer = new Timer(async _ => await UpdateWallpaper(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        private async Task UpdateWallpaper()
        {
            // 添加超时机制
            bool lockAcquired = false;
            try
            {
                // 尝试获取锁，最多等待3秒
                lockAcquired = await _updateLock.WaitAsync(TimeSpan.FromSeconds(3));
                if (!lockAcquired)
                {
                    Console.WriteLine("无法获取更新锁，放弃本次壁纸更新");
                    return;
                }
                
                var now = DateTime.Now;
                
                try
                {
                    if (_wallpaperMode == ArtfulWall.Models.Configuration.WallpaperMode.Single)
                    {
                        // 单一壁纸模式
                        await UpdateSingleWallpaper(now);
                    }
                    else
                    {
                        // 每显示器壁纸模式
                        await UpdatePerMonitorWallpapers(now);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"更新壁纸时发生错误: {ex.Message}");
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    _updateLock.Release();
                }
                
                if (!_disposed)
                {
                    // 安排下一次更新，使用较短的延迟以防错过更新
                    try
                    {
                        int nextSec = Random.Shared.Next(_minInterval, _maxInterval + 1);
                        _timer?.Change(TimeSpan.FromSeconds(nextSec), Timeout.InfiniteTimeSpan);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"安排下一次壁纸更新时出错: {ex.Message}");
                        // 尝试使用安全的默认时间重新安排
                        _timer?.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        // 更新单一壁纸
        private async Task UpdateSingleWallpaper(DateTime now)
        {
            List<Grid> candidates;

            if (_isFirstUpdate)
            {
                candidates = _grids.ToList();
                _isFirstUpdate = false;
            }
            else
            {
                candidates = _grids
                    .Where(g => !_lastUpdateTimes.ContainsKey(g) ||
                                (now - _lastUpdateTimes[g]).TotalSeconds >= GridUpdateIntervalSeconds)
                    .ToList();
                int maxCount = Math.Min(candidates.Count, _grids.Count / 4) + 1;
                int count = candidates.Count <= 3 ? candidates.Count : Random.Shared.Next(3, maxCount);
                candidates = candidates.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
            }

            var used = _grids.Select(g => g.CurrentCoverPath).ToList();
            var available = _coverPaths.Except(used).ToList();

            foreach (var grid in candidates)
            {
                if (available.Count == 0)
                {
                    Console.WriteLine("无更多封面可用。");
                    break;
                }
                var path = available[Random.Shared.Next(available.Count)];
                available.Remove(path);
                await grid.UpdateCoverAsync(path, _wallpaper!);
                _lastUpdateTimes[grid] = now;
            }

            string outPath = Path.Combine(_destFolder, "wallpaper.jpg");
            await _wallpaper!.SaveAsJpegAsync(outPath);
            
            // 使用高级API设置单一壁纸
            try
            {
                DesktopWallpaperApi.SetSingleWallpaper(outPath);
            }
            catch
            {
                // 回退到传统API
                WallpaperSetter.Set(outPath);
            }
        }

        // 更新每显示器壁纸
        private async Task UpdatePerMonitorWallpapers(DateTime now)
        {
            Dictionary<string, string> wallpaperPaths = new Dictionary<string, string>();
            bool supportPerMonitor = DesktopWallpaperApi.IsPerMonitorWallpaperSupported();
            
            Console.WriteLine($"开始更新每显示器壁纸，共 {_displays.Count} 个显示器，是否支持每显示器壁纸: {supportPerMonitor}");
            
            // 显示每个显示器及其配置
            if (_currentConfig?.MonitorConfigurations != null)
            {
                foreach (var monConfig in _currentConfig.MonitorConfigurations)
                {
                    var display = _displays.FirstOrDefault(d => d.DisplayNumber == monConfig.DisplayNumber);
                    if (display != null)
                    {
                        Console.WriteLine($"显示器 {monConfig.DisplayNumber} 配置: " +
                                         $"物理分辨率: {monConfig.Width}x{monConfig.Height}, " +
                                         $"行: {monConfig.Rows}, 列: {monConfig.Cols}");
                    }
                }
            }
            
            foreach (var display in _displays)
            {
                if (!_monitorGrids.TryGetValue(display.DeviceName, out var displayGrids) ||
                    !_monitorWallpapers.TryGetValue(display.DeviceName, out var wallpaper))
                {
                    Console.WriteLine($"跳过显示器 {display.DisplayNumber}: 未找到对应的网格或壁纸");
                    continue;
                }
                
                Console.WriteLine($"正在更新显示器 {display.DisplayNumber} 壁纸 " +
                                 $"({wallpaper.Width}x{wallpaper.Height}, " +
                                 $"网格数: {displayGrids.Count})...");
                
                List<Grid> candidates;
                
                if (_isFirstUpdate)
                {
                    candidates = displayGrids.ToList();
                }
                else
                {
                    candidates = displayGrids
                        .Where(g => !_lastUpdateTimes.ContainsKey(g) ||
                                    (now - _lastUpdateTimes[g]).TotalSeconds >= GridUpdateIntervalSeconds)
                        .ToList();
                    int maxCount = Math.Min(candidates.Count, displayGrids.Count / 4) + 1;
                    int count = candidates.Count <= 3 ? candidates.Count : Random.Shared.Next(3, maxCount);
                    candidates = candidates.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
                }
                
                var used = displayGrids.Select(g => g.CurrentCoverPath).ToList();
                var available = _coverPaths.Except(used).ToList();
                
                foreach (var grid in candidates)
                {
                    if (available.Count == 0)
                    {
                        Console.WriteLine($"显示器 {display.DisplayNumber} 无更多封面可用。");
                        break;
                    }
                    var path = available[Random.Shared.Next(available.Count)];
                    available.Remove(path);
                    await grid.UpdateCoverAsync(path, wallpaper);
                    _lastUpdateTimes[grid] = now;
                }
                
                string outPath = Path.Combine(_destFolder, $"wallpaper_monitor_{display.DisplayNumber}.jpg");
                await wallpaper.SaveAsJpegAsync(outPath);
                
                // 存储显示器ID和壁纸路径的映射
                if (supportPerMonitor && _monitorToDevicePathMap.TryGetValue(display.DeviceName, out string devicePath))
                {
                    wallpaperPaths[devicePath] = outPath;
                }
                else if (display.IsPrimary)
                {
                    // 对于不支持每显示器壁纸的系统，只设置主显示器的壁纸
                    WallpaperSetter.Set(outPath);
                    break; // 只处理主显示器
                }
            }
            
            // 设置每显示器壁纸
            if (supportPerMonitor && wallpaperPaths.Count > 0)
            {
                Console.WriteLine($"正在设置 {wallpaperPaths.Count} 个显示器的壁纸:");
                foreach (var path in wallpaperPaths)
                {
                    Console.WriteLine($"  设备路径: {path.Key} -> 图片: {Path.GetFileName(path.Value)}");
                }
                
                try
                {
                    DesktopWallpaperApi.SetWallpaperForAllMonitors(wallpaperPaths);
                    Console.WriteLine("每显示器壁纸设置成功");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"设置每显示器壁纸时出错: {ex.Message}");
                    
                    // 逐个尝试设置每个显示器壁纸
                    Console.WriteLine("尝试逐个设置每个显示器壁纸...");
                    bool anySuccess = false;
                    
                    foreach (var display in _displays)
                    {
                        try
                        {
                            if (_monitorToDevicePathMap.TryGetValue(display.DeviceName, out string devicePath) &&
                                wallpaperPaths.TryGetValue(devicePath, out string wallpaperPath))
                            {
                                var singleMonitorPath = new Dictionary<string, string> { { devicePath, wallpaperPath } };
                                DesktopWallpaperApi.SetWallpaperForAllMonitors(singleMonitorPath);
                                Console.WriteLine($"显示器 {display.DisplayNumber} 壁纸单独设置成功");
                                anySuccess = true;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"单独设置显示器 {display.DisplayNumber} 壁纸失败: {innerEx.Message}");
                        }
                    }
                    
                    // 如果单独设置也失败，回退到设置主显示器壁纸
                    if (!anySuccess)
                    {
                        Console.WriteLine("所有方法失败，回退到设置主显示器壁纸");
                        var primaryDisplay = _displays.FirstOrDefault(d => d.IsPrimary);
                        if (primaryDisplay != null && 
                            _monitorToDevicePathMap.TryGetValue(primaryDisplay.DeviceName, out string primaryPath) &&
                            wallpaperPaths.TryGetValue(primaryPath, out string wallpaperPath))
                        {
                            try
                            {
                                WallpaperSetter.Set(wallpaperPath);
                                Console.WriteLine("已设置主显示器壁纸");
                            }
                            catch (Exception fallbackEx)
                            {
                                Console.WriteLine($"设置主显示器壁纸也失败: {fallbackEx.Message}");
                            }
                        }
                    }
                }
            }
            else if (wallpaperPaths.Count == 0)
            {
                Console.WriteLine("警告: 没有生成任何显示器的壁纸路径，壁纸未设置");
            }
            
            _isFirstUpdate = false;
        }

        // 释放壁纸资源
        private void DisposeWallpapers()
        {
            try
            {
                if (_wallpaper != null)
                {
                    var tempWallpaper = _wallpaper;
                    _wallpaper = null; // 先置空引用，防止其他线程访问
                    tempWallpaper.Dispose();
                }
                
                // 使用安全的方式释放每个显示器壁纸
                var wallpaperCopy = new Dictionary<string, Image<Rgba32>>(_monitorWallpapers);
                _monitorWallpapers.Clear(); // 清空集合，防止其他线程访问
                
                foreach (var wallpaper in wallpaperCopy.Values)
                {
                    try
                    {
                        wallpaper.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"释放显示器壁纸时出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"释放壁纸资源时出错: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                // 取消所有正在进行的操作
                _displaySettingsChangeCts?.Cancel();
                _displaySettingsChangeCts?.Dispose();
                _displaySettingsChangeCts = null;
                
                // 停止定时器
                _timer?.Dispose();
                _timer = null;
                
                // 取消事件订阅
                DisplayManager.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                
                // 使用线程安全的方式释放壁纸资源
                Task.Run(() =>
                {
                    try
                    {
                        // 在后台线程中安全释放资源，避免阻塞UI线程
                        DisposeWallpapers();
                        
                        // 清理所有网格资源
                        foreach (var grid in _grids.ToList())
                        {
                            try
                            {
                                grid.CurrentCover?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"释放网格资源时出错: {ex.Message}");
                            }
                        }
                        _grids.Clear();
                        
                        foreach (var displayGrids in _monitorGrids.Values)
                        {
                            foreach (var grid in displayGrids.ToList())
                            {
                                try
                                {
                                    grid.CurrentCover?.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"释放显示器网格资源时出错: {ex.Message}");
                                }
                            }
                        }
                        _monitorGrids.Clear();
                        
                        // 清理其他集合
                        _lastUpdateTimes.Clear();
                        _monitorToDevicePathMap.Clear();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"清理资源时出错: {ex.Message}");
                    }
                });
            }
        }

        // 添加新方法：为特定显示器设置指定壁纸
        public async Task SetImageForMonitor(string imagePath, int displayNumber)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                throw new FileNotFoundException("找不到指定的图片文件", imagePath);
            }
            
            await _updateLock.WaitAsync();
            try
            {
                var targetDisplay = _displays.FirstOrDefault(d => d.DisplayNumber == displayNumber);
                if (targetDisplay == null)
                {
                    throw new ArgumentException($"未找到显示器 #{displayNumber}");
                }
                
                if (!_monitorWallpapers.TryGetValue(targetDisplay.DeviceName, out var wallpaper))
                {
                    throw new InvalidOperationException($"显示器 #{displayNumber} 的壁纸未初始化");
                }
                
                // 重新创建一个黑色背景的图像
                var newWallpaper = new Image<Rgba32>(wallpaper.Width, wallpaper.Height, Color.Black);
                _monitorWallpapers[targetDisplay.DeviceName] = newWallpaper;
                wallpaper = newWallpaper;
                
                // 加载并调整图片大小以适应整个显示器
                using (var image = await Image.LoadAsync<Rgba32>(imagePath))
                {
                    int width = wallpaper.Width;
                    int height = wallpaper.Height;
                    
                    // 计算缩放和位置以保持纵横比
                    float scale = Math.Max((float)width / image.Width, (float)height / image.Height);
                    int newWidth = (int)(image.Width * scale);
                    int newHeight = (int)(image.Height * scale);
                    
                    // 计算居中位置
                    int x = (width - newWidth) / 2;
                    int y = (height - newHeight) / 2;
                    
                    // 调整图像大小并绘制到壁纸上
                    image.Mutate(i => i.Resize(newWidth, newHeight));
                    wallpaper.Mutate(ctx => ctx.DrawImage(image, new Point(x, y), 1));
                }
                
                // 保存并应用壁纸
                string outPath = Path.Combine(_destFolder, $"wallpaper_monitor_{displayNumber}.jpg");
                await wallpaper.SaveAsJpegAsync(outPath);
                
                // 设置壁纸
                if (DesktopWallpaperApi.IsPerMonitorWallpaperSupported())
                {
                    var wallpaperPaths = new Dictionary<string, string>();
                    if (_monitorToDevicePathMap.TryGetValue(targetDisplay.DeviceName, out string devicePath))
                    {
                        wallpaperPaths[devicePath] = outPath;
                        DesktopWallpaperApi.SetWallpaperForAllMonitors(wallpaperPaths);
                    }
                }
                else if (targetDisplay.IsPrimary)
                {
                    // 对于不支持每显示器壁纸的系统，只能设置主显示器的壁纸
                    WallpaperSetter.Set(outPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"为显示器 #{displayNumber} 设置壁纸时出错: {ex.Message}");
                throw;
            }
            finally
            {
                _updateLock.Release();
            }
        }

        // 添加设置单张图片作为所有显示器的壁纸的方法
        public async Task SetImageForAllMonitors(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                throw new FileNotFoundException("找不到指定的图片文件", imagePath);
            }
            
            await _updateLock.WaitAsync();
            try
            {
                // 加载原始图像
                string outPath = Path.Combine(_destFolder, "wallpaper.jpg");
                
                // 复制图片到目标路径
                File.Copy(imagePath, outPath, true);
                
                // 使用高级API设置单一壁纸
                try
                {
                    DesktopWallpaperApi.SetSingleWallpaper(outPath);
                }
                catch
                {
                    // 回退到传统API
                    WallpaperSetter.Set(outPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置壁纸时出错: {ex.Message}");
                throw;
            }
            finally
            {
                _updateLock.Release();
            }
        }
    }
} 