using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

namespace ArtfulWall.Utils
{
    public static class DesktopWallpaperApi
    {
        #region COM Interop Definitions

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public enum DESKTOP_WALLPAPER_POSITION
        {
            DWPOS_CENTER = 0,
            DWPOS_TILE = 1,
            DWPOS_STRETCH = 2,
            DWPOS_FIT = 3,
            DWPOS_FILL = 4,
            DWPOS_SPAN = 5
        }

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

        [ComImport]
        [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        public class DesktopWallpaper
        {
        }

        #endregion

        // 检查系统是否支持每显示器壁纸功能
        public static bool IsPerMonitorWallpaperSupported()
        {
            try
            {
                // Windows 8及以上支持IDesktopWallpaper接口
                if (Environment.OSVersion.Version.Major < 6 || 
                    (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor < 2))
                {
                    return false;
                }

                // 尝试创建接口实例
                var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                uint count = wpInstance.GetMonitorDevicePathCount();
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        // 设置每个显示器的壁纸
        public static void SetWallpaperForAllMonitors(Dictionary<string, string> wallpaperPaths)
        {
            if (!IsPerMonitorWallpaperSupported())
            {
                throw new PlatformNotSupportedException("当前系统不支持每显示器壁纸功能");
            }

            // 记录开始时间，用于实现超时保护
            DateTime startTime = DateTime.Now;
            const int MaxExecutionTimeMs = 5000; // 最大执行时间5秒
            const int MaxRetryCount = 2; // 最大重试次数
            
            // 验证壁纸路径
            foreach (var path in wallpaperPaths.Values)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"壁纸文件不存在: {path}");
                }
            }

            try
            {
                var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                
                // 设置为"填充"模式，确保壁纸填满屏幕
                wpInstance.SetPosition(DESKTOP_WALLPAPER_POSITION.DWPOS_FILL);
                
                int successCount = 0;
                
                // 对每个显示器路径，使用重试机制设置壁纸
                foreach (var kvp in wallpaperPaths)
                {
                    string monitorId = kvp.Key;
                    string wallpaperPath = kvp.Value;
                    
                    if (!File.Exists(wallpaperPath))
                        continue;
                    
                    // 实现重试机制
                    int retryCount = 0;
                    bool success = false;
                    
                    while (!success && retryCount < MaxRetryCount)
                    {
                        try
                        {
                            // 检查是否超时
                            if ((DateTime.Now - startTime).TotalMilliseconds > MaxExecutionTimeMs)
                            {
                                Console.WriteLine("设置壁纸操作超时，已完成部分设置");
                                return;
                            }
                            
                            wpInstance.SetWallpaper(monitorId, wallpaperPath);
                            success = true;
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            retryCount++;
                            if (retryCount >= MaxRetryCount)
                            {
                                Console.WriteLine($"设置显示器 {monitorId} 壁纸失败，已达最大重试次数: {ex.Message}");
                            }
                            else
                            {
                                Console.WriteLine($"尝试设置显示器 {monitorId} 壁纸失败，正在重试 ({retryCount}/{MaxRetryCount}): {ex.Message}");
                                // 短暂延迟后重试
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                    }
                }
                
                Console.WriteLine($"成功设置了 {successCount}/{wallpaperPaths.Count} 个显示器的壁纸");
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"设置每显示器壁纸时出现COM错误: 0x{comEx.ErrorCode:X}, {comEx.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置每显示器壁纸时出错: {ex.Message}");
                throw;
            }
        }

        // 获取所有显示器的ID
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
                Console.WriteLine($"获取显示器信息时出错: {ex.Message}");
            }
            
            return result;
        }

        // 兼容模式下设置单一壁纸
        public static void SetSingleWallpaper(string wallpaperPath)
        {
            try
            {
                if (IsPerMonitorWallpaperSupported())
                {
                    var wpInstance = (IDesktopWallpaper)new DesktopWallpaper();
                    wpInstance.SetPosition(DESKTOP_WALLPAPER_POSITION.DWPOS_FILL);
                    
                    // 设置所有显示器使用相同壁纸
                    uint count = wpInstance.GetMonitorDevicePathCount();
                    for (uint i = 0; i < count; i++)
                    {
                        string monitorId = wpInstance.GetMonitorDevicePathAt(i);
                        wpInstance.SetWallpaper(monitorId, wallpaperPath);
                    }
                }
                else
                {
                    // 回退到传统壁纸设置
                    WallpaperSetter.Set(wallpaperPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置单一壁纸时出错: {ex.Message}");
                // 回退到传统壁纸设置
                WallpaperSetter.Set(wallpaperPath);
            }
        }
    }
} 