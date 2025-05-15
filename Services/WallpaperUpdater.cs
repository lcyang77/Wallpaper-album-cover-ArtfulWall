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

        private ImageManager _imageManager;
        private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);
        private List<string> _coverPaths;
        private List<Grid> _grids;
        private Image<Rgba32>? _wallpaper;
        private Timer? _timer;
        private readonly ConcurrentDictionary<Grid, DateTime> _lastUpdateTimes;
        private bool _isFirstUpdate = true;
        private bool _disposed = false;

        public WallpaperUpdater(
            string folderPath,
            string destFolder,
            int width,
            int height,
            int rows,
            int cols,
            ImageManager imageManager,
            int minInterval,
            int maxInterval
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

            _lastUpdateTimes = new ConcurrentDictionary<Grid, DateTime>();
            _coverPaths      = new List<string>();
            _grids           = new List<Grid>();
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_folderPath) || !Directory.Exists(_folderPath))
            {
                Console.WriteLine("文件夹路径无效或不存在。");
                return;
            }

            LoadAlbumCovers();
            InitializeWallpaperAndGrids();
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

            LoadAlbumCovers();
            InitializeWallpaperAndGrids();
            _lastUpdateTimes.Clear();
            _isFirstUpdate = true;
            _timer?.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
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
                            !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase))
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
            _wallpaper?.Dispose();
            _grids.Clear();

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

        private void ScheduleUpdate()
        {
            _timer = new Timer(async _ => await UpdateWallpaper(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        private async Task UpdateWallpaper()
        {
            await _updateLock.WaitAsync();
            try
            {
                var now = DateTime.Now;
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
                _wallpaper!.SaveAsJpeg(outPath);
                WallpaperSetter.Set(outPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新壁纸时发生错误: {ex.Message}");
            }
            finally
            {
                _updateLock.Release();
                if (!_disposed)
                {
                    int nextSec = Random.Shared.Next(_minInterval, _maxInterval + 1);
                    _timer?.Change(TimeSpan.FromSeconds(nextSec), Timeout.InfiniteTimeSpan);
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _timer?.Dispose();
                _timer = null;
                _wallpaper?.Dispose();
                _wallpaper = null;
                foreach (var grid in _grids)
                {
                    grid.CurrentCover?.Dispose();
                }
                _grids.Clear();
            }
        }
    }
} 