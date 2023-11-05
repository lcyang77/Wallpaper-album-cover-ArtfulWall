using System;
using System.Runtime.InteropServices;

public static class Wallpaper
{
    const int SPI_SETDESKWALLPAPER = 20;
    const int SPIF_UPDATEINIFILE = 0x01;
    const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    public static void Set(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空或空白。", nameof(path));
        }

        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
