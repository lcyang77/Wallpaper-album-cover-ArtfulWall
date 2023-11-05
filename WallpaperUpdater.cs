using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Threading;

public class WallpaperUpdater : IDisposable
{
    private readonly int _minInterval;
    private readonly int _maxInterval;
    private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1); // 修改为 SemaphoreSlim 用于异步锁
    private readonly string _folderPath;
    private readonly string _destFolder;
    private readonly int _width;
    private readonly int _height;
    private readonly int _rows;
    private readonly int _cols;
    private ImageManager _imageManager;
    private List<string> _coverPaths;
    private Image<Rgba32> _wallpaper;
    private List<Grid> _grids;
    private System.Threading.Timer _timer;

    private ConcurrentDictionary<Grid, DateTime> _lastUpdateTimes;
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
    int minInterval, // 添加这个参数
    int maxInterval  // 添加这个参数
)
    {
    _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath), "文件夹路径不能为 null。");
    _destFolder = destFolder ?? throw new ArgumentNullException(nameof(destFolder), "目标文件夹路径不能为 null。");
    _width = width;
    _height = height;
    _rows = rows;
    _cols = cols;
    _imageManager = imageManager ?? throw new ArgumentNullException(nameof(imageManager), "图像管理器不能为 null。");
    _minInterval = minInterval; // 现在这里不会有错误，因为 minInterval 参数已定义
    _maxInterval = maxInterval; // 现在这里不会有错误，因为 maxInterval 参数已定义
    _lastUpdateTimes = new ConcurrentDictionary<Grid, DateTime>();
    _coverPaths = new List<string>();
    _grids = new List<Grid>();
}

    public void Start()
    {
        if (string.IsNullOrWhiteSpace(_folderPath))
        {
            Console.WriteLine("文件夹路径无效。");
            return;
        }

        LoadAlbumCovers();
        InitializeWallpaperAndGrids();
        ScheduleUpdate();
    }

    private void LoadAlbumCovers()
    {
        int retries = 0;
        while (retries < 3)
        {
            try
            {
                _coverPaths = Directory.EnumerateFiles(_folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                                   !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载封面路径时发生错误：{ex.Message}");
                retries++;
                if (retries >= 3)
                {
                    throw new InvalidOperationException("无法加载封面图片。", ex);
                }
            }
        }
    }

   private void InitializeWallpaperAndGrids()
{
    // 计算基础网格尺寸
    int baseGridSize = Math.Min(_width / _cols, _height / _rows);

    // 计算总网格尺寸和剩余空间
    int totalGridWidth = baseGridSize * _cols;
    int totalGridHeight = baseGridSize * _rows;
    int remainingWidth = _width - totalGridWidth;
    int remainingHeight = _height - totalGridHeight;

    // 如果网格总尺寸超出壁纸尺寸，适当减少网格尺寸
    if (remainingWidth < 0) {
        baseGridSize -= 1; // 减少宽度
        totalGridWidth = baseGridSize * _cols;
        remainingWidth = _width - totalGridWidth;
    }

    if (remainingHeight < 0) {
        baseGridSize -= 1; // 减少高度
        totalGridHeight = baseGridSize * _rows;
        remainingHeight = _height - totalGridHeight;
    }

    // 均匀分配间隙
    int gapSizeWidth = _cols > 1 ? remainingWidth / (_cols - 1) : 0;
    int gapSizeHeight = _rows > 1 ? remainingHeight / (_rows - 1) : 0;

    _wallpaper = new SixLabors.ImageSharp.Image<Rgba32>(_width, _height);
    _grids = new List<Grid>();

    for (int i = 0; i < _rows * _cols; i++)
    {
        int col = i % _cols;
        int row = i / _cols;

        // 保证四周没有间隙
        int offsetX = col * (baseGridSize + gapSizeWidth);
        int offsetY = row * (baseGridSize + gapSizeHeight);

        SixLabors.ImageSharp.PointF topLeft = new SixLabors.ImageSharp.PointF(offsetX, offsetY);
        Grid grid = new Grid(topLeft, new SixLabors.ImageSharp.SizeF(baseGridSize, baseGridSize), _imageManager);
        _grids.Add(grid);
    }
}



    private void ScheduleUpdate()
{
    // 初始化定时器，触发立即更新，然后设置为手动重置
    _timer = new System.Threading.Timer(async _ =>
    {
        await UpdateWallpaper();
    }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan); // 初始触发设为0以立即开始
}

private async Task UpdateWallpaper()
{
    try
    {
        // 异步等待进入 SemaphoreSlim
        await _updateLock.WaitAsync();

        var now = DateTime.Now;
        List<Grid> updateCandidates;

        // 确定需要更新的网格
        if (_isFirstUpdate)
        {
            updateCandidates = _grids.ToList();
            _isFirstUpdate = false;
        }
        else
        {
            // 选出满足更新条件的网格
            updateCandidates = _grids.Where(g => 
                !_lastUpdateTimes.ContainsKey(g) || 
                (now - _lastUpdateTimes[g]).TotalSeconds >= 10).ToList();

            // 计算实际更新的网格数量
            int maxUpdateCount = Math.Min(updateCandidates.Count, _grids.Count / 4) + 1;
            int updateCount = (updateCandidates.Count <= 3) 
                ? updateCandidates.Count 
                : Random.Shared.Next(3, maxUpdateCount);
            updateCandidates = updateCandidates.OrderBy(x => Guid.NewGuid()).Take(updateCount).ToList();
        }

        // 找出当前未使用的封面
        var currentCovers = _grids.Select(g => g.CurrentCoverPath).ToList();
        var availableCovers = _coverPaths.Except(currentCovers).ToList();

        // 更新每个选定的网格
        foreach (var grid in updateCandidates)
        {
            if (!availableCovers.Any()) break;

            var newCoverPath = availableCovers[Random.Shared.Next(availableCovers.Count)];
            availableCovers.Remove(newCoverPath);

            // 更新网格封面
            await grid.UpdateCoverAsync(newCoverPath, _wallpaper);
            _lastUpdateTimes[grid] = now;
        }

        // 保存并设置壁纸
        string wallpaperPath = Path.Combine(_destFolder, "wallpaper.jpg");
        _wallpaper.SaveAsJpeg(wallpaperPath);
        Wallpaper.Set(wallpaperPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"更新壁纸时发生错误: {ex.Message}");
        // 处理异常
    }
    finally
    {
        // 释放 SemaphoreSlim
        _updateLock.Release();

        if (!_disposed)
        {
            // 计算下次更新时间间隔
            var nextUpdateInSeconds = Random.Shared.Next(_minInterval, _maxInterval + 1);
            // 重新设置定时器
            _timer?.Change(TimeSpan.FromSeconds(nextUpdateInSeconds), Timeout.InfiniteTimeSpan);
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
        _updateLock?.Dispose(); // 确保释放 SemaphoreSlim 资源

        GC.SuppressFinalize(this);
    }
}

}

