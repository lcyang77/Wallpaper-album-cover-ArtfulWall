// Configuration.cs
using System;
using System.IO; // for Path.Combine
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace ArtfulWall.Models
{
    /// <summary>
    /// Represents the application configuration settings.
    /// Implements IEquatable for proper comparison.
    /// </summary>
    public class Configuration : IEquatable<Configuration>
    {
        /// <summary>
        /// Folder path containing the source images.
        /// </summary>
        public string? FolderPath { get; set; }

        /// <summary>
        /// Destination folder path for generated wallpaper images.
        /// Typically a "my_wallpaper" subdirectory under FolderPath.
        /// </summary>
        public string? DestFolder { get; set; }

        /// <summary>
        /// Width of the generated wallpaper in pixels.
        /// Used as default for single wallpaper mode in multi-monitor setups.
        /// </summary>
        public int Width { get; set; } = 1920;

        /// <summary>
        /// Height of the generated wallpaper in pixels.
        /// Used as default for single wallpaper mode in multi-monitor setups.
        /// </summary>
        public int Height { get; set; } = 1080;

        /// <summary>
        /// Number of rows in the wallpaper grid.
        /// Used as default in multi-monitor mode.
        /// </summary>
        public int Rows { get; set; } = 1;

        /// <summary>
        /// Number of columns in the wallpaper grid.
        /// Used as default in multi-monitor mode.
        /// </summary>
        public int Cols { get; set; } = 1;

        /// <summary>
        /// Minimum interval between wallpaper changes in seconds.
        /// </summary>
        public int MinInterval { get; set; } = 3;

        /// <summary>
        /// Maximum interval between wallpaper changes in seconds.
        /// </summary>
        public int MaxInterval { get; set; } = 10;

        /// <summary>
        /// Wallpaper mode: Per-monitor or single wallpaper.
        /// </summary>
        public WallpaperMode Mode { get; set; } = WallpaperMode.PerMonitor;

        /// <summary>
        /// Whether to listen for display changes and auto-adjust wallpaper.
        /// </summary>
        public bool AutoAdjustToDisplayChanges { get; set; } = true;

        /// <summary>
        /// Whether to adapt to DPI scaling automatically.
        /// </summary>
        public bool AdaptToDpiScaling { get; set; } = true;

        /// <summary>
        /// Specific configurations for each monitor.
        /// </summary>
        public List<MonitorConfiguration> MonitorConfigurations { get; set; } = new List<MonitorConfiguration>();

        /// <summary>
        /// Enumeration of wallpaper modes.
        /// </summary>
        public enum WallpaperMode
        {
            /// <summary>
            /// Generate separate wallpaper for each monitor.
            /// </summary>
            PerMonitor,

            /// <summary>
            /// Use a single wallpaper for all monitors.
            /// </summary>
            Single
        }

        /// <summary>
        /// Creates a shallow copy of the current Configuration.
        /// </summary>
        /// <returns>A copy of the current Configuration.</returns>
        public Configuration Clone()
        {
            var clone = (Configuration)this.MemberwiseClone();

            // Deep copy MonitorConfigurations
            clone.MonitorConfigurations = new List<MonitorConfiguration>();
            foreach (var monConfig in this.MonitorConfigurations)
            {
                clone.MonitorConfigurations.Add(monConfig.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Determines whether the specified Configuration is equal to the current one.
        /// </summary>
        /// <param name="other">The Configuration to compare to.</param>
        /// <returns>True if equal; otherwise, false.</returns>
        public bool Equals(Configuration? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            // Compare basic properties
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

            // Check MonitorConfigurations equality
            if (MonitorConfigurations.Count != other.MonitorConfigurations.Count)
                return false;

            for (int i = 0; i < MonitorConfigurations.Count; i++)
            {
                if (!MonitorConfigurations[i].Equals(other.MonitorConfigurations[i]))
                    return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Configuration);
        }

        /// <summary>
        /// Returns the hash code for this Configuration.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
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

            foreach (var config in MonitorConfigurations)
            {
                hash.Add(config);
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns a string representation of the current Configuration.
        /// </summary>
        /// <returns>A string describing the configuration.</returns>
        public override string ToString()
        {
            return $"Path: {FolderPath}, Size: {Width}x{Height}, Grid: {Rows}x{Cols}, Interval: {MinInterval}-{MaxInterval}s, Mode: {Mode}";
        }
    }

    /// <summary>
    /// Stores configuration specific to each monitor.
    /// </summary>
    public class MonitorConfiguration : IEquatable<MonitorConfiguration>
    {
        /// <summary>
        /// Monitor identifier (device path).
        /// </summary>
        public string MonitorId { get; set; } = "";

        /// <summary>
        /// Monitor display order number.
        /// </summary>
        public int DisplayNumber { get; set; }

        /// <summary>
        /// Specific width for the monitor.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Specific height for the monitor.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// DPI scaling factor for the monitor.
        /// </summary>
        public float DpiScaling { get; set; } = 1.0f;

        /// <summary>
        /// Number of grid rows for the monitor.
        /// </summary>
        public int Rows { get; set; } = 1;

        /// <summary>
        /// Number of grid columns for the monitor.
        /// </summary>
        public int Cols { get; set; } = 1;

        /// <summary>
        /// Indicates whether the monitor is in portrait orientation.
        /// </summary>
        public bool IsPortrait { get; set; }

        /// <summary>
        /// Creates a shallow copy of the current MonitorConfiguration.
        /// </summary>
        /// <returns>A copy of the current MonitorConfiguration.</returns>
        public MonitorConfiguration Clone()
        {
            return (MonitorConfiguration)this.MemberwiseClone();
        }

        /// <summary>
        /// Determines whether the specified MonitorConfiguration is equal to the current one.
        /// </summary>
        /// <param name="other">The MonitorConfiguration to compare to.</param>
        /// <returns>True if equal; otherwise, false.</returns>
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

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as MonitorConfiguration);
        }

        /// <summary>
        /// Returns the hash code for this MonitorConfiguration.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
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
