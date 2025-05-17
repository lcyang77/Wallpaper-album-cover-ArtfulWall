using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Runtime.Versioning;

namespace ArtfulWall.Utils
{
    public static class WallpaperSetter
    {
        // Define constants related to the Windows API for setting desktop wallpaper
        private const int SPI_SETDESKWALLPAPER = 20;
        private const int SPIF_UPDATEINIFILE   = 0x01;
        private const int SPIF_SENDCHANGE      = 0x02;

        // Import the SystemParametersInfo function from user32.dll to apply the wallpaper change
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(
            int uAction,
            int uParam,
            string lpvParam,
            int fuWinIni
        );

        /// <summary>
        /// Public method to set the desktop wallpaper.
        /// </summary>
        /// <param name="path">The full file path of the image to use as wallpaper.</param>
        public static void Set(string path)
        {
            // Validate that the provided path is non-empty and points to an existing file
            ValidateWallpaperPath(path);

            // Determine if the current OS is Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, perform the wallpaper update
                SetWindowsWallpaper(path);
            }
            else
            {
                // On non-Windows platforms, indicate lack of support
                throw new PlatformNotSupportedException(
                    "The current operating system does not support automatic wallpaper setting."
                );
            }
        }

        /// <summary>
        /// Internal helper to set the wallpaper on Windows platforms.
        /// </summary>
        /// <param name="path">The file path of the wallpaper image.</param>
        private static void SetWindowsWallpaper(string path)
        {
            // Invoke the native API; a return value of 0 indicates failure
            if (SystemParametersInfo(
                    SPI_SETDESKWALLPAPER,
                    0,
                    path,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
                ) == 0)
            {
                // Throw an exception if the API call fails
                throw new InvalidOperationException(
                    "Unable to set desktop wallpaper. This may be due to an operating system error or insufficient permissions."
                );
            }
        }

        /// <summary>
        /// Validates that the wallpaper path is not null or empty and that the file exists.
        /// </summary>
        /// <param name="path">The file path to validate.</param>
        private static void ValidateWallpaperPath(string path)
        {
            // Ensure the path is not null, empty, or whitespace
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
            }

            // Ensure the file exists at the specified path
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The specified file could not be found.", path);
            }
        }
    }
}
