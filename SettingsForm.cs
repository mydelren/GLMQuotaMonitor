using System.Drawing;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 设置窗口
/// </summary>
public class SettingsForm : Form
{
    private readonly ConfigService _configService;
    private readonly ThemeService _themeService;

    private TextBox _txtToken = null!;
    private Button _btnToggleToken = null!;
    private ComboBox _cmbPlatform = null!;
    private NumericUpDown _nudPolling = null!;
    private ComboBox _cmbTheme = null!;
    private CheckBox _chkFloatingBar = null!;
    private NumericUpDown _nudWarning = null!;
    private NumericUpDown _nudCritical = null!;
    private bool _tokenVisible;
    /// <summary>当前主题标记，供下拉框自绘使用（默认深色与启动主题一致）</summary>
    private bool _darkMode = true;

    public SettingsForm(ConfigService configService, ThemeService themeService)
    {
        _configService = configService;
        _themeService = themeService;

        InitializeUI();
        LoadConfigToUI();

        _themeService.ThemeChanged += OnThemeChanged;
        ApplyTheme(_themeService.IsDark);
    }

    /// <summary>
    /// 深浅色通用的下拉框自绘：WinForms 原生 ComboBox 的列表区不吃 BackColor，
    /// 不自绘的话深色模式下会保持刺眼的系统浅色
    /// </summary>
    private void ComboDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ComboBox combo) return;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = _darkMode ? Color.FromArgb(40, 40, 60) : Color.White;
        Color fg = _darkMode ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);
        if (selected)
        {
            // 浅色高亮底配白字才有足够对比；深色用同系浅一档
            bg = _darkMode ? Color.FromArgb(69, 71, 90) : SystemColors.Highlight;
            fg = _darkMode ? Color.White : SystemColors.HighlightText;
        }

        using var brush = new SolidBrush(bg);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]),
            e.Font ?? Font, e.Bounds, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
            e.DrawFocusRectangle();
    }

    private void InitializeUI()
    {
        Text = "GLM 配额监控 - 设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 392);
        ShowInTaskbar = false;
        TopMost = true;

        // 分区化布局：监控 / 外观与行为 / 预警阈值，替代原先两条无标题分隔线
        int y = 14;
        int labelX = 16;
        int inputX = 120;
        int inputWidth = 270;

        Controls.Add(CreateSection("监控", labelX, y));
        y += 24;

        // API Key
        Controls.Add(CreateLabel("API Key:", labelX, y));
        _txtToken = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth - 36, 24),
            PasswordChar = '●',
            Font = new Font("Consolas", 9f)
        };
        Controls.Add(_txtToken);

        _btnToggleToken = new Button
        {
            Text = "👁",
            Location = new Point(inputX + inputWidth - 32, y),
            Size = new Size(28, 24),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnToggleToken.Click += (_, _) =>
        {
            _tokenVisible = !_tokenVisible;
            _txtToken.PasswordChar = _tokenVisible ? '\0' : '●';
            _btnToggleToken.Text = _tokenVisible ? "🔒" : "👁";
        };
        Controls.Add(_btnToggleToken);
        y += 34;
        Controls.Add(CreateLabel("平台:", labelX, y));
        _cmbPlatform = new ComboBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat
        };
        _cmbPlatform.DrawItem += ComboDrawItem;
        _cmbPlatform.Items.AddRange(new object[] { "自动检测", "智谱 AI", "智谱 AI (dev)", "Z.ai" });
        Controls.Add(_cmbPlatform);
        y += 34;

        // 轮询间隔
        Controls.Add(CreateLabel("轮询间隔:", labelX, y));
        _nudPolling = new NumericUpDown
        {
            Location = new Point(inputX, y),
            Size = new Size(80, 24),
            Minimum = 1,
            Maximum = 30,
            Increment = 1
        };
        Controls.Add(_nudPolling);
        Controls.Add(CreateLabel("分钟", inputX + 86, y + 3));
        y += 32;

        Controls.Add(CreateSection("外观与行为", labelX, y));
        y += 24;

        // 主题
        Controls.Add(CreateLabel("主题:", labelX, y));
        _cmbTheme = new ComboBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat
        };
        _cmbTheme.DrawItem += ComboDrawItem;
        _cmbTheme.Items.AddRange(new object[] { "跟随系统", "深色", "浅色" });
        Controls.Add(_cmbTheme);
        y += 32;

        // 浮动条
        _chkFloatingBar = new CheckBox
        {
            Text = "显示浮动监控条",
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24)
        };
        Controls.Add(_chkFloatingBar);
        y += 30;

        Controls.Add(CreateSection("预警阈值", labelX, y));
        y += 24;

        // 警告阈值
        Controls.Add(CreateLabel("警告阈值:", labelX, y));
        _nudWarning = new NumericUpDown
        {
            Location = new Point(inputX, y),
            Size = new Size(80, 24),
            Minimum = 10,
            Maximum = 95,
            Increment = 5
        };
        Controls.Add(_nudWarning);
        Controls.Add(CreateLabel("%  (图标变黄)", inputX + 86, y + 3));
        y += 32;

        // 临界阈值
        Controls.Add(CreateLabel("临界阈值:", labelX, y));
        _nudCritical = new NumericUpDown
        {
            Location = new Point(inputX, y),
            Size = new Size(80, 24),
            Minimum = 20,
            Maximum = 100,
            Increment = 5
        };
        Controls.Add(_nudCritical);
        Controls.Add(CreateLabel("%  (图标变红, 弹通知)", inputX + 86, y + 3));
        y += 38;

        // 按钮
        var btnSave = new Button
        {
            Text = "保存",
            DialogResult = DialogResult.OK,
            Location = new Point(220, y),
            Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat
        };
        btnSave.Click += OnSave;
        Controls.Add(btnSave);

        var btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, y),
            Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat
        };
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private static Label CreateLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
    }

    /// <summary>分区标题（加粗），替代旧版无标题分隔线</summary>
    private static Label CreateSection(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
        };
    }

    /// <summary>
    /// 从配置加载到 UI
    /// </summary>
    private void LoadConfigToUI()
    {
        var config = _configService.Config;

        _txtToken.Text = config.AuthToken;
        _cmbPlatform.SelectedIndex = (int)config.Platform;
        _nudPolling.Value = config.GetEffectivePollingInterval();
        _cmbTheme.SelectedIndex = (int)config.Theme;
        _chkFloatingBar.Checked = config.ShowFloatingBar;
        _nudWarning.Value = config.WarningThreshold;
        _nudCritical.Value = config.CriticalThreshold;
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    private void OnSave(object? sender, EventArgs e)
    {
        // 验证阈值
        if (_nudWarning.Value >= _nudCritical.Value)
        {
            MessageBox.Show("警告阈值必须小于临界阈值", "输入错误",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var config = new AppConfig
        {
            AuthToken = _txtToken.Text.Trim(),
            Platform = (PlatformType)_cmbPlatform.SelectedIndex,
            PollingIntervalMinutes = (int)_nudPolling.Value,
            Theme = (ThemeMode)_cmbTheme.SelectedIndex,
            ShowFloatingBar = _chkFloatingBar.Checked,
            WarningThreshold = (int)_nudWarning.Value,
            CriticalThreshold = (int)_nudCritical.Value,
            // 保留浮动条位置与贴边状态（由 FloatingBar 自行更新）
            FloatingBarX = _configService.Config.FloatingBarX,
            FloatingBarY = _configService.Config.FloatingBarY,
            SnapEdgeValue = _configService.Config.SnapEdgeValue,
            SnapPosition = _configService.Config.SnapPosition
        };

        _configService.Save(config);

        Close();
    }

    private void OnThemeChanged(bool isDark)
    {
        if (InvokeRequired)
            Invoke(() => ApplyTheme(isDark));
        else
            ApplyTheme(isDark);
    }

    private void ApplyTheme(bool isDark)
    {
        _darkMode = isDark;

        Color bg = isDark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(245, 245, 250);
        Color fg = isDark ? Color.FromArgb(224, 224, 224) : Color.FromArgb(30, 30, 30);
        Color inputBg = isDark ? Color.FromArgb(40, 40, 60) : Color.White;
        Color btnBg = isDark ? Color.FromArgb(49, 50, 68) : Color.FromArgb(230, 230, 235);

        BackColor = bg;
        ForeColor = fg;

        foreach (Control ctrl in Controls)
        {
            switch (ctrl)
            {
                case TextBox tb:
                    tb.BackColor = inputBg;
                    tb.ForeColor = fg;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = inputBg;
                    nud.ForeColor = fg;
                    // NumericUpDown 的文本区是内部 TextBox，不一起改色会保持系统白色
                    foreach (Control child in nud.Controls)
                        if (child is TextBox inner)
                        {
                            inner.BackColor = inputBg;
                            inner.ForeColor = fg;
                        }
                    break;
                case ComboBox cmb:
                    cmb.BackColor = inputBg;
                    cmb.ForeColor = fg;
                    cmb.Invalidate();
                    break;
                case Button btn:
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = btnBg;
                    btn.ForeColor = fg;
                    btn.FlatAppearance.MouseOverBackColor = isDark
                        ? Color.FromArgb(69, 71, 90)
                        : Color.FromArgb(215, 215, 222);
                    break;
                case CheckBox chk:
                    chk.ForeColor = fg;
                    break;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _themeService.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }
}
