//  ConfigEditorForm.cs
//  Rewritten with English UI text and comments.
//  NOTE: Only text and comments were translated; program logic is unchanged.

using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using ArtfulWall.Models;      // Using the separated model
using ArtfulWall.Services;    // For WallpaperUpdater
using ArtfulWall.Utils;       // For DisplayManager
using Size = System.Drawing.Size; // Alias for System.Drawing.Size

namespace ArtfulWall.UI
{
    /// <summary>
    /// A Windows Forms dialog that lets the user view and edit the
    /// application-wide configuration for ArtfulWall.
    /// </summary>
    public class ConfigEditorForm : Form
    {
        // ─────────────────────────────────────────────────────────────
        // Fields
        // ─────────────────────────────────────────────────────────────
        private Configuration originalConfig;           // Configuration as it was when the form was opened
        private Configuration currentConfig;            // Configuration being edited in the UI
        private readonly string configPath;             // Path to the JSON config file on disk
        private WallpaperUpdater? wallpaperUpdater;     // Optional: Used to apply changes immediately
        private List<DisplayInfo>? displayInfo;         // Cached information for all detected monitors

        // ─────────────────────────────────────────────────────────────
        // UI controls
        // ─────────────────────────────────────────────────────────────
        private readonly TextBox folderPathTextBox           = new TextBox();
        private readonly Button  browseButton                = new Button { Text = "Browse…" };
        private readonly NumericUpDown widthNumericUpDown    = new NumericUpDown { Maximum = 10000, Minimum = 1, Value = 1920 };
        private readonly NumericUpDown heightNumericUpDown   = new NumericUpDown { Maximum = 10000, Minimum = 1, Value = 1080 };
        private readonly NumericUpDown rowsNumericUpDown     = new NumericUpDown { Maximum = 100,   Minimum = 1, Value = 1 };
        private readonly NumericUpDown colsNumericUpDown     = new NumericUpDown { Maximum = 100,   Minimum = 1, Value = 1 };
        private readonly NumericUpDown minIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1, Value = 3 };
        private readonly NumericUpDown maxIntervalNumericUpDown = new NumericUpDown { Maximum = 3600, Minimum = 1, Value = 10 };
        private readonly ComboBox wallpaperModeComboBox      = new ComboBox();     // Wallpaper-mode selector
        private readonly CheckBox adaptToDpiCheckBox         = new CheckBox { Text = "Adapt to DPI scaling automatically", Checked = true };
        private readonly CheckBox autoAdjustDisplayCheckBox  = new CheckBox { Text = "Auto-adjust wallpaper on display-setting changes", Checked = true };
        private readonly TabControl monitorTabControl        = new TabControl();   // Per-monitor settings
        private readonly Button confirmButton                = new Button { Text = "OK" };
        private readonly Button cancelButton                 = new Button { Text = "Cancel" };
        private readonly CheckBox applyWithoutRestartCheckBox =
            new CheckBox { Text = "Apply changes immediately after saving (no restart required)", Checked = true };

        // UI controls for per-monitor grid sizes
        private readonly Dictionary<int, NumericUpDown> monitorRowsControls = new();
        private readonly Dictionary<int, NumericUpDown> monitorColsControls = new();

        /// <summary>
        /// Indicates whether the configuration has been modified and saved
        /// since the form was opened. Renamed from <c>ConfigChangedSinceLoad</c>
        /// to resolve compiler error CS1061.
        /// </summary>
        public bool ConfigChanged { get; private set; } = false;

        // ─────────────────────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────────────────────
        public ConfigEditorForm(string configFilePath)
        {
            configPath     = configFilePath;
            currentConfig  = new Configuration();        // Start with defaults
            originalConfig = currentConfig.Clone();      // Deep clone for later comparison

            // Try to retrieve monitor information up-front (optional)
            try
            {
                displayInfo = DisplayManager.GetDisplays();
            }
            catch
            {
                displayInfo = new List<DisplayInfo>();   // Fallback to empty list
            }

            InitializeFormControls();
            LoadConfiguration();
        }

        /// <summary>
        /// Injects a <see cref="WallpaperUpdater"/> so that the form can
        /// apply the updated configuration immediately after saving.
        /// </summary>
        public void SetWallpaperUpdater(WallpaperUpdater updater) => wallpaperUpdater = updater;

        // ─────────────────────────────────────────────────────────────
        // UI initialization
        // ─────────────────────────────────────────────────────────────
        private void InitializeFormControls()
        {
            Text            = "Configuration Editor";
            ClientSize      = new Size(850, 550);     // Bigger window to fit controls
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterScreen;
            Padding         = new Padding(10);

            // Main table-layout container
            var tableLayoutPanel = new TableLayoutPanel
            {
                ColumnCount      = 3,
                RowCount         = 12,
                Dock             = DockStyle.Fill,
                AutoSize         = true,
                CellBorderStyle  = TableLayoutPanelCellBorderStyle.None
            };

            // Column sizes
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F)); // Label column
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // Input
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Browse button

            // Row sizes
            for (int i = 0; i < 10; i++)
                tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 220F)); // Monitor tab control
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));  // Apply-immediately checkbox
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Button row

            // Helper method to add a label and control in a single row
            void AddControlRow(string labelText, Control control, int rowIndex, bool spanInput = true)
            {
                var label = new Label
                {
                    Text      = labelText,
                    Dock      = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    AutoSize  = false
                };
                tableLayoutPanel.Controls.Add(label, 0, rowIndex);

                control.Dock = DockStyle.Fill;
                tableLayoutPanel.Controls.Add(control, 1, rowIndex);

                if (spanInput)
                    tableLayoutPanel.SetColumnSpan(control, 2);
            }

            // ── Folder path + browse button ─────────────────────────
            AddControlRow("Image folder path:", folderPathTextBox, rowIndex: 0, spanInput: false);
            browseButton.Dock   = DockStyle.Fill;
            browseButton.Margin = new Padding(3, 0, 0, 0);
            browseButton.Click += BrowseButton_Click;
            tableLayoutPanel.Controls.Add(browseButton, 2, 0);

            // ── Basic numeric settings ──────────────────────────────
            AddControlRow("Width (pixels):",            widthNumericUpDown,  1);
            AddControlRow("Height (pixels):",           heightNumericUpDown, 2);
            AddControlRow("Rows:",                      rowsNumericUpDown,   3);
            AddControlRow("Columns:",                   colsNumericUpDown,   4);
            AddControlRow("Minimum interval (seconds):", minIntervalNumericUpDown, 5);
            AddControlRow("Maximum interval (seconds):", maxIntervalNumericUpDown, 6);

            // ── Wallpaper-mode selector ─────────────────────────────
            wallpaperModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            wallpaperModeComboBox.Items.AddRange(new object[] { "Per-monitor wallpaper", "Single wallpaper" });
            wallpaperModeComboBox.SelectedIndex      = 0;
            wallpaperModeComboBox.SelectedIndexChanged += WallpaperMode_Changed;
            AddControlRow("Wallpaper mode:", wallpaperModeComboBox, 7);

            // ── DPI & display-change checkboxes ─────────────────────
            AddControlRow("DPI adaptation:",        adaptToDpiCheckBox,        8);
            AddControlRow("Display change:",        autoAdjustDisplayCheckBox, 9);

            // ── Per-monitor tab control ─────────────────────────────
            InitializeMonitorTabs();
            AddControlRow("Monitor-specific settings:", monitorTabControl, 10);

            // ── Apply-without-restart ───────────────────────────────
            applyWithoutRestartCheckBox.Dock = DockStyle.Fill;
            applyWithoutRestartCheckBox.Padding = new Padding(0, 5, 0, 0);
            tableLayoutPanel.Controls.Add(applyWithoutRestartCheckBox, 0, 11);
            tableLayoutPanel.SetColumnSpan(applyWithoutRestartCheckBox, 3);

            // ── OK / Cancel buttons ─────────────────────────────────
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock          = DockStyle.Bottom,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                Padding       = new Padding(0, 10, 0, 0)
            };
            confirmButton.Size = cancelButton.Size = new Size(80, 28);
            confirmButton.Click += ConfirmButton_Click;
            cancelButton.Click  += CancelButton_Click;

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(confirmButton);

            // Add controls to form
            Controls.Add(tableLayoutPanel);
            Controls.Add(buttonPanel);
        }

        /// <summary>
        /// Builds the per-monitor tab pages and their numeric controls.
        /// </summary>
        private void InitializeMonitorTabs()
        {
            monitorTabControl.SizeMode = TabSizeMode.FillToRight;
            monitorTabControl.Dock     = DockStyle.Fill;
            monitorTabControl.Height   = 200;
            monitorRowsControls.Clear();
            monitorColsControls.Clear();

            if (displayInfo is null || displayInfo.Count == 0)
            {
                var noDisplayTab = new TabPage("No displays detected");
                var infoLabel = new Label
                {
                    Text      = "Unable to retrieve monitor information. Global settings will be used.",
                    Dock      = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };
                noDisplayTab.Controls.Add(infoLabel);
                monitorTabControl.TabPages.Add(noDisplayTab);
                return;
            }

            // Build a tab for each detected monitor
            foreach (var display in displayInfo)
            {
                string orientation = display.Orientation.ToString();
                string title       = $"Monitor {display.DisplayNumber + 1}: {display.Width}×{display.Height}";
                if (display.IsPrimary) title += " (Primary)";
                title += $" – {orientation}";

                var tabPage = new TabPage(title);

                // Layout inside each tab
                var tabLayout = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount    = 3,
                    Dock        = DockStyle.Fill,
                    Padding     = new Padding(10)
                };
                tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
                tabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                for (int i = 0; i < 3; i++)
                    tabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

                // Monitor info label
                var infoLabel = new Label
                {
                    Text      = $"DPI scaling: {display.DpiScaling:F2}×   Resolution: {display.Width}×{display.Height}",
                    Dock      = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                tabLayout.Controls.Add(infoLabel, 0, 0);
                tabLayout.SetColumnSpan(infoLabel, 2);

                // Rows
                var rowsLabel = new Label
                {
                    Text      = "Rows:",
                    Dock      = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                var rowsUpDown = new NumericUpDown
                {
                    Minimum = 1,
                    Maximum = 100,
                    Value   = rowsNumericUpDown.Value, // Default to global
                    Dock    = DockStyle.Fill
                };
                tabLayout.Controls.Add(rowsLabel, 0, 1);
                tabLayout.Controls.Add(rowsUpDown, 1, 1);
                monitorRowsControls[display.DisplayNumber] = rowsUpDown;

                // Columns
                var colsLabel = new Label
                {
                    Text      = "Columns:",
                    Dock      = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft
                };
                var colsUpDown = new NumericUpDown
                {
                    Minimum = 1,
                    Maximum = 100,
                    Value   = colsNumericUpDown.Value, // Default to global
                    Dock    = DockStyle.Fill
                };
                tabLayout.Controls.Add(colsLabel, 0, 2);
                tabLayout.Controls.Add(colsUpDown, 1, 2);
                monitorColsControls[display.DisplayNumber] = colsUpDown;

                tabPage.Controls.Add(tabLayout);
                monitorTabControl.TabPages.Add(tabPage);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Event handlers
        // ─────────────────────────────────────────────────────────────
        private void WallpaperMode_Changed(object? sender, EventArgs e)
        {
            bool isPerMonitor = wallpaperModeComboBox.SelectedIndex == 0;
            monitorTabControl.Enabled   = isPerMonitor;
            adaptToDpiCheckBox.Enabled  = isPerMonitor;
            autoAdjustDisplayCheckBox.Enabled = isPerMonitor;
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description        = "Select the folder that contains your images",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrWhiteSpace(folderPathTextBox.Text) &&
                Directory.Exists(folderPathTextBox.Text))
            {
                dlg.SelectedPath = folderPathTextBox.Text;
            }

            if (dlg.ShowDialog(this) == DialogResult.OK)
                folderPathTextBox.Text = dlg.SelectedPath;
        }

        // ─────────────────────────────────────────────────────────────
        // Configuration I/O
        // ─────────────────────────────────────────────────────────────
        /// <summary>Reads the JSON config file (if any) and populates the UI.</summary>
        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    MessageBox.Show(
                        "Configuration file not found. Default settings will be used. " +
                        "A new file will be created when you save.",
                        "Information",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    currentConfig  = new Configuration();
                    originalConfig = currentConfig.Clone();
                    PopulateUIFromConfig(currentConfig);
                    return;
                }

                string configText = File.ReadAllText(configPath);
                var    loaded     = JsonSerializer.Deserialize<Configuration>(configText);

                currentConfig  = loaded ?? new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
            catch (JsonException ex)
            {
                MessageBox.Show(
                    $"Error parsing configuration: {ex.Message}\nDefault settings will be used.",
                    "Configuration Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                currentConfig  = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    $"Unable to read configuration: {ex.Message}\nDefault settings will be used.",
                    "Permission Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                currentConfig  = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unknown error loading configuration: {ex.Message}\nDefault settings will be used.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                currentConfig  = new Configuration();
                originalConfig = currentConfig.Clone();
                PopulateUIFromConfig(currentConfig);
            }
        }

        /// <summary>
        /// Copies the values of <paramref name="configToDisplay"/> into the UI
        /// controls so that the user can edit them.
        /// </summary>
        private void PopulateUIFromConfig(Configuration configToDisplay)
        {
            // Basic settings
            folderPathTextBox.Text   = configToDisplay.FolderPath;
            widthNumericUpDown.Value = Clamp(widthNumericUpDown,  configToDisplay.Width);
            heightNumericUpDown.Value= Clamp(heightNumericUpDown, configToDisplay.Height);
            rowsNumericUpDown.Value  = Clamp(rowsNumericUpDown,   configToDisplay.Rows);
            colsNumericUpDown.Value  = Clamp(colsNumericUpDown,   configToDisplay.Cols);
            minIntervalNumericUpDown.Value = Clamp(minIntervalNumericUpDown, configToDisplay.MinInterval);
            maxIntervalNumericUpDown.Value = Clamp(maxIntervalNumericUpDown, configToDisplay.MaxInterval);

            // Newer settings
            wallpaperModeComboBox.SelectedIndex =
                configToDisplay.Mode == Configuration.WallpaperMode.PerMonitor ? 0 : 1;
            adaptToDpiCheckBox.Checked         = configToDisplay.AdaptToDpiScaling;
            autoAdjustDisplayCheckBox.Checked  = configToDisplay.AutoAdjustToDisplayChanges;

            // Per-monitor overrides (only if monitor info is available)
            if (displayInfo is not null && displayInfo.Count > 0)
            {
                foreach (var display in displayInfo)
                {
                    var monitorConfig = configToDisplay.MonitorConfigurations
                        .FirstOrDefault(mc => mc.DisplayNumber == display.DisplayNumber);

                    if (monitorConfig is not null)
                    {
                        if (monitorRowsControls.TryGetValue(display.DisplayNumber, out var rowsCtrl))
                            rowsCtrl.Value = Clamp(rowsCtrl, monitorConfig.Rows);

                        if (monitorColsControls.TryGetValue(display.DisplayNumber, out var colsCtrl))
                            colsCtrl.Value = Clamp(colsCtrl, monitorConfig.Cols);
                    }
                    else
                    {
                        if (monitorRowsControls.TryGetValue(display.DisplayNumber, out var rowsCtrl))
                            rowsCtrl.Value = configToDisplay.Rows;

                        if (monitorColsControls.TryGetValue(display.DisplayNumber, out var colsCtrl))
                            colsCtrl.Value = configToDisplay.Cols;
                    }
                }
            }

            bool isPerMonitor = wallpaperModeComboBox.SelectedIndex == 0;
            monitorTabControl.Enabled       = isPerMonitor;
            adaptToDpiCheckBox.Enabled      = isPerMonitor;
            autoAdjustDisplayCheckBox.Enabled = isPerMonitor;
        }

        /// <summary>
        /// Builds a new <see cref="Configuration"/> object from the current
        /// state of the UI controls.
        /// </summary>
        private Configuration GetCurrentConfigFromUI()
        {
            var uiConfig = new Configuration
            {
                FolderPath  = folderPathTextBox.Text.Trim(),
                Width       = (int)widthNumericUpDown.Value,
                Height      = (int)heightNumericUpDown.Value,
                Rows        = (int)rowsNumericUpDown.Value,
                Cols        = (int)colsNumericUpDown.Value,
                MinInterval = (int)minIntervalNumericUpDown.Value,
                MaxInterval = (int)maxIntervalNumericUpDown.Value,
                Mode        = wallpaperModeComboBox.SelectedIndex == 0 ?
                                Configuration.WallpaperMode.PerMonitor :
                                Configuration.WallpaperMode.Single,
                AdaptToDpiScaling          = adaptToDpiCheckBox.Checked,
                AutoAdjustToDisplayChanges = autoAdjustDisplayCheckBox.Checked,
                MonitorConfigurations      = new List<MonitorConfiguration>()
            };

            uiConfig.DestFolder = string.IsNullOrWhiteSpace(uiConfig.FolderPath)
                ? null
                : Path.Combine(uiConfig.FolderPath, "my_wallpaper");

            // Add per-monitor overrides
            if (displayInfo is not null &&
                displayInfo.Count > 0 &&
                uiConfig.Mode == Configuration.WallpaperMode.PerMonitor)
            {
                foreach (var display in displayInfo)
                {
                    if (monitorRowsControls.TryGetValue(display.DisplayNumber, out var rowsCtrl) &&
                        monitorColsControls.TryGetValue(display.DisplayNumber, out var colsCtrl))
                    {
                        uiConfig.MonitorConfigurations.Add(new MonitorConfiguration
                        {
                            DisplayNumber = display.DisplayNumber,
                            MonitorId     = display.DeviceName,
                            Width         = display.Width,
                            Height        = display.Height,
                            DpiScaling    = display.DpiScaling,
                            Rows          = (int)rowsCtrl.Value,
                            Cols          = (int)colsCtrl.Value,
                            IsPortrait    = display.Orientation is DisplayInfo.OrientationType.Portrait
                                         or DisplayInfo.OrientationType.PortraitFlipped
                        });
                    }
                }
            }

            return uiConfig;
        }

        /// <summary>True if the user changed any setting since the form was opened.</summary>
        private bool CheckForConfigurationChanges() =>
            !GetCurrentConfigFromUI().Equals(originalConfig);

        // Clamp helper for numeric-up-down controls
        private static decimal Clamp(NumericUpDown ctrl, int value) =>
            Math.Max(ctrl.Minimum, Math.Min(ctrl.Maximum, value));

        // ─────────────────────────────────────────────────────────────
        // Validation + save
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Validates user input, writes the configuration file, and updates
        /// <see cref="currentConfig"/> / <see cref="originalConfig"/>.
        /// Returns <c>true</c> if everything succeeded.
        /// </summary>
        private bool ValidateAndSaveChanges()
        {
            var updatedConfig = GetCurrentConfigFromUI();

            if (updatedConfig.MinInterval > updatedConfig.MaxInterval)
            {
                MessageBox.Show(
                    "Minimum interval cannot be greater than maximum interval.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                minIntervalNumericUpDown.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(updatedConfig.FolderPath))
            {
                MessageBox.Show(
                    "Image folder path cannot be empty.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                folderPathTextBox.Focus();
                return false;
            }

            if (!Directory.Exists(updatedConfig.FolderPath))
            {
                MessageBox.Show(
                    $"The specified folder \"{updatedConfig.FolderPath}\" does not exist.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                folderPathTextBox.Focus();
                return false;
            }

            // Verify at least one usable image exists in the folder
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };

            bool hasImageFiles;
            try
            {
                hasImageFiles = Directory.EnumerateFiles(updatedConfig.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Any(file =>
                        allowedExtensions.Contains(Path.GetExtension(file)) &&
                        !string.Equals(Path.GetFileName(file), "wallpaper.jpg", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking images: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (!hasImageFiles)
            {
                var answer = MessageBox.Show(
                    $"Warning: No image files were found in \"{updatedConfig.FolderPath}\".\n\n" +
                    "The wallpaper engine may not function correctly. Do you want to save anyway?",
                    "No Images Found",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer == DialogResult.No)
                {
                    folderPathTextBox.Focus();
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(updatedConfig.DestFolder))
                updatedConfig.DestFolder = Path.Combine(updatedConfig.FolderPath, "my_wallpaper");

            try
            {
                Directory.CreateDirectory(updatedConfig.DestFolder!);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to create destination folder \"{updatedConfig.DestFolder}\": {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Serialize + save
            try
            {
                var options   = new JsonSerializerOptions { WriteIndented = true };
                var json      = JsonSerializer.Serialize(updatedConfig, options);
                File.WriteAllText(configPath, json);

                currentConfig  = updatedConfig;
                originalConfig = currentConfig.Clone();
                ConfigChanged  = true;
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show($"Unable to save configuration: {ex.Message}",
                    "Permission Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unknown error while saving: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        private void ConfirmButton_Click(object? sender, EventArgs e)
        {
            if (!CheckForConfigurationChanges())
            {
                MessageBox.Show("No changes detected.", "Information",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (!ValidateAndSaveChanges()) return;

            MessageBox.Show("Configuration saved successfully.",
                "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

            bool appliedImmediately = false;
            if (applyWithoutRestartCheckBox.Checked && wallpaperUpdater is not null)
            {
                try
                {
                    wallpaperUpdater.UpdateConfig(currentConfig.Clone());
                    appliedImmediately = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to apply configuration immediately: {ex.Message}\n" +
                        "Changes were saved, but you may need to restart the application.",
                        "Apply Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            if (applyWithoutRestartCheckBox.Checked &&
                wallpaperUpdater is null &&
                ConfigChanged)
            {
                var restart = MessageBox.Show(
                    "Some changes may require a restart to take effect.\nRestart now?",
                    "Restart Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (restart == DialogResult.Yes)
                {
                    DialogResult = DialogResult.Yes;
                    Application.Restart();
                    Environment.Exit(0);
                }
                else
                {
                    DialogResult = DialogResult.OK;
                }
            }
            else
            {
                DialogResult = DialogResult.OK;
                if (!appliedImmediately && ConfigChanged)
                {
                    MessageBox.Show(
                        "Configuration saved. Changes will take effect on the next start.",
                        "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            Close();
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// Prompts the user if they try to close the window without saving.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.None &&
                CheckForConfigurationChanges())
            {
                var res = MessageBox.Show(
                    "Configuration has changed but was not saved. Discard changes?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (res == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
            base.OnFormClosing(e);
        }
    }
}
