using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace ArtfulWall.Utils
{
    /// <summary>
    /// Provides methods to interact with the Windows Desktop Wallpaper COM interface.
    /// </summary>
    public static class DesktopWallpaperApi
    {
        #region COM Interop Definitions

        /// <summary>
        /// Represents the coordinates of the monitor rectangle.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Enum for wallpaper positioning modes.
        /// </summary>
        public enum DESKTOP_WALLPAPER_POSITION
        {
            DWPOS_CENTER = 0,
            DWPOS_TILE = 1,
            DWPOS_STRETCH = 2,
            DWPOS_FIT = 3,
            DWPOS_FILL = 4,
            DWPOS_SPAN = 5
        }

        /// <summary>
        /// COM interface for desktop wallpaper management.
        /// </summary>
        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);

            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            [return: MarshalAs(UnmanagedType.U4)]
            uint GetMonitorDevicePathCount();

            [return: MarshalAs(UnmanagedType.Struct)]
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);

            void SetBackgroundColor([MarshalAs(UnmanagedType.U4)] uint color);
            [return: MarshalAs(UnmanagedType.U4)]
            uint GetBackgroundColor();

            void SetPosition([MarshalAs(UnmanagedType.I4)] DESKTOP_WALLPAPER_POSITION position);
            [return: MarshalAs(UnmanagedType.I4)]
            DESKTOP_WALLPAPER_POSITION GetPosition();

            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();

            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);

            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);

            DESKTOP_WALLPAPER_POSITION GetStatus();

            bool Enable();
        }

        /// <summary>
        /// COM class for IDesktopWallpaper implementation.
        /// </summary>
        [ComImport]
        [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        public class DesktopWallpaper
        {
        }

        #endregion

        /// <summary>
        /// Checks if the system supports the per-monitor wallpaper feature.
        /// </summary>
        /// <returns>True if supported; otherwise, false.</returns>
        public static bool IsPerMonitorWallpaperSupported()
        {
            try
            {
                // Windows 8 and above support the IDesktopWallpaper interface
                if (Environment.OSVersion.Version.Major < 6 ||
                    (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor < 2))
                {
                    return false;
                }

                // Try to create the COM interface instance
                var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                uint count = wpInstance.GetMonitorDevicePathCount();
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets wallpapers for all monitors using specified paths.
        /// </summary>
        /// <param name="wallpaperPaths">Dictionary of monitor IDs and wallpaper file paths.</param>
        public static void SetWallpaperForAllMonitors(Dictionary<string, string> wallpaperPaths)
        {
            if (!IsPerMonitorWallpaperSupported())
            {
                throw new PlatformNotSupportedException("The current system does not support per-monitor wallpaper feature");
            }

            // Record start time for timeout protection
            DateTime startTime = DateTime.Now;
            const int MaxExecutionTimeMs = 5000; // Maximum execution time 5 seconds
            const int MaxRetryCount = 2;        // Maximum retry attempts

            // Validate wallpaper paths
            foreach (var path in wallpaperPaths.Values)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"Wallpaper file not found: {path}");
                }
            }

            try
            {
                var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();

                // Set position to "Fill" mode to ensure wallpaper fills the screen
                wpInstance.SetPosition(DESKTOP_WALLPAPER_POSITION.DWPOS_FILL);

                int successCount = 0;

                // Iterate through each monitor and apply retry mechanism
                foreach (var kvp in wallpaperPaths)
                {
                    string monitorId = kvp.Key;
                    string wallpaperPath = kvp.Value;

                    if (!File.Exists(wallpaperPath))
                        continue;

                    int retryCount = 0;
                    bool success = false;

                    while (!success && retryCount < MaxRetryCount)
                    {
                        try
                        {
                            // Check for timeout
                            if ((DateTime.Now - startTime).TotalMilliseconds > MaxExecutionTimeMs)
                            {
                                Console.WriteLine("Wallpaper setting operation timed out, partial completion");
                                return;
                            }

                            // Set wallpaper for the current monitor
                            wpInstance.SetWallpaper(monitorId, wallpaperPath);
                            success = true;
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            if (retryCount >= MaxRetryCount)
                            {
                                Console.WriteLine($"Failed to set wallpaper for monitor {monitorId}, reached max retry count: {ex.Message}");
                            }
                            else
                            {
                                Console.WriteLine($"Attempt to set wallpaper for monitor {monitorId} failed, retrying ({retryCount}/{MaxRetryCount}): {ex.Message}");
                                // Short delay before retrying
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                    }
                }

                Console.WriteLine($"Successfully set wallpapers for {successCount}/{wallpaperPaths.Count} monitors");
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"COM error occurred while setting per-monitor wallpapers: 0x{comEx.ErrorCode:X}, {comEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while setting per-monitor wallpapers: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves information (IDs and rectangles) for all monitors.
        /// </summary>
        /// <returns>Dictionary of monitor IDs and their rectangles.</returns>
        public static Dictionary<string, RECT> GetAllMonitorInfo()
        {
            var result = new Dictionary<string, RECT>();

            if (!IsPerMonitorWallpaperSupported())
            {
                return result;
            }

            try
            {
                var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                uint count = wpInstance.GetMonitorDevicePathCount();

                for (uint i = 0; i < count; i++)
                {
                    string monitorId = wpInstance.GetMonitorDevicePathAt(i);
                    RECT rect = wpInstance.GetMonitorRECT(monitorId);
                    result.Add(monitorId, rect);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving monitor information: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Sets a single wallpaper for all monitors in compatibility mode.
        /// </summary>
        /// <param name="wallpaperPath">The wallpaper file path.</param>
        public static void SetSingleWallpaper(string wallpaperPath)
        {
            try
            {
                if (IsPerMonitorWallpaperSupported())
                {
                    var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                    wpInstance.SetPosition(DESKTOP_WALLPAPER_POSITION.DWPOS_FILL);

                    // Set the same wallpaper for all monitors
                    uint count = wpInstance.GetMonitorDevicePathCount();
                    for (uint i = 0; i < count; i++)
                    {
                        string monitorId = wpInstance.GetMonitorDevicePathAt(i);
                        wpInstance.SetWallpaper(monitorId, wallpaperPath);
                    }
                }
                else
                {
                    // Fallback to traditional wallpaper setting
                    WallpaperSetter.Set(wallpaperPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while setting single wallpaper: {ex.Message}");
                // Fallback to traditional wallpaper setting
                WallpaperSetter.Set(wallpaperPath);
            }
        }
    }
}
