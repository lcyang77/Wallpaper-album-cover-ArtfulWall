using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace ArtfulWall.Utils
{
    public class DisplayInfo
    {
        public int DisplayNumber { get; set; }
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public string DeviceName { get; set; }
        // 物理分辨率 - 显示器实际的像素数量
        public int Width { get; set; }
        public int Height { get; set; }
        public float DpiScaling { get; set; }
        public OrientationType Orientation { get; set; }
        
        public enum OrientationType
        {
            Landscape = 0,
            Portrait = 90,
            LandscapeFlipped = 180,
            PortraitFlipped = 270
        }

        public override string ToString()
        {
            return $"显示器 {DisplayNumber + 1}: {Width}x{Height}, {(IsPrimary ? "主显示器" : "副显示器")}, DPI: {DpiScaling:F2}, 方向: {Orientation}";
        }
    }

    public static class DisplayManager
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr hprocess, out int value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        // Windows 10 1607+ API
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DMDO_DEFAULT = 0;
        private const int DMDO_90 = 1;
        private const int DMDO_180 = 2;
        private const int DMDO_270 = 3;
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;
        
        // DPI types
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int MDT_ANGULAR_DPI = 1;
        private const int MDT_RAW_DPI = 2;
        private const int MDT_DEFAULT = MDT_EFFECTIVE_DPI;
        
        // Monitor from Window constants
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public char[] szDevice;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }
        
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        
        #endregion

        // 从Windows设置中获取某个显示器的DPI缩放设置
        private static float GetDisplayScaleFromRegistry(string deviceName)
        {
            try
            {
                                 // 从设备名称中提取ID部分
                 string displayId = deviceName;
                 displayId = displayId.Replace(@"\", "");
                 displayId = displayId.Replace(".", "");
                
                // 在注册表中查找此显示器的设置
                using (var scaleKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\PerMonitorSettings\" + displayId))
                {
                    if (scaleKey != null)
                    {
                        var scaleValue = scaleKey.GetValue("DpiValue");
                        if (scaleValue != null)
                        {
                            // DpiValue通常是-1表示100%，-2表示200%，等等
                            int dpiSetting = Convert.ToInt32(scaleValue);
                            if (dpiSetting < 0)
                            {
                                // 转换为缩放比例
                                float scale = Math.Abs(dpiSetting) / 100.0f;
                                Console.WriteLine($"显示器 {displayId} 从注册表获取到DPI缩放: {scale:F2}");
                                return scale;
                            }
                        }
                    }
                    
                    // 尝试查找另一种格式的注册表键
                    string adapterId = string.Empty;
                    string sourceId = string.Empty;
                    
                    // 尝试从 DISPLAY1 获取显示器内部ID
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration"))
                    {
                        if (key != null)
                        {
                            foreach (string adapterKey in key.GetSubKeyNames())
                            {
                                using (var adapter = key.OpenSubKey(adapterKey))
                                {
                                    if (adapter != null)
                                    {
                                        foreach (string sourceKey in adapter.GetSubKeyNames())
                                        {
                                            using (var source = adapter.OpenSubKey(sourceKey))
                                            {
                                                if (source != null)
                                                {
                                                    string activeSize = source.GetValue("ActiveSize") as string;
                                                    string devicePath = source.GetValue("DevicePath") as string;
                                                    
                                                    if (!string.IsNullOrEmpty(devicePath) && 
                                                        devicePath.Contains(displayId))
                                                    {
                                                        adapterId = adapterKey;
                                                        sourceId = sourceKey;
                                                        break;
                                                    }
                                                }
                                            }
                                            if (!string.IsNullOrEmpty(adapterId)) break;
                                        }
                                    }
                                    if (!string.IsNullOrEmpty(adapterId)) break;
                                }
                            }
                        }
                    }
                    
                    // 如果找到了适配器和源ID，查找DPI设置
                    if (!string.IsNullOrEmpty(adapterId) && !string.IsNullOrEmpty(sourceId))
                    {
                        string scalePath = $@"Control Panel\Desktop\PerMonitorSettings\{adapterId}_{sourceId}";
                        using (var scalePathKey = Registry.CurrentUser.OpenSubKey(scalePath))
                        {
                            if (scalePathKey != null)
                            {
                                var scaleValue = scalePathKey.GetValue("DpiValue");
                                if (scaleValue != null)
                                {
                                    // DpiValue通常是-1表示100%，-2表示200%，等等
                                    int dpiSetting = Convert.ToInt32(scaleValue);
                                    if (dpiSetting < 0)
                                    {
                                        // 转换为缩放比例
                                        float scale = Math.Abs(dpiSetting) / 100.0f;
                                        Console.WriteLine($"显示器 {displayId} 从高级注册表获取到DPI缩放: {scale:F2}");
                                        return scale;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"从注册表获取显示器缩放设置失败: {ex.Message}");
            }
            
            return 0; // 返回0表示未找到
        }

        // 获取所有显示器信息
        public static List<DisplayInfo> GetDisplays()
        {
            var result = new List<DisplayInfo>();
            int displayNumber = 0;
            
            try
            {
                // 定义一个任务来完成显示器检测，使用 CancellationToken 以支持取消
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(5000); // 设置5秒超时
                
                Task<List<DisplayInfo>> detectionTask = Task.Run(() => 
                {
                    var displays = new List<DisplayInfo>();
                    
                    // 先尝试启用高DPI感知
                    try
                    {
                        if (Environment.OSVersion.Version.Major >= 10 ||
                            (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor >= 3))
                        {
                            // 对于Windows 8.1+，尝试设置为Per-Monitor DPI感知
                            SetProcessDpiAwareness(2); // 2 = PROCESS_PER_MONITOR_DPI_AWARE
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"设置DPI感知模式失败: {ex.Message}");
                    }
                    
                    bool enumSuccess = false;
                    try
                    {
                        // 尝试枚举显示器，使用超时保护
                        enumSuccess = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, 
                            (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                            {
                                if (cts.Token.IsCancellationRequested)
                                    return false; // 取消操作
                                    
                                var mi = new MONITORINFOEX();
                                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                                
                                if (!GetMonitorInfo(hMonitor, ref mi))
                                    return true; // 继续枚举
                                
                                var bounds = new Rectangle(
                                    lprcMonitor.left, 
                                    lprcMonitor.top, 
                                    lprcMonitor.right - lprcMonitor.left, 
                                    lprcMonitor.bottom - lprcMonitor.top);
    
                                string deviceName = new string(mi.szDevice).TrimEnd('\0');
    
                                // 获取显示方向
                                var orientation = GetDisplayOrientation(deviceName);
                                
                                // 首先尝试从注册表获取每个显示器的实际DPI缩放设置
                                float dpiScaling = GetDisplayScaleFromRegistry(deviceName);
                                
                                // 如果从注册表获取失败，使用API方法
                                if (dpiScaling <= 0)
                                {
                                    dpiScaling = GetDpiScaling(hMonitor);
                                    
                                    // 如果无法获取准确的DPI缩放，尝试从全局设置获取
                                    if (dpiScaling <= 0)
                                    {
                                        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                                        {
                                            if (key != null)
                                            {
                                                var logPixels = key.GetValue("LogPixels");
                                                if (logPixels != null)
                                                {
                                                    dpiScaling = Convert.ToInt32(logPixels) / 96.0f;
                                                }
                                            }
                                        }
                                        
                                        if (dpiScaling <= 0)
                                        {
                                            dpiScaling = 1.0f; // 默认值
                                        }
                                    }
                                }

                                // 正确计算物理分辨率（对于Windows 8.1+来说，bounds已经是物理分辨率）
                                int width = bounds.Width;
                                int height = bounds.Height;
                                
                                var num = displayNumber++;
                                displays.Add(new DisplayInfo {
                                    DisplayNumber = num,
                                    Bounds = bounds,
                                    IsPrimary = (mi.dwFlags & 0x1) != 0, // MONITORINFOF_PRIMARY
                                    DeviceName = deviceName,
                                    Width = width,
                                    Height = height,
                                    Orientation = orientation,
                                    DpiScaling = dpiScaling
                                });
                                
                                return true;
                            }, 
                            IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"枚举显示器时发生错误: {ex.Message}");
                        enumSuccess = false;
                    }
                    
                    if (!enumSuccess || displays.Count == 0)
                    {
                        // 尝试使用屏幕类获取至少一个显示器信息
                        try
                        {
                            var primaryScreen = Screen.PrimaryScreen;
                            if (primaryScreen != null)
                            {
                                displays.Add(new DisplayInfo
                                {
                                    DisplayNumber = 0,
                                    Bounds = primaryScreen.Bounds,
                                    IsPrimary = true,
                                    DeviceName = @"\\.\DISPLAY1",
                                    Width = primaryScreen.Bounds.Width,
                                    Height = primaryScreen.Bounds.Height,
                                    Orientation = DisplayInfo.OrientationType.Landscape,
                                    DpiScaling = 1.0f
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"使用Screen.PrimaryScreen获取显示器信息失败: {ex.Message}");
                        }
                    }
                    
                    return displays;
                }, cts.Token);
                
                try
                {
                    // 等待任务完成或超时
                    bool taskCompleted = Task.WaitAny(new[] { detectionTask }, 5000, cts.Token) >= 0;
                    result = taskCompleted && detectionTask.Status == TaskStatus.RanToCompletion ? detectionTask.Result : new List<DisplayInfo>();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("显示器检测操作已取消或超时");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetDisplays方法发生错误: {ex.Message}");
            }
            
            // 如果没有检测到显示器，添加一个默认显示器
            if (result.Count == 0)
            {
                result.Add(new DisplayInfo
                {
                    DisplayNumber = 0,
                    Bounds = new Rectangle(0, 0, 1920, 1080),
                    IsPrimary = true,
                    DeviceName = @"\\.\DISPLAY1",
                    Width = 1920,
                    Height = 1080,
                    Orientation = DisplayInfo.OrientationType.Landscape,
                    DpiScaling = 1.0f
                });
                
                Console.WriteLine("未检测到显示器，使用默认显示器配置");
            }
            
            // 打印所有显示器信息
            Console.WriteLine("检测到显示器:");
            foreach (var display in result)
            {
                Console.WriteLine($"  {display}");
            }
            
            return result;
        }

        // 获取显示器的方向
        private static DisplayInfo.OrientationType GetDisplayOrientation(string deviceName)
        {
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            
            if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            {
                switch (dm.dmDisplayOrientation)
                {
                    case DMDO_DEFAULT:
                        return DisplayInfo.OrientationType.Landscape;
                    case DMDO_90:
                        return DisplayInfo.OrientationType.Portrait;
                    case DMDO_180:
                        return DisplayInfo.OrientationType.LandscapeFlipped;
                    case DMDO_270:
                        return DisplayInfo.OrientationType.PortraitFlipped;
                }
            }
            
            return DisplayInfo.OrientationType.Landscape; // 默认为横向
        }

        // 获取显示器的DPI缩放
        private static float GetDpiScaling(IntPtr hMonitor)
        {
            float scaling = 1.0f;
            bool success = false;
            
            // 尝试多种方法获取DPI缩放
            try
            {
                // 1. 最准确的方法：直接从Windows注册表获取显示缩放设置
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
                {
                    if (key != null)
                    {
                        var dpiValue = key.GetValue("AppliedDPI");
                        if (dpiValue != null)
                        {
                            int dpi = Convert.ToInt32(dpiValue);
                            scaling = dpi / 96.0f;
                            Console.WriteLine($"从注册表获取到DPI缩放: {scaling:F2} (DPI值: {dpi})");
                            success = true;
                        }
                    }
                }
                
                // 2. 从系统显示设置获取缩放
                if (!success && Environment.OSVersion.Version.Major >= 10)
                {
                    // Windows 10及以上，尝试获取活动窗口的DPI
                    var activeHandle = Form.ActiveForm?.Handle ?? IntPtr.Zero;
                    if (activeHandle == IntPtr.Zero)
                    {
                        // 尝试获取任何窗口句柄
                        foreach (Form form in Application.OpenForms)
                        {
                            activeHandle = form.Handle;
                            if (activeHandle != IntPtr.Zero) break;
                        }
                    }
                    
                    if (activeHandle != IntPtr.Zero)
                    {
                        try
                        {
                            uint dpi = GetDpiForWindow(activeHandle);
                            if (dpi > 0)
                            {
                                scaling = dpi / 96.0f;
                                Console.WriteLine($"从活动窗口获取到DPI缩放: {scaling:F2} (DPI值: {dpi})");
                                success = true;
                            }
                            else
                            {
                                // 获取窗口对应的显示器
                                IntPtr windowMonitor = MonitorFromWindow(activeHandle, MONITOR_DEFAULTTONEAREST);
                                if (windowMonitor != IntPtr.Zero && windowMonitor == hMonitor)
                                {
                                    // 尝试使用GetDpiForMonitor获取
                                    uint dpiX, dpiY;
                                    if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                                    {
                                        scaling = dpiX / 96.0f;
                                        Console.WriteLine($"从窗口监视器获取到DPI缩放: {scaling:F2} (DPI值: {dpiX}x{dpiY})");
                                        success = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"从窗口获取DPI失败: {ex.Message}");
                        }
                    }
                }
                
                // 3. 使用Windows 8.1+的API
                if (!success && (Environment.OSVersion.Version.Major > 6 || 
                    (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor >= 3)))
                {
                    uint dpiX, dpiY;
                    if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                    {
                        scaling = dpiX / 96.0f;
                        Console.WriteLine($"使用GetDpiForMonitor获取到DPI缩放: {scaling:F2} (DPI值: {dpiX}x{dpiY})");
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取DPI缩放失败 (高级API): {ex.Message}");
            }

            // 4. 回退方法：从主屏幕获取DPI
            if (!success)
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    try
                    {
                        int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                        if (dpiX > 0)
                        {
                            scaling = dpiX / 96.0f;
                            Console.WriteLine($"使用GetDeviceCaps获取到DPI缩放: {scaling:F2} (DPI值: {dpiX})");
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"获取DPI缩放失败 (传统API): {ex.Message}");
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, hdc);
                    }
                }
            }

            // 5. 检查常用的缩放值并将其规范化 (100%, 125%, 150%, 175%, 200%, 250%, 300%, etc)
            if (success)
            {
                // 将缩放值规范化为Windows常用的缩放比例
                float[] commonScales = { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.25f, 2.5f, 3.0f, 3.5f, 4.0f };
                float closestScale = commonScales.OrderBy(x => Math.Abs(x - scaling)).First();
                
                // 如果接近常用比例（误差在3%以内），则使用常用比例
                if (Math.Abs(closestScale - scaling) < 0.03)
                {
                    Console.WriteLine($"规范化DPI缩放: {scaling:F2} -> {closestScale:F2}");
                    scaling = closestScale;
                }
            }
            else
            {
                Console.WriteLine("所有DPI检测方法都失败，使用默认值: 1.0");
                scaling = 1.0f; // 默认缩放
            }
            
            return scaling;
        }

        // 注册显示器变更事件
        public static event EventHandler DisplaySettingsChanged;

        static DisplayManager()
        {
            // 订阅系统的显示设置变更事件
            SystemEvents.DisplaySettingsChanged += (s, e) => 
            {
                DisplaySettingsChanged?.Invoke(s, e);
            };
        }
    }
} 