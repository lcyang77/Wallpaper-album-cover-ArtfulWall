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
    /// <summary>
    /// Represents a single physical display/monitor and its key characteristics.
    /// </summary>
    public class DisplayInfo
    {
        public int DisplayNumber { get; set; }
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public string DeviceName { get; set; }

        // Physical resolution – the monitor’s real pixel dimensions
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>DPI scaling factor (1 = 100 %).</summary>
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
            return $"Display {DisplayNumber + 1}: {Width}x{Height}, " +
                   $"{(IsPrimary ? "Primary" : "Secondary")}, " +
                   $"DPI: {DpiScaling:F2}, Orientation: {Orientation}";
        }
    }

    /// <summary>
    /// Helper class for querying monitors, DPI scaling, orientation, and reacting to
    /// display-configuration changes.
    /// </summary>
    public static class DisplayManager
    {
        #region Win32 / Shell / GDI imports

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(
            IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(
            string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(
            IntPtr hprocess, out int value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        // Windows 10 (1607+) API
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr hwnd, uint dwFlags);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DMDO_DEFAULT = 0;
        private const int DMDO_90 = 1;
        private const int DMDO_180 = 2;
        private const int DMDO_270 = 3;
        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        // DPI-type constants
        private const int MDT_EFFECTIVE_DPI = 0;
        private const int MDT_ANGULAR_DPI = 1;
        private const int MDT_RAW_DPI = 2;
        private const int MDT_DEFAULT = MDT_EFFECTIVE_DPI;

        // Monitor-from-window flag
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;                    // MONITORINFOF_PRIMARY = 0x1
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

        private delegate bool MonitorEnumProc(
            IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        #endregion Win32 imports

        /// <summary>
        /// Obtain DPI scaling for a given monitor by inspecting per-monitor registry
        /// keys. Returns 0 if not found.
        /// </summary>
        private static float GetDisplayScaleFromRegistry(string deviceName)
        {
            try
            {
                // Extract the pure ID portion from a device path such as “\\.\DISPLAY1”
                string displayId = deviceName.Replace(@"\", "").Replace(".", "");

                // Try the primary PerMonitorSettings key
                using (var scaleKey =
                       Registry.CurrentUser.OpenSubKey(
                           @"Control Panel\Desktop\PerMonitorSettings\" + displayId))
                {
                    if (scaleKey != null)
                    {
                        var scaleValue = scaleKey.GetValue("DpiValue");
                        if (scaleValue != null)
                        {
                            // DpiValue: –1 = 100 %, –2 = 200 %, etc.
                            int dpiSetting = Convert.ToInt32(scaleValue);
                            if (dpiSetting < 0)
                            {
                                float scale = Math.Abs(dpiSetting) / 100.0f;
                                Console.WriteLine(
                                    $"Display {displayId} DPI scaling from registry: {scale:F2}");
                                return scale;
                            }
                        }
                    }

                    // Try a legacy/alternate registry path
                    string adapterId = string.Empty;
                    string sourceId = string.Empty;

                    // Discover adapter/source IDs that map to this display
                    using (var key =
                           Registry.LocalMachine.OpenSubKey(
                               @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration"))
                    {
                        if (key != null)
                        {
                            foreach (string adapterKey in key.GetSubKeyNames())
                            {
                                using (var adapter = key.OpenSubKey(adapterKey))
                                {
                                    if (adapter == null) continue;

                                    foreach (string sourceKey in adapter.GetSubKeyNames())
                                    {
                                        using (var source = adapter.OpenSubKey(sourceKey))
                                        {
                                            if (source == null) continue;

                                            string devicePath =
                                                source.GetValue("DevicePath") as string;

                                            if (!string.IsNullOrEmpty(devicePath) &&
                                                devicePath.Contains(displayId))
                                            {
                                                adapterId = adapterKey;
                                                sourceId = sourceKey;
                                                break;
                                            }
                                        }
                                        if (!string.IsNullOrEmpty(adapterId)) break;
                                    }
                                }
                                if (!string.IsNullOrEmpty(adapterId)) break;
                            }
                        }
                    }

                    // If adapter/source IDs were found, query their PerMonitorSettings key
                    if (!string.IsNullOrEmpty(adapterId) &&
                        !string.IsNullOrEmpty(sourceId))
                    {
                        string scalePath =
                            $@"Control Panel\Desktop\PerMonitorSettings\{adapterId}_{sourceId}";
                        using (var scalePathKey =
                               Registry.CurrentUser.OpenSubKey(scalePath))
                        {
                            if (scalePathKey != null)
                            {
                                var scaleValue = scalePathKey.GetValue("DpiValue");
                                if (scaleValue != null)
                                {
                                    int dpiSetting = Convert.ToInt32(scaleValue);
                                    if (dpiSetting < 0)
                                    {
                                        float scale = Math.Abs(dpiSetting) / 100.0f;
                                        Console.WriteLine(
                                            $"Display {displayId} DPI scaling "
                                            + $"from advanced registry: {scale:F2}");
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
                Console.WriteLine(
                    $"Failed to get display scaling setting from registry: {ex.Message}");
            }

            return 0; // Not found
        }

        /// <summary>
        /// Enumerate all detected monitors. Guarantees at least one result by returning
        /// a default 1920×1080 primary display if enumeration fails.
        /// </summary>
        public static List<DisplayInfo> GetDisplays()
        {
            var result = new List<DisplayInfo>();
            int displayNumber = 0;

            try
            {
                // Run detection on a background task with a 5-s timeout
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(5000);

                Task<List<DisplayInfo>> detectionTask = Task.Run(() =>
                {
                    var displays = new List<DisplayInfo>();

                    // Attempt to enable per-monitor DPI awareness (Win 8.1+)
                    try
                    {
                        if (Environment.OSVersion.Version.Major >= 10 ||
                            (Environment.OSVersion.Version.Major == 6 &&
                             Environment.OSVersion.Version.Minor >= 3))
                        {
                            // PROCESS_PER_MONITOR_DPI_AWARE = 2
                            SetProcessDpiAwareness(2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to set DPI awareness mode: {ex.Message}");
                    }

                    bool enumSuccess = false;
                    try
                    {
                        enumSuccess = EnumDisplayMonitors(
                            IntPtr.Zero,
                            IntPtr.Zero,
                            (IntPtr hMonitor, IntPtr hdcMonitor,
                                ref RECT lprcMonitor, IntPtr dwData) =>
                            {
                                if (cts.Token.IsCancellationRequested)
                                    return false; // Cancel

                                var mi = new MONITORINFOEX
                                {
                                    cbSize = Marshal.SizeOf(typeof(MONITORINFOEX))
                                };

                                if (!GetMonitorInfo(hMonitor, ref mi))
                                    return true; // Continue enumeration

                                var bounds = new Rectangle(
                                    lprcMonitor.left,
                                    lprcMonitor.top,
                                    lprcMonitor.right - lprcMonitor.left,
                                    lprcMonitor.bottom - lprcMonitor.top);

                                string deviceName =
                                    new string(mi.szDevice).TrimEnd('\0');

                                // Orientation and per-monitor DPI
                                var orientation = GetDisplayOrientation(deviceName);
                                float dpiScaling = GetDisplayScaleFromRegistry(deviceName);

                                // Fall back to API methods if registry search failed
                                if (dpiScaling <= 0)
                                {
                                    dpiScaling = GetDpiScaling(hMonitor);

                                    // Last-ditch fallback to global LogPixels
                                    if (dpiScaling <= 0)
                                    {
                                        using var key =
                                            Registry.CurrentUser.OpenSubKey(
                                                @"Control Panel\Desktop");
                                        if (key?.GetValue("LogPixels") is int logPixels)
                                            dpiScaling = logPixels / 96.0f;

                                        if (dpiScaling <= 0)
                                            dpiScaling = 1.0f; // Default
                                    }
                                }

                                // Bounds are already physical pixels on Win 8.1+
                                int width = bounds.Width;
                                int height = bounds.Height;

                                displays.Add(new DisplayInfo
                                {
                                    DisplayNumber = displayNumber++,
                                    Bounds = bounds,
                                    IsPrimary = (mi.dwFlags & 0x1) != 0,
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
                        Console.WriteLine($"Error occurred while enumerating displays: {ex.Message}");
                        enumSuccess = false;
                    }

                    // Fallback: at least one screen via Screen class
                    if (!enumSuccess || displays.Count == 0)
                    {
                        try
                        {
                            var primary = Screen.PrimaryScreen;
                            if (primary != null)
                            {
                                displays.Add(new DisplayInfo
                                {
                                    DisplayNumber = 0,
                                    Bounds = primary.Bounds,
                                    IsPrimary = true,
                                    DeviceName = @"\\.\DISPLAY1",
                                    Width = primary.Bounds.Width,
                                    Height = primary.Bounds.Height,
                                    Orientation = DisplayInfo.OrientationType.Landscape,
                                    DpiScaling = 1.0f
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"Failed to get display info using Screen.PrimaryScreen: {ex.Message}");
                        }
                    }

                    return displays;
                }, cts.Token);

                // Await task completion or timeout
                bool taskCompleted =
                    Task.WaitAny(new[] { detectionTask }, 5000, cts.Token) >= 0;

                result = taskCompleted &&
                         detectionTask.Status == TaskStatus.RanToCompletion
                         ? detectionTask.Result
                         : new List<DisplayInfo>();
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Display detection operation was cancelled or timed out");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetDisplays method failed: {ex.Message}");
            }

            // Absolute fallback – inject a 1080p primary display
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

                Console.WriteLine(
                    "No displays detected, using default display configuration");
            }

            // Dump display summary to console
            Console.WriteLine("Detected displays:");
            foreach (var display in result)
                Console.WriteLine($"  {display}");

            return result;
        }

        /// <summary>
        /// Query the current orientation of the specified device.
        /// </summary>
        private static DisplayInfo.OrientationType GetDisplayOrientation(
            string deviceName)
        {
            var dm = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
            };

            if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
            {
                return dm.dmDisplayOrientation switch
                {
                    DMDO_DEFAULT => DisplayInfo.OrientationType.Landscape,
                    DMDO_90 => DisplayInfo.OrientationType.Portrait,
                    DMDO_180 => DisplayInfo.OrientationType.LandscapeFlipped,
                    DMDO_270 => DisplayInfo.OrientationType.PortraitFlipped,
                    _ => DisplayInfo.OrientationType.Landscape
                };
            }

            return DisplayInfo.OrientationType.Landscape; // Default
        }

        /// <summary>
        /// Determine DPI scaling for a monitor using multiple fallbacks (registry,
        /// per-monitor, window, GetDeviceCaps, etc.).
        /// </summary>
        private static float GetDpiScaling(IntPtr hMonitor)
        {
            float scaling = 1.0f;
            bool success = false;

            // 1) Registry: WindowMetrics \ AppliedDPI
            try
            {
                using var key =
                    Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics");
                if (key?.GetValue("AppliedDPI") is int dpi)
                {
                    scaling = dpi / 96.0f;
                    Console.WriteLine(
                        $"DPI scaling obtained from registry: {scaling:F2} (DPI value: {dpi})");
                    success = true;
                }

                // 2) System display settings (Windows 10+)
                if (!success && Environment.OSVersion.Version.Major >= 10)
                {
                    IntPtr activeHandle = Form.ActiveForm?.Handle ?? IntPtr.Zero;
                    if (activeHandle == IntPtr.Zero)
                    {
                        // Grab any open form handle
                        foreach (Form f in Application.OpenForms)
                        {
                            activeHandle = f.Handle;
                            if (activeHandle != IntPtr.Zero) break;
                        }
                    }

                    if (activeHandle != IntPtr.Zero)
                    {
                        try
                        {
                            uint dpiWin = GetDpiForWindow(activeHandle);
                            if (dpiWin > 0)
                            {
                                scaling = dpiWin / 96.0f;
                                Console.WriteLine(
                                    $"DPI scaling obtained from active window: {scaling:F2} (DPI {dpiWin})");
                                success = true;
                            }
                            else
                            {
                                // Same monitor as requested?
                                IntPtr winMon = MonitorFromWindow(
                                    activeHandle, MONITOR_DEFAULTTONEAREST);
                                if (winMon != IntPtr.Zero && winMon == hMonitor)
                                {
                                    uint dpiX, dpiY;
                                    if (GetDpiForMonitor(
                                            hMonitor, MDT_EFFECTIVE_DPI,
                                            out dpiX, out dpiY) == 0)
                                    {
                                        scaling = dpiX / 96.0f;
                                        Console.WriteLine(
                                            $"DPI scaling obtained from window monitor: {scaling:F2} "
                                            + $"(DPI {dpiX}×{dpiY})");
                                        success = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to get DPI from window: {ex.Message}");
                        }
                    }
                }

                // 3) GetDpiForMonitor (Windows 8.1+)
                if (!success &&
                    (Environment.OSVersion.Version.Major > 6 ||
                     (Environment.OSVersion.Version.Major == 6 &&
                      Environment.OSVersion.Version.Minor >= 3)))
                {
                    uint dpiX, dpiY;
                    if (GetDpiForMonitor(
                            hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0)
                    {
                        scaling = dpiX / 96.0f;
                        Console.WriteLine(
                            $"DPI scaling obtained using GetDpiForMonitor: {scaling:F2} "
                            + $"(DPI {dpiX}×{dpiY})");
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get DPI scaling (advanced API): {ex.Message}");
            }

            // 4) Legacy fallback – GetDeviceCaps on the primary screen
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
                            Console.WriteLine(
                                $"DPI scaling obtained using GetDeviceCaps: {scaling:F2} (DPI {dpiX})");
                            success = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"Failed to get DPI scaling (legacy API): {ex.Message}");
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, hdc);
                    }
                }
            }

            // 5) Snap to common Windows scaling factors (100 %, 125 %, …)
            if (success)
            {
                float[] commonScales =
                    { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.25f, 2.5f, 3.0f, 3.5f, 4.0f };
                float closest = commonScales
                    .OrderBy(x => Math.Abs(x - scaling)).First();

                if (Math.Abs(closest - scaling) < 0.03f)
                {
                    Console.WriteLine($"Normalized DPI scaling: {scaling:F2} → {closest:F2}");
                    scaling = closest;
                }
            }
            else
            {
                Console.WriteLine("All DPI detection methods failed, using default: 1.0");
                scaling = 1.0f;
            }

            return scaling;
        }

        // Event fired when Windows announces a display-settings change
        public static event EventHandler DisplaySettingsChanged;

        static DisplayManager()
        {
            // Wire up the system event
            SystemEvents.DisplaySettingsChanged += (s, e) =>
            {
                DisplaySettingsChanged?.Invoke(s, e);
            };
        }
    }
}
