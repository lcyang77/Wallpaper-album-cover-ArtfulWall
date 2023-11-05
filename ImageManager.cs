using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp; // ImageSharp 命名空间
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using PointF = SixLabors.ImageSharp.PointF;
using System.Threading;
using Timer = System.Threading.Timer; // 明确使用 System.Threading.Timer
using Size = SixLabors.ImageSharp.Size; // 明确使用 SixLabors.ImageSharp.Size

public class ImageManager
{
    private class CacheItem
{
    public Image<Rgba32>? Image { get; set; }
    public LinkedListNode<string>? Node { get; set; }
}


    private readonly Dictionary<string, CacheItem> _imageCache;
    private readonly LinkedList<string> _lruList;
    private readonly int _maxCacheSize;
    private readonly Timer _cacheCleanupTimer;

    public ImageManager(int maxCacheSize = 150, TimeSpan? cacheCleanupInterval = null)
    {
        _imageCache = new Dictionary<string, CacheItem>();
        _lruList = new LinkedList<string>();
        _maxCacheSize = maxCacheSize;
        _cacheCleanupTimer = new Timer(CleanupCache, null, cacheCleanupInterval ?? TimeSpan.FromMinutes(30), cacheCleanupInterval ?? Timeout.InfiniteTimeSpan);
    }

    public Image<Rgba32>? GetOrAddImage(string path, Size size)
    {
        string key = GetCacheKey(path);
        if (_imageCache.TryGetValue(key, out CacheItem cacheItem))
        {
            // Move the item to the end of the LRU list
            _lruList.Remove(cacheItem.Node);
            _lruList.AddLast(cacheItem.Node);
            return cacheItem.Image;
        }

        try
        {
            var image = LoadAndResizeImage(path, size);
            var node = new LinkedListNode<string>(key);
            cacheItem = new CacheItem { Image = image, Node = node };

            _imageCache[key] = cacheItem;
            _lruList.AddLast(node);

            if (_imageCache.Count > _maxCacheSize)
            {
                RemoveLeastRecentlyUsedItem();
            }

            return image;
        }
        catch (Exception ex)
        {
            // Handle exceptions in loading or processing the image
            Console.WriteLine($"Error loading image at {path}: {ex.Message}");
            return null;
        }
    }

    private string GetCacheKey(string filePath)
    {
        var lastWriteTime = File.GetLastWriteTimeUtc(filePath).Ticks.ToString();
        return $"{filePath}_{lastWriteTime}";
    }

    private Image<Rgba32> LoadAndResizeImage(string path, Size targetSize)
{
    using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);

    // 如果图片不是正方形，则先裁剪成正方形
    if (image.Width != image.Height)
    {
        // 计算需要裁剪的尺寸，取宽和高中的较小值
        int squareSize = Math.Min(image.Width, image.Height);
        
        // 计算裁剪区域的左上角坐标，以确保裁剪区域以图像中心为中心
        int cropX = (image.Width - squareSize) / 2;
        int cropY = (image.Height - squareSize) / 2;
        
        // 创建裁剪区域
        var cropRectangle = new SixLabors.ImageSharp.Rectangle(cropX, cropY, squareSize, squareSize);

        // 执行裁剪
        image.Mutate(x => x.Crop(cropRectangle));
    }

    // 调整图像的大小为目标尺寸
    // 因为已经裁剪为正方形，所以可以直接使用 targetSize
    image.Mutate(x => x.Resize(targetSize.Width, targetSize.Height));

    return image.Clone();
}


    private void RemoveLeastRecentlyUsedItem()
    {
        if (_lruList.First is not null)
        {
            _imageCache.Remove(_lruList.First.Value);
            _lruList.RemoveFirst();
        }
    }

    private void CleanupCache(object state)
    {
        // Add logic to cleanup the cache based on size, memory usage, or time interval
        while (_imageCache.Count > _maxCacheSize)
        {
            RemoveLeastRecentlyUsedItem();
        }
    }

    public void ClearCache()
{
    foreach (var item in _imageCache.Values)
    {
        item.Image?.Dispose();
    }
    _imageCache.Clear();
    _lruList.Clear();
}


    public void Dispose()
    {
        ClearCache();
        _cacheCleanupTimer.Dispose();
    }
}
