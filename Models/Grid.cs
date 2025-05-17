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
    /// <summary>
    /// Represents a grid area on the wallpaper where a cover image can be drawn and manages its lifecycle.
    /// </summary>
    public class Grid : IDisposable
    {
        private readonly object _sync = new object();
        private string _currentCoverPath = string.Empty;

        /// <summary>
        /// The path of the currently drawn cover image.
        /// </summary>
        public string CurrentCoverPath => _currentCoverPath;
    
        /// <summary>
        /// The position of the grid on the wallpaper (floating-point coordinates).
        /// </summary>
        public PointF Position { get; }

        /// <summary>
        /// The size of the grid (floating-point width and height).
        /// </summary>
        public SizeF Size { get; }

        /// <summary>
        /// The current cover image instance in use.
        /// </summary>
        public Image<Rgba32>? CurrentCover { get; private set; }

        private readonly ImageManager _imageManager;
        private bool _disposed;

        /// <summary>
        /// Constructor to initialize the grid's position, size, and ImageManager instance.
        /// </summary>
        /// <param name="position">The top-left position of the grid on the wallpaper.</param>
        /// <param name="size">The width and height of the grid.</param>
        /// <param name="imageManager">An ImageManager instance for loading and caching images.</param>
        public Grid(PointF position, SizeF size, ImageManager imageManager)
        {
            Position = position;
            Size = size;
            _imageManager = imageManager ?? throw new ArgumentNullException(nameof(imageManager));
        }

        /// <summary>
        /// Asynchronously updates the grid's cover image and draws the new cover onto the given wallpaper.
        /// </summary>
        /// <param name="coverPath">The file path of the new cover image.</param>
        /// <param name="wallpaper">The wallpaper image to draw onto.</param>
        public async Task UpdateCoverAsync(string coverPath, Image<Rgba32> wallpaper)
        {
            if (string.IsNullOrWhiteSpace(coverPath))
                throw new ArgumentException("Cover image path cannot be null or whitespace.", nameof(coverPath));
            if (wallpaper is null)
                throw new ArgumentNullException(nameof(wallpaper), "Wallpaper image cannot be null.");

            // Normalize the path to prevent cache mismatches
            coverPath = Path.GetFullPath(coverPath);

            // Calculate integer coordinates and size, using rounding to minimize error
            int posX = (int)Math.Round(Position.X);
            int posY = (int)Math.Round(Position.Y);
            int width = Math.Max(1, (int)Math.Round(Size.Width));
            int height = Math.Max(1, (int)Math.Round(Size.Height));

            // Clamp boundaries to prevent exceeding wallpaper bounds
            posX = Math.Max(0, posX);
            posY = Math.Max(0, posY);
            width = Math.Min(width, wallpaper.Width - posX);
            height = Math.Min(height, wallpaper.Height - posY);

            // Determine if it's already the current cover
            lock (_sync)
            {
                if (coverPath.Equals(_currentCoverPath, StringComparison.OrdinalIgnoreCase) && CurrentCover != null)
                {
                    Console.WriteLine($"Cover unchanged, skipping update: {coverPath}");
                    return;
                }
            }

            Image<Rgba32>? newCover = null;
            try
            {
                // Load or retrieve the cover image at the required size from cache
                newCover = await _imageManager.GetOrAddImageAsync(coverPath, new SixLabors.ImageSharp.Size(width, height));
                if (newCover is null)
                {
                    Console.WriteLine($"Failed to load cover image: {coverPath}");
                    return;
                }

                // Crop and resize to the target dimensions
                var resizeOptions = new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(width, height),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                };
                newCover.Mutate(ctx => ctx.Resize(resizeOptions));

                // Dispose of old resources and draw the new cover
                lock (_sync)
                {
                    CurrentCover?.Dispose();
                    wallpaper.Mutate(ctx => ctx.DrawImage(newCover, new Point(posX, posY), 1f));
                    CurrentCover = newCover;
                    _currentCoverPath = coverPath;
                }

                Console.WriteLine($"Grid updated at [{posX},{posY}] {width}×{height}, image: {Path.GetFileName(coverPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating cover: {ex.Message}");
                // If newCover was not applied, dispose of it
                if (newCover != null && !coverPath.Equals(_currentCoverPath, StringComparison.OrdinalIgnoreCase))
                    newCover.Dispose();
            }
        }

        /// <summary>
        /// Disposes the current cover image resources.
        /// </summary>
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
