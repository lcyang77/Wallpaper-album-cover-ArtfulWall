---

# ArtfulWall 壁纸自动更新程序

## 项目简介
ArtfulWall 是一款 Windows 平台下的多显示器智能壁纸自动更换程序。支持高分辨率、多显示器、DPI 缩放、壁纸网格拼贴、系统托盘交互、配置热更新、配置备份恢复等高级特性。适合追求桌面美观与个性化的用户。

## 主要特性
- **系统托盘运行**：后台驻留，右键菜单快速访问所有功能。
- **多显示器支持**：可为每个显示器生成独立壁纸，自动适配分辨率与方向。
- **DPI 缩放适配**：自动检测并适配各显示器的 DPI 缩放，保证壁纸清晰。
- **壁纸网格拼贴**：支持自定义行列数，将多张图片拼贴为一张壁纸。
- **壁纸切换间隔**：可配置最小/最大切换间隔，支持随机时间。
- **壁纸资源管理**：一键打开图片文件夹，支持多种图片格式（jpg/png/bmp）。
- **配置编辑器**：内置可视化配置编辑器，支持多显示器独立设置。
- **配置备份与恢复**：自动备份配置文件，防止误操作丢失。
- **开机自启动**：一键设置/取消随 Windows 启动。
- **高性能图像缓存**：内置 LRU 缓存与异步加载，提升大图/多图性能。

## 目录结构
- `Core/Program.cs`：主入口，托盘与主循环、配置加载、业务调度。
- `Services/WallpaperUpdater.cs`：壁纸生成与切换核心，支持多显示器、DPI、网格拼贴。
- `Services/ImageManager.cs`：高性能图像缓存与异步加载。
- `Models/Configuration.cs`：全局与多显示器配置模型。
- `Models/Grid.cs`：壁纸网格单元，负责图片绘制。
- `UI/ConfigEditorForm.cs`：可视化配置编辑器，支持热更新。
- `Utils/DisplayManager.cs`：多显示器与 DPI 检测。
- `Utils/DesktopWallpaperApi.cs`：系统级壁纸设置（支持每显示器壁纸）。
- `Utils/WallpaperSetter.cs`：传统壁纸设置兼容。
- `config.json`：主配置文件，支持手动与可视化编辑。

## 配置说明
- 支持全局与每显示器独立配置（分辨率、网格、DPI）。
- 典型配置字段（详见 `config.json`）：
  - `FolderPath`：壁纸图片文件夹
  - `Width`/`Height`：壁纸分辨率
  - `Rows`/`Cols`：拼贴网格
  - `MinInterval`/`MaxInterval`：切换间隔（秒）
  - `Mode`：`PerMonitor`（多显示器独立）或 `Single`（单一壁纸）
  - `MonitorConfigurations`：每显示器独立设置（可选）

## 使用方法
1. 启动程序后，系统托盘出现 ArtfulWall 图标。
2. 右键菜单可设置开机自启、编辑配置、打开图片文件夹、退出等。
3. 编辑配置后可选择立即生效或下次启动生效。
4. 支持多显示器、DPI 缩放、壁纸拼贴等高级自定义。

## 系统要求
- Windows 10/11
- .NET 7.0+ Runtime
- 支持多显示器与高分辨率

## 安装与运行
1. 下载并解压安装包。
2. 运行 `ArtfulWall.exe`，首次启动自动生成配置文件。
3. 可通过托盘菜单或 `config.json` 进行个性化设置。

## 高级说明
- 自动检测并适配显示器变更、DPI 改变等系统事件。
- 配置文件自动备份，异常时自动恢复。
- 支持命令行参数与手动编辑配置。

---

# Wallpaper Auto-Updater (ArtfulWall)

## Overview
ArtfulWall is a powerful, multi-monitor wallpaper auto-updater for Windows. It features high-res support, DPI scaling, grid collage, tray UI, live config editing, backup/restore, and robust error handling.

## Key Features
- **System tray app**: Lightweight, easy access to all features.
- **Multi-monitor support**: Independent wallpaper per monitor, auto resolution/orientation adaptation.
- **DPI scaling**: Detects and adapts to each monitor's DPI for crisp images.
- **Grid collage**: Customizable rows/columns, collage multiple images into one wallpaper.
- **Configurable intervals**: Randomized min/max wallpaper change intervals.
- **Resource management**: One-click open image folder, supports jpg/png/bmp.
- **Visual config editor**: Built-in, supports per-monitor settings.
- **Backup & restore**: Auto backup of config, easy recovery.
- **Auto-start**: One-click enable/disable on Windows boot.
- **High-performance image cache**: LRU cache, async loading for large/many images.

## Directory Structure
- `Core/Program.cs`: Main entry, tray, config loading, business logic.
- `Services/WallpaperUpdater.cs`: Wallpaper generation, multi-monitor, DPI, grid logic.
- `Services/ImageManager.cs`: High-performance image cache, async loading.
- `Models/Configuration.cs`: Global and per-monitor config model.
- `Models/Grid.cs`: Wallpaper grid unit, image drawing.
- `UI/ConfigEditorForm.cs`: Visual config editor, hot reload.
- `Utils/DisplayManager.cs`: Multi-monitor and DPI detection.
- `Utils/DesktopWallpaperApi.cs`: System-level wallpaper setting (per-monitor support).
- `Utils/WallpaperSetter.cs`: Legacy wallpaper setting fallback.
- `config.json`: Main config file, editable via UI or manually.

## Configuration
- Supports both global and per-monitor settings (resolution, grid, DPI).
- Typical config fields (see `config.json`):
  - `FolderPath`: Image folder
  - `Width`/`Height`: Wallpaper resolution
  - `Rows`/`Cols`: Grid collage
  - `MinInterval`/`MaxInterval`: Change interval (seconds)
  - `Mode`: `PerMonitor` (multi-monitor) or `Single` (single wallpaper)
  - `MonitorConfigurations`: Per-monitor overrides (optional)

## Usage
1. Launch the app, ArtfulWall tray icon appears.
2. Right-click tray icon for auto-start, config editor, open image folder, exit, etc.
3. Edit config via UI or `config.json`, apply immediately or on next start.
4. Supports multi-monitor, DPI scaling, grid collage, and more.

## System Requirements
- Windows 10/11
- .NET 7.0+ runtime
- Multi-monitor and high-res support

## Installation & Run
1. Download and extract the package.
2. Run `ArtfulWall.exe`, config file auto-generated on first run.
3. Customize via tray menu or `config.json`.

## Advanced
- Auto adapts to display/DPI changes.
- Auto config backup and recovery.
- Command-line and manual config supported.

---
