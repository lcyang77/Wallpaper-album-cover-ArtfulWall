using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using ArtfulWall.Models; // Using the separated model
using ArtfulWall.Services; // For WallpaperUpdater
using Size = System.Drawing.Size; // Alias for System.Drawing.Size

namespace ArtfulWall.UI
{
    /// <summary>
    /// 提供用于编辑应用程序配置的窗体。
    /// </summary>
    public class ConfigEditorForm : Form
    {
        private Configuration originalConfig; // Store the initial configuration for comparison
        private Configuration currentConfig;  // Store the configuration being edited
        private readonly string configPath;
        private WallpaperUpdater? wallpaperUpdater; // Optional: For applying changes immediately

        // UI Controls
        private readonly TextBox folderPathTextBox = new TextBox();
        private readonly Button browseButton = new Button { Text = "浏览..." };
        private readonly NumericUpDown widthNumericUpDown = new NumericUpDown { Maximum = 10000, Minimum = 1, Value = 1920 };
        private readonly NumericUpDown heightNumericUpDown = new NumericUpDown { Maximum = 10000, Minimum = 1, Value = 1080 };
        private readonly NumericUpDown rowsNumericUpDown = new NumericUpDown { Maximum = 100, Minimum = 1, Value = 1 };
        private readonly NumericUpDown colsNumericUpDown = new NumericUpDown { Maximum = 100, Minimum = 1, Value = 1 };
        private readonly NumericUpDown minIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1, Value = 3 };
        private readonly NumericUpDown maxIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1, Value = 10 };
        private readonly Button confirmButton = new Button { Text = "确认" };
        private readonly Button cancelButton = new Button { Text = "取消" };
        private readonly CheckBox applyWithoutRestartCheckBox = new CheckBox { Text = "保存后立即应用更改（无需重启应用）", Checked = true };

        /// <summary>
        /// 指示配置自加载以来是否已更改并成功保存。
        /// 此属性名已从 ConfigChangedSinceLoad 更改为 ConfigChanged 以解决 CS1061 错误。
        /// </summary>
        public bool ConfigChanged { get; private set; } = false;

        /// <summary>
        /// 初始化 ConfigEditorForm 类的新实例。
        /// </summary>
        /// <param name="configFilePath">配置文件的路径。</param>
        public ConfigEditorForm(string configFilePath)
        {
            this.configPath = configFilePath;
            // Initialize with default or empty config to avoid null issues before loading
            this.currentConfig = new Configuration();
            this.originalConfig = this.currentConfig.Clone();

            InitializeFormControls();
            LoadConfiguration();
        }

        /// <summary>
        /// 设置 WallpaperUpdater 实例，用于在保存后立即应用配置更改。
        /// </summary>
        /// <param name="updater">WallpaperUpdater 实例。</param>
        public void SetWallpaperUpdater(WallpaperUpdater updater)
        {
            this.wallpaperUpdater = updater;
        }

        private void InitializeFormControls()
        {
            this.Text = "配置编辑器";
            this.ClientSize = new Size(430, 320); // Adjusted size for better spacing
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen; // Center on parent or screen
            this.Padding = new Padding(10);

            var tableLayoutPanel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 9, // 7 for inputs, 1 for checkbox, 1 for padding/buttons
                Dock = DockStyle.Fill,
                AutoSize = true,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None, // Cleaner look
            };

            // Define column styles for better control
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F)); // Labels
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // Input controls
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Browse button

            // Define row styles
            for (int i = 0; i < 7; i++) // Input rows
                tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // Checkbox row
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Button panel row (will be handled by DockStyle.Bottom)

            // Helper to add controls
            void AddControlRow(string labelText, Control control, int rowIndex, bool spanInput = true)
            {
                var label = new Label { Text = labelText, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoSize = false };
                tableLayoutPanel.Controls.Add(label, 0, rowIndex);
                control.Dock = DockStyle.Fill;
                tableLayoutPanel.Controls.Add(control, 1, rowIndex);
                if (spanInput)
                {
                    tableLayoutPanel.SetColumnSpan(control, 2); // Span across input and button columns if no specific button for this row
                }
            }

            // --- 封面图片路径 ---
            AddControlRow("封面图片路径:", folderPathTextBox, 0, false); // Don't span, browse button is next
            browseButton.Dock = DockStyle.Fill;
            browseButton.Margin = new Padding(3, 0, 0, 0); // Add some margin for the browse button
            browseButton.Click += BrowseButton_Click;
            tableLayoutPanel.Controls.Add(browseButton, 2, 0);

            // --- 其他控件 ---
            AddControlRow("宽度 (像素):", widthNumericUpDown, 1);
            AddControlRow("高度 (像素):", heightNumericUpDown, 2);
            AddControlRow("行数:", rowsNumericUpDown, 3);
            AddControlRow("列数:", colsNumericUpDown, 4);
            AddControlRow("最小间隔 (秒):", minIntervalNumericUpDown, 5);
            AddControlRow("最大间隔 (秒):", maxIntervalNumericUpDown, 6);

            // --- CheckBox ---
            applyWithoutRestartCheckBox.Dock = DockStyle.Fill;
            applyWithoutRestartCheckBox.Padding = new Padding(0, 5, 0, 0);
            tableLayoutPanel.Controls.Add(applyWithoutRestartCheckBox, 0, 7);
            tableLayoutPanel.SetColumnSpan(applyWithoutRestartCheckBox, 3);

            // --- 按钮面板 ---
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom, // Dock to the bottom of the form
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 10, 0, 0) // Top padding for separation
            };
            confirmButton.Size = new Size(80, 28);
            cancelButton.Size = new Size(80, 28);
            confirmButton.Click += ConfirmButton_Click;
            cancelButton.Click += CancelButton_Click;

            buttonPanel.Controls.Add(cancelButton); // Add cancel first for RightToLeft
            buttonPanel.Controls.Add(confirmButton);

            this.Controls.Add(tableLayoutPanel);
            this.Controls.Add(buttonPanel); // Add button panel last so it's at the bottom
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using (var folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "请选择封面图片所在的文件夹";
                folderBrowserDialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(folderPathTextBox.Text) && Directory.Exists(folderPathTextBox.Text))
                {
                    folderBrowserDialog.SelectedPath = folderPathTextBox.Text;
                }

                if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderPathTextBox.Text = folderBrowserDialog.SelectedPath;
                }
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    // Config file doesn't exist, use defaults and allow user to save a new one.
                    MessageBox.Show("配置文件不存在，将使用默认设置。您可以在保存时创建新的配置文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    currentConfig = new Configuration(); // Ensure it's a new default instance
                    originalConfig = currentConfig.Clone();
                    PopulateUIFromConfig(currentConfig);
                    return;
                }

                var configText = File.ReadAllText(configPath);
                var loadedConfig = JsonSerializer.Deserialize<Configuration>(configText);

                if (loadedConfig == null)
                {
                    MessageBox.Show("配置文件格式不正确或为空。将使用默认设置。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    currentConfig = new Configuration();
                }
                else
                {
                    currentConfig = loadedConfig;
                }

                originalConfig = currentConfig.Clone(); // Store a copy of the originally loaded config
                PopulateUIFromConfig(currentConfig);
            }
            catch (JsonException jsonEx)
            {
                MessageBox.Show($"解析配置文件时发生错误：{jsonEx.Message}\n将使用默认设置。", "配置加载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentConfig = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
            catch (UnauthorizedAccessException uae)
            {
                MessageBox.Show($"无法读取配置文件，请检查文件权限：{uae.Message}\n将使用默认设置。", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentConfig = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置时发生未知错误：{ex.Message}\n将使用默认设置。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                currentConfig = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
        }

        private void PopulateUIFromConfig(Configuration configToDisplay)
        {
            folderPathTextBox.Text = configToDisplay.FolderPath;
            widthNumericUpDown.Value = Math.Max(widthNumericUpDown.Minimum, Math.Min(widthNumericUpDown.Maximum, configToDisplay.Width));
            heightNumericUpDown.Value = Math.Max(heightNumericUpDown.Minimum, Math.Min(heightNumericUpDown.Maximum, configToDisplay.Height));
            rowsNumericUpDown.Value = Math.Max(rowsNumericUpDown.Minimum, Math.Min(rowsNumericUpDown.Maximum, configToDisplay.Rows));
            colsNumericUpDown.Value = Math.Max(colsNumericUpDown.Minimum, Math.Min(colsNumericUpDown.Maximum, configToDisplay.Cols));
            minIntervalNumericUpDown.Value = Math.Max(minIntervalNumericUpDown.Minimum, Math.Min(minIntervalNumericUpDown.Maximum, configToDisplay.MinInterval));
            maxIntervalNumericUpDown.Value = Math.Max(maxIntervalNumericUpDown.Minimum, Math.Min(maxIntervalNumericUpDown.Maximum, configToDisplay.MaxInterval));
        }

        private Configuration GetCurrentConfigFromUI()
        {
            var uiConfig = new Configuration
            {
                FolderPath = folderPathTextBox.Text.Trim(),
                Width = (int)widthNumericUpDown.Value,
                Height = (int)heightNumericUpDown.Value,
                Rows = (int)rowsNumericUpDown.Value,
                Cols = (int)colsNumericUpDown.Value,
                MinInterval = (int)minIntervalNumericUpDown.Value,
                MaxInterval = (int)maxIntervalNumericUpDown.Value
            };

            if (!string.IsNullOrWhiteSpace(uiConfig.FolderPath))
            {
                uiConfig.DestFolder = Path.Combine(uiConfig.FolderPath, "my_wallpaper");
            }
            else
            {
                uiConfig.DestFolder = null;
            }
            return uiConfig;
        }

        private bool CheckForConfigurationChanges()
        {
            var configFromUI = GetCurrentConfigFromUI();
            return !configFromUI.Equals(originalConfig);
        }

        private bool ValidateAndSaveChanges()
        {
            var updatedConfig = GetCurrentConfigFromUI();

            if (updatedConfig.MinInterval > updatedConfig.MaxInterval)
            {
                MessageBox.Show("最小间隔时间不能大于最大间隔时间。", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                minIntervalNumericUpDown.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(updatedConfig.FolderPath))
            {
                MessageBox.Show("封面图片路径不能为空。", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                folderPathTextBox.Focus();
                return false;
            }

            if (!Directory.Exists(updatedConfig.FolderPath))
            {
                MessageBox.Show($"指定的封面图片路径 \"{updatedConfig.FolderPath}\" 不存在。", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                folderPathTextBox.Focus();
                return false;
            }

            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
            bool hasImageFiles = false;
            try
            {
                hasImageFiles = Directory
                   .EnumerateFiles(updatedConfig.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                   .Any(file =>
                       allowedExtensions.Contains(Path.GetExtension(file)) &&
                       !Path.GetFileName(file).Equals("wallpaper.jpg", StringComparison.OrdinalIgnoreCase)
                   );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"检查图片文件时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!hasImageFiles)
            {
                var result = MessageBox.Show(
                    $"警告：在指定的文件夹 \"{updatedConfig.FolderPath}\" 中似乎未找到任何符合条件的图片文件。\n\n" +
                    "这可能会导致壁纸应用无法正常工作。您确定要继续保存此路径吗？",
                    "未找到图片",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    folderPathTextBox.Focus();
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(updatedConfig.DestFolder))
            {
                updatedConfig.DestFolder = Path.Combine(updatedConfig.FolderPath, "my_wallpaper");
            }

            try
            {
                Directory.CreateDirectory(updatedConfig.DestFolder!);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法创建目标文件夹 \"{updatedConfig.DestFolder}\"：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var configJson = JsonSerializer.Serialize(updatedConfig, options);
                File.WriteAllText(configPath, configJson);

                currentConfig = updatedConfig;
                originalConfig = currentConfig.Clone();
                ConfigChanged = true; // 更新属性名

                return true;
            }
            catch (UnauthorizedAccessException uae)
            {
                MessageBox.Show($"无法保存配置文件，请检查文件权限：{uae.Message}", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        private void ConfirmButton_Click(object? sender, EventArgs e)
        {
            if (CheckForConfigurationChanges())
            {
                if (ValidateAndSaveChanges())
                {
                    MessageBox.Show("配置已成功保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    bool appliedImmediately = false;
                    if (applyWithoutRestartCheckBox.Checked && wallpaperUpdater != null)
                    {
                        try
                        {
                            wallpaperUpdater.UpdateConfig(currentConfig.Clone());
                            appliedImmediately = true;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"尝试立即应用配置时出错: {ex.Message}\n更改已保存，但可能需要重启应用程序才能完全生效。", "应用错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }

                    if (applyWithoutRestartCheckBox.Checked && wallpaperUpdater == null && ConfigChanged) // 使用更新后的属性名
                    {
                        var restartResult = MessageBox.Show(
                            "配置已保存。部分更改可能需要重启应用程序才能生效。\n是否立即重启应用程序？",
                            "重启提示",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (restartResult == DialogResult.Yes)
                        {
                            this.DialogResult = DialogResult.Yes;
                            Application.Restart();
                            Environment.Exit(0);
                            return;
                        }
                        else
                        {
                            this.DialogResult = DialogResult.OK;
                        }
                    }
                    else if (appliedImmediately || !ConfigChanged) // 使用更新后的属性名
                    {
                        this.DialogResult = DialogResult.OK;
                    }
                    else
                    {
                        this.DialogResult = DialogResult.OK;
                        MessageBox.Show("配置已保存。更改将在下次应用程序启动时生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    this.Close();
                }
            }
            else
            {
                MessageBox.Show("未检测到配置变更。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && this.DialogResult == DialogResult.None && CheckForConfigurationChanges())
            {
                var result = MessageBox.Show("配置已更改但未保存。是否放弃更改？", "未保存的更改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }
    }
}