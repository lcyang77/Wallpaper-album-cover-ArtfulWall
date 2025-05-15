using System;
using System.IO; // 用于 Path.Combine
using System.Text.Json.Serialization;

namespace ArtfulWall.Models
{
    /// <summary>
    /// 代表应用程序的配置信息。
    /// Implements IEquatable for proper comparison.
    /// </summary>
    public class Configuration : IEquatable<Configuration>
    {
        /// <summary>
        /// 包含源图片的文件夹路径。
        /// </summary>
        public string? FolderPath { get; set; }

        /// <summary>
        /// 生成的壁纸图片的目标文件夹路径。
        /// 通常是 FolderPath 下的 "my_wallpaper" 子目录。
        /// </summary>
        public string? DestFolder { get; set; }

        /// <summary>
        /// 生成壁纸的宽度（像素）。
        /// </summary>
        public int Width { get; set; } = 1920; // Default width

        /// <summary>
        /// 生成壁纸的高度（像素）。
        /// </summary>
        public int Height { get; set; } = 1080; // Default height

        /// <summary>
        /// 壁纸网格的行数。
        /// </summary>
        public int Rows { get; set; } = 1; // Default rows

        /// <summary>
        /// 壁纸网格的列数。
        /// </summary>
        public int Cols { get; set; } = 1; // Default columns

        /// <summary>
        /// 壁纸切换的最小间隔时间（秒）。
        /// </summary>
        public int MinInterval { get; set; } = 3;

        /// <summary>
        /// 壁纸切换的最大间隔时间（秒）。
        /// </summary>
        public int MaxInterval { get; set; } = 10;

        /// <summary>
        /// 创建当前配置对象的浅拷贝。
        /// </summary>
        /// <returns>当前配置对象的副本。</returns>
        public Configuration Clone()
        {
            return (Configuration)this.MemberwiseClone(); // MemberwiseClone is sufficient for this class
        }

        /// <summary>
        /// 确定指定的 Configuration 对象是否等于当前对象。
        /// </summary>
        /// <param name="other">要与当前对象进行比较的 Configuration 对象。</param>
        /// <returns>如果指定的对象等于当前对象，则为 true；否则为 false。</returns>
        public bool Equals(Configuration? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(DestFolder, other.DestFolder, StringComparison.OrdinalIgnoreCase) &&
                   Width == other.Width &&
                   Height == other.Height &&
                   Rows == other.Rows &&
                   Cols == other.Cols &&
                   MinInterval == other.MinInterval &&
                   MaxInterval == other.MaxInterval;
        }

        /// <summary>
        /// 确定指定的对象是否等于当前对象。
        /// </summary>
        /// <param name="obj">要与当前对象进行比较的对象。</param>
        /// <returns>如果指定的对象等于当前对象，则为 true；否则为 false。</returns>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Configuration);
        }

        /// <summary>
        /// 返回此 Configuration 实例的哈希代码。
        /// </summary>
        /// <returns>32 位有符号整数哈希代码。</returns>
        public override int GetHashCode()
        {
            // Use HashCode.Combine for a good hash code distribution
            return HashCode.Combine(
                FolderPath?.ToLowerInvariant(), // Use ToLowerInvariant for case-insensitive comparison in hash
                DestFolder?.ToLowerInvariant(),
                Width,
                Height,
                Rows,
                Cols,
                MinInterval,
                MaxInterval
            );
        }

        /// <summary>
        /// 返回表示当前对象的字符串。
        /// </summary>
        public override string ToString()
        {
            return $"Path: {FolderPath}, Size: {Width}x{Height}, Grid: {Rows}x{Cols}, Interval: {MinInterval}-{MaxInterval}s";
        }
    }
}
