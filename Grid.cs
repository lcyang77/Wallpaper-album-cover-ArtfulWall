using System;
using System.Drawing; // 确保引入 System.Drawing 命名空间
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

public class Grid
{
    private string _currentCoverPath;  // 新增字段

    public string CurrentCoverPath  
    {
        get => _currentCoverPath;
        private set => _currentCoverPath = value;
    }// 新增属性
    public SixLabors.ImageSharp.PointF Position { get; set; }
    public SixLabors.ImageSharp.SizeF Size { get; set; }
    public Image<Rgba32>? CurrentCover { get; private set; }
    private ImageManager _imageManager;

    public Grid(SixLabors.ImageSharp.PointF position, SixLabors.ImageSharp.SizeF size, ImageManager imageManager)
{
    Position = position;
    Size = size;
    CurrentCover = new Image<Rgba32>(1, 1);
    _imageManager = imageManager;
    _currentCoverPath = string.Empty; // 或者 "default/path/to/cover" 如果有默认路径
}


    public async Task UpdateCoverAsync(string coverPath, Image<Rgba32> wallpaper)
{
    if (string.IsNullOrWhiteSpace(coverPath))
    {
        throw new ArgumentException("封面图像路径不能为空或空白。", nameof(coverPath));
    }

    if (wallpaper == null)
    {
        throw new ArgumentNullException(nameof(wallpaper), "壁纸图像不能为空。");
    }

    // 只有在封面路径改变时才更新封面
    if (_currentCoverPath == coverPath && CurrentCover != null)
    {
        return;
    }

    try
    {
        var cover = await Task.Run(() => 
            _imageManager.GetOrAddImage(coverPath, new SixLabors.ImageSharp.Size((int)Size.Width, (int)Size.Height)));

        // 更新封面图像之前，先释放之前的封面图像资源
        CurrentCover?.Dispose();

        wallpaper.Mutate(x => x.DrawImage(cover, new SixLabors.ImageSharp.Point((int)Position.X, (int)Position.Y), 1));
        CurrentCover = cover;
        _currentCoverPath = coverPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"更新封面时发生错误：{ex.Message}");
    }
}


}
