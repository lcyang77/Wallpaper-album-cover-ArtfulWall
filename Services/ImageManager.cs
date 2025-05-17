// ImageManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtfulWall.Services
{
    public class ImageManager : IDisposable
    {
        // Defines an inner class representing a single cache entry
        private class CacheItem
        {
            public Image<Rgba32>? Image { get; set; }            // The cached image object
            public LinkedListNode<string>? Node { get; set; }    // Node in the LRU linked list
            public long Size { get; set; }                       // Size of the image in bytes
            public DateTime LastAccessTime { get; set; }         // Timestamp of last access
        }

        private readonly ConcurrentDictionary<string, CacheItem> _imageCache;   // Thread-safe dictionary storing cache items
        private readonly LinkedList<string> _lruList;                            // Linked list implementing LRU eviction order
        private readonly object _lruLock = new object();                        // Lock object for synchronizing LRU list access
        private readonly int _maxCacheSize;                                      // Maximum number of items allowed in cache
        private readonly Timer _cacheCleanupTimer;                               // Timer for periodic cache cleanup

        /// <summary>
        /// Initializes a new instance of ImageManager with optional cache size and cleanup interval.
        /// </summary>
        public ImageManager(int maxCacheSize = 150, TimeSpan? cacheCleanupInterval = null)
        {
            _imageCache = new ConcurrentDictionary<string, CacheItem>();
            _lruList = new LinkedList<string>();
            _maxCacheSize = maxCacheSize;
            // Set up a timer to call CleanupCache at the specified interval (default: 30 minutes)
            _cacheCleanupTimer = new Timer(
                CleanupCache,
                null,
                cacheCleanupInterval ?? TimeSpan.FromMinutes(30),
                cacheCleanupInterval ?? Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Calculates the memory footprint of the image in bytes.
        /// </summary>
        private long CalculateImageSize(Image<Rgba32> image)
        {
            int bytesPerPixel = image.PixelType.BitsPerPixel / 8;
            return image.Width * image.Height * bytesPerPixel;
        }

        /// <summary>
        /// Retrieves an image from cache or loads and adds it if not present.
        /// </summary>
        /// <param name="path">File system path to the image.</param>
        /// <param name="size">Desired dimensions for the image.</param>
        public async Task<Image<Rgba32>?> GetOrAddImageAsync(string path, Size size)
        {
            // Generate a cache key based on file path, hash, and size
            string key = await GetCacheKeyAsync(path).ConfigureAwait(false);

            // Attempt to retrieve from cache
            if (_imageCache.TryGetValue(key, out CacheItem cacheItem))
            {
                lock (_lruLock)
                {
                    // Move accessed node to the end of the LRU list
                    _lruList.Remove(cacheItem.Node);
                    _lruList.AddLast(cacheItem.Node);
                    cacheItem.LastAccessTime = DateTime.Now;
                }
                return cacheItem.Image;
            }

            // If not in cache, load and resize the image
            try
            {
                var image = await LoadAndResizeImageAsync(path, size).ConfigureAwait(false);
                var imageSize = CalculateImageSize(image);

                var node = new LinkedListNode<string>(key);
                cacheItem = new CacheItem
                {
                    Image = image,
                    Node = node,
                    Size = imageSize,
                    LastAccessTime = DateTime.Now
                };

                if (_imageCache.TryAdd(key, cacheItem))
                {
                    lock (_lruLock)
                    {
                        _lruList.AddLast(node);
                        // Evict least recently used item if cache exceeds maximum size
                        if (_imageCache.Count > _maxCacheSize)
                        {
                            RemoveLeastRecentlyUsedItem();
                        }
                    }
                }

                return image;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image at {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates a unique cache key for the file based on checksum and file size.
        /// </summary>
        private async Task<string> GetCacheKeyAsync(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var sha = System.Security.Cryptography.SHA256.Create();
            byte[] checksum = await Task.Run(() => sha.ComputeHash(stream)).ConfigureAwait(false);
            string hash = BitConverter.ToString(checksum).Replace("-", string.Empty);
            long fileSize = new FileInfo(filePath).Length;
            return $"{filePath}_{hash}_{fileSize}";
        }

        /// <summary>
        /// Loads an image from disk, crops it to a square if necessary, and resizes to target dimensions.
        /// </summary>
        private async Task<Image<Rgba32>> LoadAndResizeImageAsync(string path, Size targetSize)
        {
            return await Task.Run(() =>
            {
                using var image = Image.Load<Rgba32>(path);

                // Crop to square centered region if dimensions are not equal
                if (image.Width != image.Height)
                {
                    int squareSize = Math.Min(image.Width, image.Height);
                    int cropX = (image.Width - squareSize) / 2;
                    int cropY = (image.Height - squareSize) / 2;
                    var cropRectangle = new Rectangle(cropX, cropY, squareSize, squareSize);
                    image.Mutate(x => x.Crop(cropRectangle));
                }

                // Resize the image to the requested dimensions
                image.Mutate(x => x.Resize(targetSize.Width, targetSize.Height));
                return image.Clone();
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the least recently used item from the cache and frees resources.
        /// </summary>
        private void RemoveLeastRecentlyUsedItem()
        {
            lock (_lruLock)
            {
                if (_lruList.First != null)
                {
                    var key = _lruList.First.Value;
                    if (_imageCache.TryRemove(key, out CacheItem? cacheItem))
                    {
                        cacheItem.Image?.Dispose();
                        _lruList.RemoveFirst();
                    }
                }
            }
        }

        /// <summary>
        /// Periodically called to enforce cache size limits by removing large or stale items.
        /// </summary>
        private void CleanupCache(object state)
        {
            lock (_lruLock)
            {
                var itemsToRemove = _imageCache.Values
                    .OrderByDescending(item => item.Size)        // Sort by size descending
                    .ThenBy(item => item.LastAccessTime)         // Then by oldest access time
                    .TakeWhile(_ => _imageCache.Count > _maxCacheSize)
                    .Select(item => item.Node?.Value)
                    .ToList();

                foreach (var key in itemsToRemove)
                {
                    if (_imageCache.TryRemove(key, out CacheItem? cacheItem))
                    {
                        cacheItem.Image?.Dispose();
                        _lruList.Remove(cacheItem.Node);
                    }
                }
            }
        }

        /// <summary>
        /// Clears all cache entries and releases their image resources.
        /// </summary>
        public void ClearCache()
        {
            lock (_lruLock)
            {
                foreach (var item in _imageCache.Values)
                {
                    item.Image?.Dispose();
                }
                _imageCache.Clear();
                _lruList.Clear();
            }
        }

        /// <summary>
        /// Disposes the ImageManager, clearing cache and stopping the cleanup timer.
        /// </summary>
        public void Dispose()
        {
            ClearCache();                  // Release all cached image resources
            _cacheCleanupTimer.Dispose();  // Stop and dispose the cleanup timer
        }
    }
}
