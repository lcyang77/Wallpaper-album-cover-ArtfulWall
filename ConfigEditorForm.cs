using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Size = System.Drawing.Size;
using System.Linq;
using System.Collections.Generic;

public class ConfigEditorForm : Form
{
    private Configuration originalConfig;
    private Configuration config;
    private string configPath;

    private TextBox folderPathTextBox = new TextBox();
    private Button browseButton = new Button { Text = "浏览..." }; // 新增浏览按钮
    private NumericUpDown widthNumericUpDown = new NumericUpDown { Maximum = 10000, Minimum = 1 };
    private NumericUpDown heightNumericUpDown = new NumericUpDown { Maximum = 10000, Minimum = 1 };
    private NumericUpDown rowsNumericUpDown = new NumericUpDown { Maximum = 100, Minimum = 1 };
    private NumericUpDown colsNumericUpDown = new NumericUpDown { Maximum = 100, Minimum = 1 };
    private NumericUpDown minIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1 };
    private NumericUpDown maxIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1 };
    private Button confirmButton = new Button { Text = "确认" };
    private Button cancelButton = new Button { Text = "取消" };
    private CheckBox applyWithoutRestartCheckBox = new CheckBox { Text = "保存后立即应用更改（无需重启）", Checked = true };

    public bool ConfigChanged { get; private set; } = false;

    public ConfigEditorForm(string configPath)
    {
        this.configPath = configPath;
        InitializeFormControls();
        LoadConfiguration();
    }

    private void InitializeFormControls()
    {
        this.Text = "配置编辑器";
        this.ClientSize = new Size(420, 310); // 稍微增加窗体宽度以容纳浏览按钮
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        var tableLayoutPanel = new TableLayoutPanel
        {
            ColumnCount = 3, // 增加一列用于浏览按钮
            RowCount = 9,
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        // 调整列宽百分比
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F)); // 标签列
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F)); // 输入框列
        tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F)); // 按钮列

        for (int i = 0; i < 9; i++)
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

        // --- 封面图片路径 ---
        tableLayoutPanel.Controls.Add(new Label { Text = "封面图片路径", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 0);
        folderPathTextBox.Dock = DockStyle.Fill; // 让文本框填充单元格
        tableLayoutPanel.Controls.Add(folderPathTextBox, 1, 0);
        browseButton.Dock = DockStyle.Fill; // 让按钮填充单元格
        browseButton.Click += BrowseButton_Click; // 关联点击事件
        tableLayoutPanel.Controls.Add(browseButton, 2, 0);


        // --- 其他控件 ---
        // 为了保持对齐，其他控件的文本框/数字选择框需要跨列 (ColumnSpan = 2) 或者将浏览按钮列设为0宽度（但不推荐）
        // 这里我们选择让输入控件占据第二列，第三列留空或用于其他（如果需要）
        // 或者，更简洁的方式是，只为路径行设置3列，其他行保持2列。但TableLayoutPanel不支持行级别的列数不同。
        // 因此，我们将第二列和第三列合并给其他控件。

        tableLayoutPanel.Controls.Add(new Label { Text = "宽度（像素）", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 1);
        widthNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(widthNumericUpDown, 1, 1);
        tableLayoutPanel.SetColumnSpan(widthNumericUpDown, 2); // 让NumericUpDown跨越第二和第三列

        tableLayoutPanel.Controls.Add(new Label { Text = "高度（像素）", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 2);
        heightNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(heightNumericUpDown, 1, 2);
        tableLayoutPanel.SetColumnSpan(heightNumericUpDown, 2);

        tableLayoutPanel.Controls.Add(new Label { Text = "行数", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 3);
        rowsNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(rowsNumericUpDown, 1, 3);
        tableLayoutPanel.SetColumnSpan(rowsNumericUpDown, 2);

        tableLayoutPanel.Controls.Add(new Label { Text = "列数", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 4);
        colsNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(colsNumericUpDown, 1, 4);
        tableLayoutPanel.SetColumnSpan(colsNumericUpDown, 2);

        tableLayoutPanel.Controls.Add(new Label { Text = "最小间隔（秒）", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 5);
        minIntervalNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(minIntervalNumericUpDown, 1, 5);
        tableLayoutPanel.SetColumnSpan(minIntervalNumericUpDown, 2);

        tableLayoutPanel.Controls.Add(new Label { Text = "最大间隔（秒）", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true }, 0, 6);
        maxIntervalNumericUpDown.Dock = DockStyle.Fill;
        tableLayoutPanel.Controls.Add(maxIntervalNumericUpDown, 1, 6);
        tableLayoutPanel.SetColumnSpan(maxIntervalNumericUpDown, 2);

        // 修改CheckBox控件放置方式，使其占据三列显示完整文本
        applyWithoutRestartCheckBox.Dock = DockStyle.Fill;
        applyWithoutRestartCheckBox.AutoSize = true;
        tableLayoutPanel.SetColumnSpan(applyWithoutRestartCheckBox, 3); // 让CheckBox跨越三列
        tableLayoutPanel.Controls.Add(applyWithoutRestartCheckBox, 0, 7);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = false,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 10, 10, 0),
            Height = 40,
            Dock = DockStyle.Bottom
        };
        buttonPanel.Controls.Add(confirmButton);
        buttonPanel.Controls.Add(cancelButton);

        confirmButton.Click += ConfirmButton_Click;
        cancelButton.Click += CancelButton_Click;

        this.Controls.Add(tableLayoutPanel);
        this.Controls.Add(buttonPanel);
    }

    // 浏览按钮点击事件处理程序
    private void BrowseButton_Click(object sender, EventArgs e)
    {
        using (var folderBrowserDialog = new FolderBrowserDialog())
        {
            // 设置对话框的初始选定路径（如果文本框中已有路径）
            if (!string.IsNullOrWhiteSpace(folderPathTextBox.Text) && Directory.Exists(folderPathTextBox.Text))
            {
                folderBrowserDialog.SelectedPath = folderPathTextBox.Text;
            }
            folderBrowserDialog.Description = "请选择封面图片所在的文件夹";
            folderBrowserDialog.ShowNewFolderButton = true; // 允许用户创建新文件夹

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                folderPathTextBox.Text = folderBrowserDialog.SelectedPath;
            }
        }
    }

    private void LoadConfiguration()
    {
        try
        {
            var configText = File.ReadAllText(configPath);
            var configuration = JsonSerializer.Deserialize<Configuration>(configText);
            if (configuration == null)
            {
                MessageBox.Show("配置文件格式不正确或者为空。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }
            config = configuration;
            originalConfig = config.Clone();
            
            folderPathTextBox.Text = config.FolderPath;
            widthNumericUpDown.Value = config.Width > 0 ? config.Width : 1; // 确保值在范围内
            heightNumericUpDown.Value = config.Height > 0 ? config.Height : 1;
            rowsNumericUpDown.Value = config.Rows > 0 ? config.Rows : 1;
            colsNumericUpDown.Value = config.Cols > 0 ? config.Cols : 1;
            minIntervalNumericUpDown.Value = config.MinInterval > 0 ? config.MinInterval : 1;
            maxIntervalNumericUpDown.Value = config.MaxInterval > 0 ? config.MaxInterval : 1;
        }
        catch (UnauthorizedAccessException uae)
        {
            MessageBox.Show($"无法读取配置文件，请检查文件权限：{uae.Message}", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载配置时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            this.Close();
        }
    }

    private bool HasConfigurationChanged()
    {
        var updatedConfig = GetCurrentConfigFromUI();
        return !updatedConfig.Equals(originalConfig);
    }

    private Configuration GetCurrentConfigFromUI()
    {
        var updatedConfig = new Configuration
        {
            FolderPath = folderPathTextBox.Text.Trim(),
            Width = (int)widthNumericUpDown.Value,
            Height = (int)heightNumericUpDown.Value,
            Rows = (int)rowsNumericUpDown.Value,
            Cols = (int)colsNumericUpDown.Value,
            MinInterval = (int)minIntervalNumericUpDown.Value,
            MaxInterval = (int)maxIntervalNumericUpDown.Value
        };
        
        // 保持DestFolder为my_wallpaper子目录
        if (!string.IsNullOrEmpty(updatedConfig.FolderPath))
        {
            updatedConfig.DestFolder = Path.Combine(updatedConfig.FolderPath, "my_wallpaper");
        }
        
        return updatedConfig;
    }

    private bool SaveConfiguration()
    {
        try
        {
            if (minIntervalNumericUpDown.Value > maxIntervalNumericUpDown.Value)
            {
                MessageBox.Show("最小间隔不能大于最大间隔！", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string currentFolderPath = folderPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(currentFolderPath))
            {
                MessageBox.Show("封面图片路径不能为空！", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (!Directory.Exists(currentFolderPath))
            {
                MessageBox.Show($"指定的封面图片路径 \"{currentFolderPath}\" 不存在！", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            
            // 检查文件夹中是否有图片文件
            var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".bmp" };
            var hasImageFiles = Directory
                .EnumerateFiles(currentFolderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Any(file => 
                    allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()) &&
                    !file.EndsWith("wallpaper.jpg", StringComparison.OrdinalIgnoreCase)); // 排除生成的壁纸文件
            
            if (!hasImageFiles)
            {
                var result = MessageBox.Show(
                    $"警告：在指定的文件夹 \"{currentFolderPath}\" 中未找到任何图片文件。\n\n" +
                    "这可能会导致壁纸应用无法正常工作。您确定要继续保存此路径吗？",
                    "未找到图片",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                
                if (result == DialogResult.No)
                {
                    return false;
                }
            }

            config.FolderPath = currentFolderPath;

            // 自动创建 my_wallpaper 子目录
            string dest = Path.Combine(config.FolderPath, "my_wallpaper");
            try
            {
                Directory.CreateDirectory(dest);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法创建目标文件夹 \"{dest}\"：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            config.DestFolder = dest;

            config.Width = (int)widthNumericUpDown.Value;
            config.Height = (int)heightNumericUpDown.Value;
            config.Rows = (int)rowsNumericUpDown.Value;
            config.Cols = (int)colsNumericUpDown.Value;
            config.MinInterval = (int)minIntervalNumericUpDown.Value;
            config.MaxInterval = (int)maxIntervalNumericUpDown.Value;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var configText = JsonSerializer.Serialize(config, options);
            File.WriteAllText(configPath, configText);
            
            ConfigChanged = true;
            return true;
        }
        catch (UnauthorizedAccessException uae)
        {
            MessageBox.Show($"无法访问配置文件路径，请检查文件权限：{uae.Message}", "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存配置时发生错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }

    private void ConfirmButton_Click(object sender, EventArgs e)
    {
        if (minIntervalNumericUpDown.Value > maxIntervalNumericUpDown.Value)
        {
            MessageBox.Show("最小间隔不能大于最大间隔！", "验证错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (HasConfigurationChanged())
        {
            if (SaveConfiguration())
            {
                MessageBox.Show("配置已保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = applyWithoutRestartCheckBox.Checked ? DialogResult.OK : DialogResult.Yes;
                this.Close();
            }
        }
        else
        {
            MessageBox.Show("未检测到配置变更。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.DialogResult = DialogResult.Cancel; // 如果没有变化，也认为是取消操作或无操作
            this.Close();
        }
    }

    private void CancelButton_Click(object sender, EventArgs e)
    {
        this.DialogResult = DialogResult.Cancel;
        this.Close();
    }
}

// Configuration 类定义 (保持不变)
public class Configuration
{
    public string? FolderPath { get; set; }
    public string? DestFolder { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Rows { get; set; }
    public int Cols { get; set; }
    public int MinInterval { get; set; } = 3;
    public int MaxInterval { get; set; } = 10;

    public Configuration Clone()
    {
        return new Configuration
        {
            FolderPath = this.FolderPath,
            DestFolder = this.DestFolder,
            Width = this.Width,
            Height = this.Height,
            Rows = this.Rows,
            Cols = this.Cols,
            MinInterval = this.MinInterval,
            MaxInterval = this.MaxInterval
        };
    }

    public bool Equals(Configuration other)
    {
        if (other == null) return false;
        return FolderPath == other.FolderPath &&
               DestFolder == other.DestFolder && // 确保比较 DestFolder
               Width == other.Width &&
               Height == other.Height &&
               Rows == other.Rows &&
               Cols == other.Cols &&
               MinInterval == other.MinInterval &&
               MaxInterval == other.MaxInterval;
    }
}
