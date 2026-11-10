using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// v1.6 仅保留三种官方模式，不提供自定义阈值。
/// 界面同时显示注册表值和 EC 0x80/0x81 值；刷新按钮单独放在右上角。
/// 电脑管家退出后会把 EC 模式标志重置为“关闭”，本程序检测到退出后
/// 会自动重新应用当前模式（重启 MBAMainService + 写阈值）。
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(0, 120, 212);
    private static readonly Color TextPrimary = Color.FromArgb(31, 35, 40);
    private static readonly Color TextSecondary = Color.FromArgb(96, 103, 112);
    private static readonly Color CardBorder = Color.FromArgb(224, 227, 232);
    private static readonly Color Success = Color.FromArgb(22, 140, 70);
    private static readonly Color Danger = Color.FromArgb(190, 40, 40);

    private HalChargeController hal;
    private bool loading;
    private bool reapplying;
    private bool pcManagerWasRunning;
    private readonly bool demo;
    private System.Windows.Forms.Timer watchTimer;

    private CheckBox chkEnable;
    private CheckBox chkKeepAlive;
    private RadioButton radioHome;
    private RadioButton radioOffice;
    private RadioButton radioTravel;
    private Label lblMode;
    private Label lblRegistry;
    private Label lblEc;
    private Label lblBattery;
    private Label lblDriver;
    private Label lblStatus;
    private Button btnApply;
    private Button btnRestore;
    private Button btnRefresh;

    public MainForm() : this(false)
    {
    }

    public MainForm(bool demo)
    {
        this.demo = demo;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        this.Text = "荣耀笔记本充电上限";
        this.ClientSize = new Size(520, 560);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(246, 247, 250);
        this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        this.DoubleBuffered = true;

        try
        {
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        Label title = new Label();
        title.Text = "充电上限";
        title.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold, GraphicsUnit.Point);
        title.ForeColor = TextPrimary;
        title.AutoSize = true;
        title.Location = new Point(24, 14);
        this.Controls.Add(title);

        Label subtitle = new Label();
        subtitle.Text = "HONOR MagicBook · 电池安全管理模式";
        subtitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        subtitle.ForeColor = TextSecondary;
        subtitle.AutoSize = true;
        subtitle.Location = new Point(27, 54);
        this.Controls.Add(subtitle);

        Panel statusCard = CreateCard(new Point(24, 82), new Size(472, 140));
        this.Controls.Add(statusCard);

        Label currentCaption = new Label();
        currentCaption.Text = "当前模式";
        currentCaption.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        currentCaption.ForeColor = TextSecondary;
        currentCaption.AutoSize = true;
        currentCaption.Location = new Point(16, 10);
        statusCard.Controls.Add(currentCaption);

        lblMode = new Label();
        lblMode.Text = "正在读取…";
        lblMode.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point);
        lblMode.ForeColor = TextPrimary;
        lblMode.AutoSize = true;
        lblMode.Location = new Point(16, 28);
        statusCard.Controls.Add(lblMode);

        lblRegistry = new Label();
        lblRegistry.Text = "注册表：--";
        lblRegistry.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        lblRegistry.ForeColor = TextSecondary;
        lblRegistry.AutoSize = false;
        lblRegistry.AutoEllipsis = true;
        lblRegistry.Location = new Point(16, 62);
        lblRegistry.Size = new Size(350, 18);
        statusCard.Controls.Add(lblRegistry);

        lblEc = new Label();
        lblEc.Text = "EC：--";
        lblEc.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblEc.ForeColor = TextPrimary;
        lblEc.AutoSize = false;
        lblEc.AutoEllipsis = true;
        lblEc.Location = new Point(16, 84);
        lblEc.Size = new Size(350, 18);
        statusCard.Controls.Add(lblEc);

        lblBattery = new Label();
        lblBattery.Text = "电池 --";
        lblBattery.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        lblBattery.ForeColor = TextSecondary;
        lblBattery.AutoSize = false;
        lblBattery.Location = new Point(16, 108);
        lblBattery.Size = new Size(350, 20);
        statusCard.Controls.Add(lblBattery);

        btnRefresh = new Button();
        btnRefresh.Text = "刷新";
        btnRefresh.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        btnRefresh.Location = new Point(380, 18);
        btnRefresh.Size = new Size(76, 34);
        btnRefresh.FlatStyle = FlatStyle.Flat;
        btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
        btnRefresh.BackColor = Color.White;
        btnRefresh.ForeColor = TextPrimary;
        btnRefresh.Click += btnRefresh_Click;
        statusCard.Controls.Add(btnRefresh);

        Panel settingsCard = CreateCard(new Point(24, 234), new Size(472, 200));
        this.Controls.Add(settingsCard);

        chkEnable = new CheckBox();
        chkEnable.Text = "启用电池安全管理（由荣耀 MBAMainService 执行）";
        chkEnable.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        chkEnable.ForeColor = TextPrimary;
        chkEnable.AutoSize = true;
        chkEnable.Location = new Point(16, 14);
        chkEnable.CheckedChanged += chkEnable_CheckedChanged;
        settingsCard.Controls.Add(chkEnable);

        radioHome = CreateModeRadio("家用模式    70% 停止 / 40% 恢复", 48);
        settingsCard.Controls.Add(radioHome);

        radioOffice = CreateModeRadio("办公模式    90% 停止 / 70% 恢复", 78);
        settingsCard.Controls.Add(radioOffice);

        radioTravel = CreateModeRadio("差旅模式  100% 停止 / 95% 恢复", 108);
        settingsCard.Controls.Add(radioTravel);

        chkKeepAlive = new CheckBox();
        chkKeepAlive.Text = "电脑管家退出后自动重新应用模式";
        chkKeepAlive.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        chkKeepAlive.ForeColor = TextPrimary;
        chkKeepAlive.AutoSize = true;
        chkKeepAlive.Checked = true;
        chkKeepAlive.Location = new Point(16, 140);
        settingsCard.Controls.Add(chkKeepAlive);

        Label hint = new Label();
        hint.Text = "模式写入注册表（REG_SZ）并重启 MBAMainService。\r\nEC 阈值 0x80/0x81 同时写入。";
        hint.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        hint.ForeColor = TextSecondary;
        hint.Location = new Point(16, 160);
        hint.Size = new Size(440, 32);
        settingsCard.Controls.Add(hint);

        btnRestore = new Button();
        btnRestore.Text = "关闭并恢复 100%";
        btnRestore.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        btnRestore.Location = new Point(216, 448);
        btnRestore.Size = new Size(150, 38);
        btnRestore.FlatStyle = FlatStyle.Flat;
        btnRestore.FlatAppearance.BorderColor = Color.FromArgb(200, 205, 212);
        btnRestore.BackColor = Color.White;
        btnRestore.ForeColor = TextPrimary;
        btnRestore.Click += btnRestore_Click;
        this.Controls.Add(btnRestore);

        btnApply = new Button();
        btnApply.Text = "应用模式";
        btnApply.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        btnApply.Location = new Point(376, 448);
        btnApply.Size = new Size(120, 38);
        btnApply.FlatStyle = FlatStyle.Flat;
        btnApply.FlatAppearance.BorderSize = 0;
        btnApply.BackColor = Accent;
        btnApply.ForeColor = Color.White;
        btnApply.Click += btnApply_Click;
        this.Controls.Add(btnApply);

        lblDriver = new Label();
        lblDriver.Text = "BIOS HAL：正在初始化…";
        lblDriver.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        lblDriver.ForeColor = TextSecondary;
        lblDriver.AutoSize = true;
        lblDriver.Location = new Point(24, 494);
        this.Controls.Add(lblDriver);

        lblStatus = new Label();
        lblStatus.Text = "";
        lblStatus.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        lblStatus.ForeColor = TextSecondary;
        lblStatus.Location = new Point(24, 516);
        lblStatus.Size = new Size(472, 36);
        this.Controls.Add(lblStatus);

        watchTimer = new System.Windows.Forms.Timer();
        watchTimer.Interval = 2000;
        watchTimer.Tick += watchTimer_Tick;

        this.FormClosing += MainForm_FormClosing;
    }

    private RadioButton CreateModeRadio(string text, int y)
    {
        RadioButton radio = new RadioButton();
        radio.Text = text;
        radio.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        radio.ForeColor = TextPrimary;
        radio.AutoSize = true;
        radio.Location = new Point(20, y);
        return radio;
    }

    private Panel CreateCard(Point location, Size size)
    {
        Panel panel = new Panel();
        panel.Location = location;
        panel.Size = size;
        panel.BackColor = Color.White;
        panel.Paint += PaintCardBorder;
        return panel;
    }

    private static void PaintCardBorder(object sender, PaintEventArgs e)
    {
        Panel panel = (Panel)sender;
        using (Pen pen = new Pen(CardBorder))
        {
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        }
    }

    private void LoadSettings()
    {
        loading = true;
        try
        {
            bool enabled = PowerManagerConfig.IsEnabled();
            int mode = PowerManagerConfig.GetMode();

            chkEnable.Checked = enabled;
            radioHome.Checked = mode == 0;
            radioOffice.Checked = mode == 1;
            radioTravel.Checked = mode == 2;
            SetRadioEnabled(enabled);
        }
        finally
        {
            loading = false;
        }

        if (demo)
        {
            chkEnable.Checked = true;
            radioHome.Checked = true;
            lblMode.Text = "家用模式 70% / 40%";
            lblRegistry.Text = "注册表：Status=\"1\"  Mode=\"0\"（家用）";
            lblEc.Text = "EC：0x80=0x28（40%）  0x81=0x46（70%）";
            lblBattery.Text = "电池 73% · 已接电源";
            lblDriver.Text = "BIOS HAL：HONOR HardwareHal（预览）";
            lblDriver.ForeColor = TextSecondary;
            ShowStatus("预览模式：实际会读取注册表和 EC 阈值。", false);
            return;
        }

        OpenHal();
        pcManagerWasRunning = IsPcManagerRunning();
        watchTimer.Start();
        RefreshState(true);
    }

    private void OpenHal()
    {
        string error;
        hal = HalChargeController.Open(out error);

        if (hal == null)
        {
            lblMode.Text = "HAL 不可用";
            lblDriver.Text = "BIOS HAL：不可用";
            lblDriver.ForeColor = Danger;
            btnApply.Enabled = false;
            btnRestore.Enabled = false;
            btnApply.BackColor = Color.FromArgb(190, 195, 202);
            btnApply.ForeColor = Color.White;
            ShowStatus(error, true);
            return;
        }

        btnApply.Enabled = true;
        btnRestore.Enabled = true;
        btnApply.BackColor = Accent;
        btnApply.ForeColor = Color.White;
        lblDriver.Text = "BIOS HAL：" + hal.DriverName;
        lblDriver.ForeColor = TextSecondary;
    }

    private void SetRadioEnabled(bool enabled)
    {
        radioHome.Enabled = enabled;
        radioOffice.Enabled = enabled;
        radioTravel.Enabled = enabled;
    }

    private void RefreshState(bool setStatus)
    {
        bool enabled = PowerManagerConfig.IsEnabled();
        int mode = PowerManagerConfig.GetMode();
        string modeName = PowerManagerConfig.GetModeName(mode);

        lblMode.Text = enabled ? modeName : "电池安全管理已关闭";
        lblRegistry.Text = string.Format(
            "注册表：Status=\"{0}\"  Mode=\"{1}\"（{2}）",
            enabled ? "1" : "0", mode,
            mode == 0 ? "家用" : mode == 1 ? "办公" : "差旅");

        if (hal != null)
        {
            byte first;
            byte second;
            if (hal.TryReadRaw(out first, out second))
            {
                byte lower = Math.Min(first, second);
                byte upper = Math.Max(first, second);
                lblEc.Text = string.Format(
                    "EC：0x80=0x{0:X2}（{1}%）  0x81=0x{2:X2}（{3}%）",
                    lower, lower, upper, upper);
            }
            else
            {
                lblEc.Text = "EC：读取失败";
            }
        }
        else
        {
            lblEc.Text = "EC：不可用";
        }

        UpdateBattery();

        if (setStatus)
            ShowStatus("已读取注册表与 EC 值。", false);
    }

    private void UpdateBattery()
    {
        int percent = BatteryInfo.Percent;
        bool ac = BatteryInfo.OnAcPower;

        if (percent < 0)
            lblBattery.Text = "电池状态未知";
        else
            lblBattery.Text = string.Format("电池 {0}% · {1}", percent,
                ac ? "已接电源" : "使用电池");
    }

    private void chkEnable_CheckedChanged(object sender, EventArgs e)
    {
        if (loading) return;
        SetRadioEnabled(chkEnable.Checked);
    }

    private int GetSelectedMode()
    {
        if (radioOffice.Checked) return 1;
        if (radioTravel.Checked) return 2;
        return 0;
    }

    private static int ModeUpper(int mode)
    {
        switch (mode)
        {
            case 1: return 90;
            case 2: return 100;
            default: return 70;
        }
    }

    private static int ModeLower(int mode)
    {
        switch (mode)
        {
            case 1: return 70;
            case 2: return 95;
            default: return 40;
        }
    }

    private void btnApply_Click(object sender, EventArgs e)
    {
        ApplyMode(chkEnable.Checked, GetSelectedMode(), true);
    }

    private void btnRestore_Click(object sender, EventArgs e)
    {
        loading = true;
        try
        {
            chkEnable.Checked = false;
            SetRadioEnabled(false);
        }
        finally
        {
            loading = false;
        }

        ApplyMode(false, 0, true);
    }

    private void ApplyMode(bool enabled, int mode, bool showErrors)
    {
        if (hal == null)
        {
            if (showErrors)
            {
                MessageBox.Show(this, "荣耀 HAL 不可用，无法写入阈值。请以管理员身份运行，" +
                    "并确认已安装荣耀电脑管家。", "无法应用",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        int upper = enabled ? ModeUpper(mode) : 100;
        int lower = enabled ? ModeLower(mode) : 0;

        SetBusy(true);
        try
        {
            // 先写 EC 阈值，服务重启时才能读到与模式匹配的值。
            hal.SetThreshold(upper, lower);

            PowerManagerConfig.Save(enabled, mode);

            string serviceError;
            bool serviceOk = PowerManagerConfig.RestartService(out serviceError);
            Thread.Sleep(600);

            // 服务启动后可能自己写一遍阈值，这里再写一次。
            hal.SetThreshold(upper, lower);

            string modeName = enabled ? PowerManagerConfig.GetModeName(mode) : "已关闭";
            if (serviceOk)
            {
                ShowStatus(string.Format(
                    "已应用：{0}。EC：停止 {1}% / 恢复 {2}%。", modeName, upper, lower), false);
            }
            else
            {
                ShowStatus(string.Format(
                    "已写注册表和 EC，但重启 MBAMainService 失败：{0}", serviceError), true);
            }

            RefreshState(false);
        }
        catch (Exception ex)
        {
            ShowStatus("应用失败：" + ex.Message, true);
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, "应用失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        btnApply.Enabled = !busy;
        btnRestore.Enabled = !busy;
        btnRefresh.Enabled = !busy;
        chkEnable.Enabled = !busy;
        this.UseWaitCursor = busy;
    }

    private void btnRefresh_Click(object sender, EventArgs e)
    {
        if (hal == null)
        {
            OpenHal();
            if (hal != null)
                RefreshState(true);
            return;
        }

        RefreshState(true);
    }

    private static bool IsPcManagerRunning()
    {
        string[] names = new string[] { "PCManager", "PCManagerTray", "HnFrontNavigator" };
        foreach (string name in names)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    foreach (Process process in processes)
                        process.Dispose();
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private void watchTimer_Tick(object sender, EventArgs e)
    {
        bool running = IsPcManagerRunning();

        if (pcManagerWasRunning && !running && chkKeepAlive.Checked &&
            chkEnable.Checked && !reapplying && hal != null)
        {
            reapplying = true;
            try
            {
                ShowStatus("检测到电脑管家退出，正在重新应用电池安全管理模式…", false);
                Application.DoEvents();

                ApplyMode(true, GetSelectedMode(), false);
                ShowStatus("电脑管家退出后已重新应用模式。", false);
            }
            catch (Exception ex)
            {
                ShowStatus("自动恢复模式失败：" + ex.Message, true);
            }
            finally
            {
                reapplying = false;
            }
        }

        pcManagerWasRunning = running;
    }

    private void ShowStatus(string text, bool error)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = error ? Danger : Success;
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        watchTimer.Stop();

        if (hal != null)
        {
            hal.Dispose();
            hal = null;
        }
    }
}


