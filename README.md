# MyWallpaper-album-cover
Wallpaper composed of multiple album covers that can be randomly changed!

MyWallpaperApp Instructions

How do I find and edit config.json
Open the file browser.
Type %APPDATA%\MyWallpaperApp in the address bar and press Enter.
Locate the config.json file in that directory and open it with a text editor such as Notepad.
Configuration item description
FolderPath: path to the image source folder. Make sure the path exists and the application has permission to access it.
DestFolder: saves the image after processing. Make sure the path exists and the application has permission to access it.
Width: The width of the image in pixels (px). Must be a positive integer.
Height: The height of the image in pixels (px). Must be a positive integer.
Rows: The number of rows in the wallpaper. Must be a positive integer.
Cols: The number of columns in the wallpaper. Must be a positive integer.
Interval: indicates the interval for updating the wallpaper, expressed in seconds. It has to be positive.

Format specification
Make sure config.json follows the following format:

{
"FolderPath": "D:\\album_cover", // Path to the folder containing the cover image Note: The number of images in the folder should exceed the number of Rows*Cols
"DestFolder": "D:\\album_cover", // Path to the destination folder where the generated wallpaper image is stored
"Width": 3840, // The width of the wallpaper (pixels)
"Height": 2160, // The height of the wallpaper (pixels)
"Rows": 6, // Number of rows in the wallpaper grid (positive integer)
"Cols": 11, // Number of columns in the wallpaper grid (positive integer)
"Interval": 10, // Basic time interval for wallpaper updates (seconds)
"MinInterval": 3, // Minimum random interval for wallpaper updates (seconds)
"MaxInterval": 10 // Maximum random interval for wallpaper updates (seconds)
}

Please replace the above paths and values according to your requirements. Due to the limited cache space allocation, setting the time parameter too small will cause the refresh effect of the initial run to be less than expected.

Questions and support
If you experience any problems with using MyWallpaperApp or require technical support, please contact LinusYang77@gmail.com




MyWallpaperApp 使用说明

如何找到和编辑 config.json
打开文件浏览器。
在地址栏输入 %APPDATA%\MyWallpaperApp 并按 Enter。
在该目录中找到 config.json 文件并用文本编辑器（如记事本）打开。
配置项说明
FolderPath: 图片的源文件夹路径。确保这个路径存在且应用程序有权限访问。
DestFolder: 图片处理后的保存路径。确保这个路径存在且应用程序有权限访问。
Width: 图片的宽度，单位为像素（px）。必须是正整数。
Height: 图片的高度，单位为像素（px）。必须是正整数。
Rows: 在壁纸中行的数量。必须是正整数。
Cols: 在壁纸中列的数量。必须是正整数。
Interval: 壁纸更新的间隔时间，单位为秒。必须是正数。

格式规范
确保 config.json 遵循以下格式：

{
    "FolderPath": "D:\\album_cover", // 存放封面图片的文件夹路径   注意：文件夹中的图片数量应当超过Rows*Cols的数量
    "DestFolder": "D:\\album_cover", // 生成的壁纸图片存放的目标文件夹路径
    "Width": 3840,                   // 壁纸的宽度（像素）
    "Height": 2160,                  // 壁纸的高度（像素）
    "Rows": 6,                       // 壁纸网格的行数（正整数）
    "Cols": 11,                      // 壁纸网格的列数（正整数）
    "Interval": 10,                   // 壁纸更新的基本时间间隔（秒）
    "MinInterval": 3,                // 壁纸更新的最小随机时间间隔（秒）
    "MaxInterval": 10                 // 壁纸更新的最大随机时间间隔（秒）
}

请根据您的需求替换以上路径和数值。由于缓存空间分配有限，对于时间参数的设定过小会导致初始运行时的刷新效果不达预期。

问题和支持
如果您在使用 MyWallpaperApp 时遇到任何问题或需要技术支持，请联系LinusYang77@gmail.com
