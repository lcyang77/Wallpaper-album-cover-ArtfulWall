//  Configuration.cs
using System;
using System.IO; // 用于 Path.Combine
using System.Text.Json.Serialization;
using System.Collections.Generic;

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
        /// 在多显示器模式下，此值用作单一壁纸模式的默认值。
        /// </summary>
        public int Width { get; set; } = 1920; // Default width

        /// <summary>
        /// 生成壁纸的高度（像素）。
        /// 在多显示器模式下，此值用作单一壁纸模式的默认值。
        /// </summary>
        public int Height { get; set; } = 1080; // Default height

        /// <summary>
        /// 壁纸网格的行数。
        /// 在多显示器模式下，此值用作默认行数。
        /// </summary>
        public int Rows { get; set; } = 1; // Default rows

        /// <summary>
        /// 壁纸网格的列数。
        /// 在多显示器模式下，此值用作默认列数。
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
        /// 壁纸模式：单一壁纸或每显示器壁纸。
        /// </summary>
        public WallpaperMode Mode { get; set; } = WallpaperMode.PerMonitor;

        /// <summary>
        /// 是否监听显示设置变更并自动调整壁纸。
        /// </summary>
        public bool AutoAdjustToDisplayChanges { get; set; } = true;

        /// <summary>
        /// 是否自动适应DPI缩放。
        /// </summary>
        public bool AdaptToDpiScaling { get; set; } = true;

        /// <summary>
        /// 每个显示器的特定配置。
        /// </summary>
        public List<MonitorConfiguration> MonitorConfigurations { get; set; } = new List<MonitorConfiguration>();

        /// <summary>
        /// 壁纸模式枚举。
        /// </summary>
        public enum WallpaperMode
        {
            /// <summary>
            /// 为每个显示器生成独立壁纸。
            /// </summary>
            PerMonitor,
            
            /// <summary>
            /// 使用单一壁纸适配所有显示器。
            /// </summary>
            Single
        }

        /// <summary>
        /// 创建当前配置对象的浅拷贝。
        /// </summary>
        /// <returns>当前配置对象的副本。</returns>
        public Configuration Clone()
        {
            var clone = (Configuration)this.MemberwiseClone();
            
            // 深拷贝MonitorConfigurations
            clone.MonitorConfigurations = new List<MonitorConfiguration>();
            foreach (var monConfig in this.MonitorConfigurations)
            {
                clone.MonitorConfigurations.Add(monConfig.Clone());
            }
            
            return clone;
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

            // 基本属性比较
            bool basicEquals = string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(DestFolder, other.DestFolder, StringComparison.OrdinalIgnoreCase) &&
                   Width == other.Width &&
                   Height == other.Height &&
                   Rows == other.Rows &&
                   Cols == other.Cols &&
                   MinInterval == other.MinInterval &&
                   MaxInterval == other.MaxInterval &&
                   Mode == other.Mode &&
                   AutoAdjustToDisplayChanges == other.AutoAdjustToDisplayChanges &&
                   AdaptToDpiScaling == other.AdaptToDpiScaling;

            if (!basicEquals) return false;

            // 检查MonitorConfigurations
            if (MonitorConfigurations.Count != other.MonitorConfigurations.Count)
                return false;

            for (int i = 0; i < MonitorConfigurations.Count; i++)
            {
                if (!MonitorConfigurations[i].Equals(other.MonitorConfigurations[i]))
                    return false;
            }

            return true;
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
            // 基本属性哈希
            HashCode hash = new HashCode();
            hash.Add(FolderPath?.ToLowerInvariant());
            hash.Add(DestFolder?.ToLowerInvariant());
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(Rows);
            hash.Add(Cols);
            hash.Add(MinInterval);
            hash.Add(MaxInterval);
            hash.Add(Mode);
            hash.Add(AutoAdjustToDisplayChanges);
            hash.Add(AdaptToDpiScaling);
            
            // 添加MonitorConfigurations哈希
            foreach (var config in MonitorConfigurations)
            {
                hash.Add(config);
            }
            
            return hash.ToHashCode();
        }

        /// <summary>
        /// 返回表示当前对象的字符串。
        /// </summary>
        public override string ToString()
        {
            return $"Path: {FolderPath}, Size: {Width}x{Height}, Grid: {Rows}x{Cols}, Interval: {MinInterval}-{MaxInterval}s, Mode: {Mode}";
        }
    }

    /// <summary>
    /// 存储每个显示器的特定配置。
    /// </summary>
    public class MonitorConfiguration : IEquatable<MonitorConfiguration>
    {
        /// <summary>
        /// 显示器ID（设备路径）。
        /// </summary>
        public string MonitorId { get; set; } = "";
        
        /// <summary>
        /// 显示器序号。
        /// </summary>
        public int DisplayNumber { get; set; }
        
        /// <summary>
        /// 显示器特定的宽度。
        /// </summary>
        public int Width { get; set; }
        
        /// <summary>
        /// 显示器特定的高度。
        /// </summary>
        public int Height { get; set; }
        
        /// <summary>
        /// DPI缩放因子
        /// </summary>
        public float DpiScaling { get; set; } = 1.0f;
        
        /// <summary>
        /// 显示器特定的网格行数。
        /// </summary>
        public int Rows { get; set; } = 1;
        
        /// <summary>
        /// 显示器特定的网格列数。
        /// </summary>
        public int Cols { get; set; } = 1;
        
        /// <summary>
        /// 显示器是否为纵向模式。
        /// </summary>
        public bool IsPortrait { get; set; }

        /// <summary>
        /// 创建当前对象的浅拷贝。
        /// </summary>
        public MonitorConfiguration Clone()
        {
            return (MonitorConfiguration)this.MemberwiseClone();
        }

        /// <summary>
        /// 确定指定的对象是否等于当前对象。
        /// </summary>
        public bool Equals(MonitorConfiguration? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(MonitorId, other.MonitorId) &&
                   DisplayNumber == other.DisplayNumber &&
                   Width == other.Width &&
                   Height == other.Height &&
                   DpiScaling == other.DpiScaling &&
                   Rows == other.Rows &&
                   Cols == other.Cols &&
                   IsPortrait == other.IsPortrait;
        }

        /// <summary>
        /// 确定指定的对象是否等于当前对象。
        /// </summary>
        public override bool Equals(object? obj)
        {
            return Equals(obj as MonitorConfiguration);
        }

        /// <summary>
        /// 返回此实例的哈希代码。
        /// </summary>
        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(MonitorId);
            hash.Add(DisplayNumber);
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(DpiScaling);
            hash.Add(Rows);
            hash.Add(Cols);
            hash.Add(IsPortrait);
            return hash.ToHashCode();
        }
    }
}
