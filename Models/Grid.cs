using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using ArtfulWall.Services;

namespace ArtfulWall.Models
{
    // 表示可在壁纸上绘制封面图像的网格区域，并管理其生命周期。
    public class Grid : IDisposable
    {
        private readonly object _sync = new object();
        private string _currentCoverPath = string.Empty;

        // 当前已绘制的封面图路径。
        public string CurrentCoverPath => _currentCoverPath;
    
        // 网格在壁纸上的位置（浮点坐标）。
        public PointF Position { get; }

        // 网格的尺寸（浮点宽高）。
        public SizeF Size { get; }

        // 当前正在使用的封面图实例。
        public Image<Rgba32>? CurrentCover { get; private set; }

        private readonly ImageManager _imageManager;
        private bool _disposed;

        // 构造函数，用于初始化网格的位置、大小和 ImageManager 实例。
        public Grid(PointF position, SizeF size, ImageManager imageManager)
        {
            Position = position;
            Size = size;
            _imageManager = imageManager ?? throw new ArgumentNullException(nameof(imageManager));
        }

        // 异步更新网格封面，将新封面绘制到给定壁纸上。
        public async Task UpdateCoverAsync(string coverPath, Image<Rgba32> wallpaper)
        {
            if (string.IsNullOrWhiteSpace(coverPath))
                throw new ArgumentException("封面图像路径不能为 null 或空白。", nameof(coverPath));
            if (wallpaper is null)
                throw new ArgumentNullException(nameof(wallpaper), "壁纸图像不能为 null。");

            // 标准化路径，避免缓存错位
            coverPath = Path.GetFullPath(coverPath);

            // 计算整数坐标和尺寸，使用四舍五入减少误差
            int posX = (int)Math.Round(Position.X);
            int posY = (int)Math.Round(Position.Y);
            int width = Math.Max(1, (int)Math.Round(Size.Width));
            int height = Math.Max(1, (int)Math.Round(Size.Height));

            // 边界约束，防止超出壁纸范围
            posX = Math.Max(0, posX);
            posY = Math.Max(0, posY);
            width = Math.Min(width, wallpaper.Width - posX);
            height = Math.Min(height, wallpaper.Height - posY);

            // 判断是否已是当前封面
            lock (_sync)
            {
                if (coverPath.Equals(_currentCoverPath, StringComparison.OrdinalIgnoreCase) && CurrentCover != null)
                {
                    Console.WriteLine($"封面未改变，跳过更新: {coverPath}");
                    return;
                }
            }

            Image<Rgba32>? newCover = null;
            try
            {
                // 加载或获取缓存中对应尺寸的封面图
                newCover = await _imageManager.GetOrAddImageAsync(coverPath, new SixLabors.ImageSharp.Size(width, height));
                if (newCover is null)
                {
                    Console.WriteLine($"无法加载封面图像: {coverPath}");
                    return;
                }

                // 裁剪并缩放到目标尺寸
                var resizeOptions = new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(width, height),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                };
                newCover.Mutate(ctx => ctx.Resize(resizeOptions));

                // 释放旧资源并绘制新封面
                lock (_sync)
                {
                    CurrentCover?.Dispose();
                    wallpaper.Mutate(ctx => ctx.DrawImage(newCover, new Point(posX, posY), 1f));
                    CurrentCover = newCover;
                    _currentCoverPath = coverPath;
                }

                Console.WriteLine($"已更新网格: [{posX},{posY}] {width}×{height}, 图像: {Path.GetFileName(coverPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新封面时发生错误: {ex.Message}");
                // 如果 newCover 尚未被应用，则释放它
                if (newCover != null && !coverPath.Equals(_currentCoverPath, StringComparison.OrdinalIgnoreCase))
                    newCover.Dispose();
            }
        }

        // 释放当前封面图资源。
        public void Dispose()
        {
            if (_disposed) return;
            lock (_sync)
            {
                CurrentCover?.Dispose();
                _disposed = true;
            }
        }
    }
}
