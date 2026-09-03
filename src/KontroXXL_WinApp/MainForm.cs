using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace KontroXXL_WinApp
{
    // --- CUSTOM CONTROLS ---

    public static class NativeMethods {
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
    }

    /// <summary>
    /// Panel that strips native scrollbars at Win32 level via CreateParams.
    /// Scroll position is still tracked internally — only the scrollbar UI is hidden.
    /// </summary>
    internal class NoScrollPanel : Panel {
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.Style &= ~0x00100000; // WS_HSCROLL
                cp.Style &= ~0x00200000; // WS_VSCROLL
                return cp;
            }
        }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x114 || m.Msg == 0x115) return;
            base.WndProc(ref m);
        }
    }

    public class ModernButton : Button
    {
        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(40, 40, 50);
            ForeColor = Color.White;
            Font = new Font("Segoe UI Semibold", 9);
            Cursor = Cursors.Hand;
            Size = new Size(100, 35);
        }
        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            if (this.ClientRectangle.Width > 0 && this.ClientRectangle.Height > 0)
            {
                using (Pen p = new Pen(Color.FromArgb(60, 60, 80), 1))
                    pevent.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    public class DonutProgress : Control
    {
        public float Value { get; set; } = 0;
        public float MaxValue { get; set; } = 100;
        public Color ProgressColor { get; set; } = Color.FromArgb(0, 192, 255);
        public string Title { get; set; } = "";
        public string Unit { get; set; } = "";

        public DonutProgress() {
            this.DoubleBuffered = true;
            this.Size = new Size(140, 170);
            this.BackColor = Color.FromArgb(10, 10, 15);
        }

        private static readonly Font FontValue = new Font("Impact", 18);
        private static readonly Font FontTitle = new Font("Segoe UI Semibold", 8);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(20, 15, 100, 100);
            float angle = Math.Max(0, Math.Min(360f, (Value / (MaxValue > 0 ? MaxValue : 100)) * 360f));

            using (Pen p = new Pen(Color.FromArgb(30, 30, 40), 10)) e.Graphics.DrawEllipse(p, rect);

            if (angle > 1) {
                using (GraphicsPath path = new GraphicsPath()) {
                    path.AddArc(rect, -90, angle);
                    using (Pen glow = new Pen(Color.FromArgb(40, ProgressColor), 14)) {
                        glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                        e.Graphics.DrawPath(glow, path);
                    }
                }
            }

            using (Pen p = new Pen(ProgressColor, 10)) {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                if (angle > 0.1) e.Graphics.DrawArc(p, rect, -90, angle);
            }

            string valStr = $"{Value:F0}{Unit}";
            SizeF szVal = e.Graphics.MeasureString(valStr, FontValue);
            e.Graphics.DrawString(valStr, FontValue, Brushes.White, (Width - szVal.Width) / 2, (110 - szVal.Height) / 2 + 15);

            SizeF szTitle = e.Graphics.MeasureString(Title.ToUpper(), FontTitle);
            e.Graphics.DrawString(Title.ToUpper(), FontTitle, Brushes.LightGray, (Width - szTitle.Width) / 2, 130);
        }
    }

    public class LineChart : Control
    {
        private readonly float[] _ring;
        private int _head = 0;
        private int _count = 0;
        private readonly int maxPts = 50;
        private readonly object _lock = new object();
        public Color ChartColor { get; set; } = Color.FromArgb(0, 255, 200);
        private static readonly Font LabelFont = new Font("Segoe UI", 8, FontStyle.Bold);

        public LineChart() {
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(15, 15, 20);
            _ring = new float[maxPts];
        }

        public void AddData(float val) {
            lock (_lock) {
                _ring[_head] = val;
                _head = (_head + 1) % maxPts;
                if (_count < maxPts) _count++;
            }
            this.Invalidate();
        }

        private float[] GetSnapshot(out int count) {
            float[] snap = new float[maxPts];
            lock (_lock) {
                count = _count;
                int start = _count < maxPts ? 0 : _head;
                for (int i = 0; i < _count; i++)
                    snap[i] = _ring[(start + i) % maxPts];
            }
            return snap;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float[] d = GetSnapshot(out int cnt);

            using (SolidBrush sb = new SolidBrush(Color.FromArgb(10, 10, 15))) e.Graphics.FillRectangle(sb, ClientRectangle);

            using (Pen grid = new Pen(Color.FromArgb(20, 255, 255, 255), 1)) {
                for (int i = 0; i < 5; i++) {
                    int y = (Height / 4) * i;
                    e.Graphics.DrawLine(grid, 0, y, Width, y);
                }
            }

            if (cnt < 2) return;

            float maxVal = 0;
            for (int i = 0; i < cnt; i++) if (d[i] > maxVal) maxVal = d[i];
            float m = Math.Max(10f, maxVal * 1.2f);
            float stX = (float)Width / (maxPts - 1);

            using (Pen p = new Pen(ChartColor, 2)) {
                PointF[] pts = new PointF[cnt];
                for (int i = 0; i < cnt; i++) pts[i] = new PointF(i * stX, Height - (d[i] / m * Height));
                e.Graphics.DrawLines(p, pts);
            }

            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, Color.White))) {
                e.Graphics.DrawString($"MAX: {maxVal:F1}", LabelFont, b, Width - 80, 5);
                float lastY = Height - (d[cnt - 1] / m * Height) - 15;
                e.Graphics.DrawString($"{d[cnt - 1]:F1} Mbps", LabelFont, b, (cnt - 1) * stX - 50, lastY);
            }
        }
    }


    // --- MAIN FORM ---

    public partial class MainForm : Form
    {
        private AppConfig config;

        // Faz 2 (A5): Ayarlar kaydedilirken duz metin anahtari sifreli alana yansitmak icin.
        public KontroXXL.Core.Security.ISecretProtector Secrets { get; set; }

        private Panel topBar, sideNav, contentContainer;
        private Dictionary<string, Panel> tabPanels = new Dictionary<string, Panel>();
        private Dictionary<string, Button> tabButtons = new Dictionary<string, Button>();
        private Dictionary<string, Panel> tabAccentBars = new Dictionary<string, Panel>();
        private Dictionary<string, System.Windows.Forms.Timer> scrollTimers = new Dictionary<string, System.Windows.Forms.Timer>();

        // Stats
        private DonutProgress dntPcCpu, dntPcRam, dntPcGpu, dntPcGpuTemp, dntPcGpuFan, dntNasCpu, dntNasTemp;
        private Label lblPcNet, lblCpuFreq, lblNasLoad, lblNasNet, lblNasMem;
        private LineChart chartPcNet, chartNasNet;
        private FlowLayoutPanel flowNasPools, flowNasAlerts, flowNasServices;
        private FlowLayoutPanel flowRealApps;

        private TextBox txtNasIp, txtNasKey;
        private Label lblSecretWarning;
        private ComboBox cmbComPort;
        private CheckBox chkEnableNas, chkEnableArduino, chkEnableShortcuts, chkStartWithWindows;
        private ListBox lstShortcuts;

        private string lastPoolSig = "", lastAlertSig = "", lastSvcSig = "", lastAppSig = "";

        public event Action<string, string> OnAppAction;
        public event Action<string, string, string> OnServiceAction;
        public event Action<string> OnNasPower;
        public event Action OnNasDismissAlerts;
        public event Action OnShortcutsUpdate;
        public event Action OnSettingsSaved;

        private static readonly Color BgDark    = Color.FromArgb(15, 15, 20);
        private static readonly Color BgMid     = Color.FromArgb(20, 20, 28);
        private static readonly Color BgCard    = Color.FromArgb(28, 28, 38);
        private static readonly Color Accent    = Color.FromArgb(0, 120, 215);
        private static readonly Color AccentGlow = Color.DeepSkyBlue;

        // A1: WinForms'ta Controls.Clear() çocukları DISPOSE ETMEZ. Her yenilemede
        // Panel/Label/Button ve içlerindeki Font nesneleri sızıyordu -> GDI handle
        // tükeniyor, saatler içinde OutOfMemoryException geliyordu (Release_v2/crash.log).
        private static void ClearAndDispose(Control.ControlCollection controls)
        {
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                Control c = controls[i];
                controls.RemoveAt(i);
                c.Dispose();
            }
        }

        // A1: satır başına yeni Font yerine paylaşılan, hiç dispose edilmeyen tek örnekler.
        private static readonly Font FontRowTitle  = new Font("Segoe UI Bold", 9);
        private static readonly Font FontRowTitleL = new Font("Segoe UI Bold", 10);
        private static readonly Font FontRowState  = new Font("Segoe UI Bold", 8);
        private static readonly Font FontRowBody   = new Font("Segoe UI", 9);
        private static readonly Font FontRowSmall  = new Font("Segoe UI", 8);
        private static readonly Font FontRowButton = new Font("Segoe UI", 8, FontStyle.Bold);
        private static readonly Font FontSemibold9 = new Font("Segoe UI Semibold", 9);

        public MainForm(AppConfig config) {
            this.config = config;
            InitializeComponent();
            LoadConfigToUI();
            SwitchTab("Dashboard");
        }

        private void InitializeComponent() {
            // Fixed window — no resize, always centered
            this.ClientSize = new Size(1000, 680);
            this.MinimumSize = this.MaximumSize = new Size(1000, 680);
            this.Text = "KONTROXXL";
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = BgDark;
            this.StartPosition = FormStartPosition.CenterScreen;
            Region = System.Drawing.Region.FromHrgn(NativeMethods.CreateRoundRectRgn(0, 0, Width, Height, 12, 12));

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath)) this.Icon = new Icon(iconPath);
            else this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 10);

            // Thin border glow
            this.Padding = new Padding(1);
            this.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(0, 100, 180), 1))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            BuildTopBar();
            BuildSideNav();

            contentContainer = new Panel() {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 15),
                Padding = new Padding(16, 12, 16, 12)
            };

            this.Controls.Add(contentContainer);
            this.Controls.Add(sideNav);
            this.Controls.Add(topBar);

            SetupDashboardTab();
            SetupNasDashboardTab();
            SetupNasAppsTab();
            SetupShortcutsTab();
            SetupSettingsTab();
        }

        private void BuildTopBar() {
            topBar = new Panel() { Dock = DockStyle.Top, Height = 48, BackColor = BgMid };
            topBar.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };

            // App title
            Label title = new Label() {
                Text = "KONTROXXL",
                ForeColor = AccentGlow,
                Font = new Font("Impact", 14),
                Location = new Point(16, 12),
                AutoSize = true
            };
            topBar.Controls.Add(title);

            // Version badge
            Label ver = new Label() {
                Text = "v2.0",
                ForeColor = Color.FromArgb(80, 140, 200),
                Font = new Font("Segoe UI Semibold", 8),
                Location = new Point(120, 18),
                AutoSize = true
            };
            topBar.Controls.Add(ver);

            // Status label (live indicator)
            Label status = new Label() {
                Text = "● LIVE",
                ForeColor = Color.LimeGreen,
                Font = new Font("Segoe UI Semibold", 8),
                Location = new Point(175, 18),
                AutoSize = true,
                Name = "lblStatus"
            };
            topBar.Controls.Add(status);

            // Close button
            Button btnClose = new Button() {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 48,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(180, 180, 180),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 30, 30);
            btnClose.Click += (s, e) => this.Hide();
            topBar.Controls.Add(btnClose);

            // Minimize button
            Button btnMin = new Button() {
                Text = "─",
                Dock = DockStyle.Right,
                Width = 48,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(180, 180, 180),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10)
            };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 50, 65);
            btnMin.Click += (s, e) => this.WindowState = FormWindowState.Minimized;
            topBar.Controls.Add(btnMin);

            // Bottom separator line
            Panel sep = new Panel() { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(40, 40, 55) };
            topBar.Controls.Add(sep);
        }

        private void BuildSideNav() {
            sideNav = new Panel() { Dock = DockStyle.Left, Width = 200, BackColor = BgMid };

            // App logo area at top of nav
            Panel logoArea = new Panel() { Dock = DockStyle.Top, Height = 20, BackColor = BgMid };
            sideNav.Controls.Add(logoArea);

            // Right separator
            Panel navSep = new Panel() { Dock = DockStyle.Right, Width = 1, BackColor = Color.FromArgb(40, 40, 55) };
            sideNav.Controls.Add(navSep);

            string[] navKeys   = { "Dashboard", "NasDashboard", "NasApps", "Shortcuts", "Settings" };
            string[] navLabels = { "PC DASHBOARD", "NAS DASHBOARD", "NAS APPS", "QUICK ACTIONS", "SYSTEM CONFIG" };
            string[] navIcons  = { "🏠", "📊", "📦", "🚀", "⚙️" };

            foreach (var (key, label, icon) in navKeys.Zip(navLabels, (k, l) => (k, l, "")).Zip(navIcons, (kl, ic) => (kl.k, kl.l, ic)))
                AddNavBtn(key, icon, label);
        }

        private void AddNavBtn(string k, string icon, string label) {
            // Accent bar (left edge, shown when active)
            Panel accentBar = new Panel() {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = Accent,
                Visible = false
            };

            // Icon label
            Label icLbl = new Label() {
                Text = icon,
                Location = new Point(12, 14),
                Size = new Size(28, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11)
            };

            // Text label
            Label txtLbl = new Label() {
                Text = label,
                Location = new Point(44, 16),
                Size = new Size(148, 20),
                ForeColor = Color.FromArgb(160, 160, 180),
                Font = new Font("Segoe UI Semibold", 8.5f),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Panel btnPanel = new Panel() {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = BgMid,
                Cursor = Cursors.Hand
            };
            btnPanel.Controls.Add(accentBar);
            btnPanel.Controls.Add(icLbl);
            btnPanel.Controls.Add(txtLbl);

            Action hover = () => { if (!accentBar.Visible) btnPanel.BackColor = Color.FromArgb(30, 30, 42); };
            Action unhover = () => { if (!accentBar.Visible) btnPanel.BackColor = BgMid; };
            btnPanel.MouseEnter += (s, e) => hover();
            btnPanel.MouseLeave += (s, e) => unhover();
            icLbl.MouseEnter  += (s, e) => hover();
            icLbl.MouseLeave  += (s, e) => unhover();
            txtLbl.MouseEnter += (s, e) => hover();
            txtLbl.MouseLeave += (s, e) => unhover();

            btnPanel.Click += (s, e) => SwitchTab(k);
            icLbl.Click   += (s, e) => SwitchTab(k);
            txtLbl.Click  += (s, e) => SwitchTab(k);

            sideNav.Controls.Add(btnPanel);
            btnPanel.BringToFront();

            // Store references using accentBar as the active indicator, Button reuse dict for txtLbl
            tabAccentBars[k] = accentBar;
            // Wrap in a dummy button for color tracking
            var dummy = new Button() { Tag = new object[] { btnPanel, accentBar, txtLbl } };
            tabButtons[k] = dummy;
        }

        private void SwitchTab(string k) {
            foreach (var kvp in tabPanels) {
                kvp.Value.Visible = false;
                if (scrollTimers.ContainsKey(kvp.Key)) scrollTimers[kvp.Key].Stop();
            }
            foreach (var kvp in tabButtons.Values) {
                var parts = kvp.Tag as object[];
                if (parts == null) continue;
                var panel = parts[0] as Panel;
                var accent = parts[1] as Panel;
                var lbl = parts[2] as Label;
                if (panel != null) panel.BackColor = BgMid;
                if (accent != null) accent.Visible = false;
                if (lbl != null) lbl.ForeColor = Color.FromArgb(160, 160, 180);
            }

            if (tabPanels.ContainsKey(k)) tabPanels[k].Visible = true;
            if (scrollTimers.ContainsKey(k)) scrollTimers[k].Start();

            if (tabButtons.ContainsKey(k)) {
                var parts = tabButtons[k].Tag as object[];
                if (parts != null) {
                    var panel = parts[0] as Panel;
                    var accent = parts[1] as Panel;
                    var lbl = parts[2] as Label;
                    if (panel != null) panel.BackColor = Color.FromArgb(22, 22, 35);
                    if (accent != null) accent.Visible = true;
                    if (lbl != null) lbl.ForeColor = Color.White;
                }
            }
        }

        // --- Tab setup helpers ---

        private Label MakeSectionHeader(string text, Color color) =>
            new Label() {
                Text = text,
                Font = new Font("Impact", 11),
                ForeColor = color,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

        private void SetupDashboardTab() {
            Panel p = new NoScrollPanel() { Dock = DockStyle.Fill, Visible = false, AutoScroll = true };
            ApplyCustomScroll(p, "Dashboard");

            // Donut row
            FlowLayoutPanel fDonuts = new FlowLayoutPanel() {
                Dock = DockStyle.Top,
                Height = 220,
                Padding = new Padding(0, 8, 0, 0),
                WrapContents = false
            };

            Panel pCpu = new Panel() { Size = new Size(140, 200), Margin = new Padding(0, 0, 10, 0) };
            dntPcCpu = new DonutProgress() { Title = "CPU", Unit = "%", ProgressColor = AccentGlow, Dock = DockStyle.Top };
            lblCpuFreq = new Label() {
                Text = "0.00 GHz",
                Font = new Font("Segoe UI Semibold", 10),
                ForeColor = Color.Gainsboro,
                Dock = DockStyle.Bottom,
                TextAlign = ContentAlignment.TopCenter,
                Height = 28
            };
            pCpu.Controls.Add(dntPcCpu);
            pCpu.Controls.Add(lblCpuFreq);

            dntPcRam     = new DonutProgress() { Title = "RAM",      Unit = "%",  ProgressColor = Color.MediumPurple };
            dntPcGpu     = new DonutProgress() { Title = "GPU",      Unit = "%",  ProgressColor = Color.LimeGreen };
            dntPcGpuTemp = new DonutProgress() { Title = "GPU TEMP", Unit = "°C", ProgressColor = Color.OrangeRed };
            dntPcGpuFan  = new DonutProgress() { Title = "GPU FAN",  Unit = "%",  ProgressColor = Color.White };

            fDonuts.Controls.Add(pCpu);
            fDonuts.Controls.Add(dntPcRam);
            fDonuts.Controls.Add(dntPcGpu);
            fDonuts.Controls.Add(dntPcGpuTemp);
            fDonuts.Controls.Add(dntPcGpuFan);

            // Network section — Dock.Top (not Fill) so AutoScroll works correctly
            Panel pNet = new Panel() { Dock = DockStyle.Top, Height = 240, Padding = new Padding(0, 12, 0, 0) };
            lblPcNet = new Label() {
                Text = "PC AĞ HIZI",
                Font = new Font("Impact", 12),
                Location = new Point(0, 0),
                AutoSize = true,
                ForeColor = Color.Cyan
            };
            chartPcNet = new LineChart() { Location = new Point(0, 32), Size = new Size(720, 180), ChartColor = Color.Cyan };
            pNet.Controls.Add(lblPcNet);
            pNet.Controls.Add(chartPcNet);

            // pNet added first (lower index), fDonuts second (higher index).
            // WinForms docks in reverse-index order: fDonuts goes to top, pNet below it.
            p.Controls.Add(pNet);
            p.Controls.Add(fDonuts);
            contentContainer.Controls.Add(p);
            tabPanels["Dashboard"] = p;
        }

        private void SetupNasDashboardTab() {
            Panel p = new NoScrollPanel() { Dock = DockStyle.Fill, Visible = false, AutoScroll = true, BackColor = Color.FromArgb(10, 10, 15) };
            ApplyCustomScroll(p, "NasDashboard");

            // Top stats row
            FlowLayoutPanel fTop = new FlowLayoutPanel() {
                Dock = DockStyle.Top,
                Height = 195,
                Padding = new Padding(0, 8, 0, 0),
                AutoSize = false,
                WrapContents = false
            };

            dntNasCpu  = new DonutProgress() { Title = "NAS CPU",  Unit = "%",  ProgressColor = AccentGlow };
            dntNasTemp = new DonutProgress() { Title = "NAS TEMP", Unit = "°C", ProgressColor = Color.OrangeRed };

            Panel pInfo = new Panel() { Size = new Size(400, 185), Margin = new Padding(20, 0, 0, 0) };
            lblNasLoad = new Label() { Text = "Load Avg: --",    Font = new Font("Segoe UI Semibold", 10), Location = new Point(0, 5),  AutoSize = true };
            lblNasNet  = new Label() { Text = "Net Activity: --", Font = new Font("Impact", 12),           Location = new Point(0, 30), AutoSize = true, ForeColor = Color.LightSkyBlue };
            lblNasMem  = new Label() { Text = "Memory: --",      Font = new Font("Segoe UI", 9),           Location = new Point(0, 58), AutoSize = true, ForeColor = Color.Gainsboro };
            chartNasNet = new LineChart() { Location = new Point(0, 80), Size = new Size(380, 70) };

            Button btnNasReboot = new Button() {
                Text = "↻  REBOOT NAS", Location = new Point(230, 0), Width = 155, Height = 30,
                BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Bold", 8), Cursor = Cursors.Hand
            };
            btnNasReboot.FlatAppearance.BorderSize = 1;
            btnNasReboot.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
            btnNasReboot.Click += (s, e) => {
                if (MessageBox.Show("NAS cihazını yeniden başlatmak istiyor musunuz?", "NAS REBOOT", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    OnNasPower?.Invoke("REBOOT");
            };

            Button btnNasShutdown = new Button() {
                Text = "⏻  SHUTDOWN NAS", Location = new Point(230, 36), Width = 155, Height = 30,
                BackColor = Color.FromArgb(60, 20, 20), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Bold", 8), Cursor = Cursors.Hand
            };
            btnNasShutdown.FlatAppearance.BorderSize = 1;
            btnNasShutdown.FlatAppearance.BorderColor = Color.FromArgb(100, 40, 40);
            btnNasShutdown.Click += (s, e) => {
                if (MessageBox.Show("NAS cihazını kapatmak istiyor musunuz?", "NAS SHUTDOWN", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    OnNasPower?.Invoke("SHUTDOWN");
            };

            pInfo.Controls.Add(lblNasLoad);
            pInfo.Controls.Add(lblNasNet);
            pInfo.Controls.Add(lblNasMem);
            pInfo.Controls.Add(chartNasNet);
            pInfo.Controls.Add(btnNasReboot);
            pInfo.Controls.Add(btnNasShutdown);

            fTop.Controls.Add(dntNasCpu);
            fTop.Controls.Add(dntNasTemp);
            fTop.Controls.Add(pInfo);
            p.Controls.Add(fTop);

            // Sections stacked in a FlowLayoutPanel (TopDown) to prevent overlap.
            // Dock.Top + AutoSize lets it grow vertically so the parent NoScrollPanel can scroll.
            FlowLayoutPanel fSections = new FlowLayoutPanel() {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 8, 0, 0)
            };

            // Storage Pools
            fSections.Controls.Add(MakeSectionHeader("STORAGE POOLS", Color.Gray));
            flowNasPools = new FlowLayoutPanel() {
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(740, 0),
                BackColor = Color.FromArgb(15, 15, 20),
                Margin = new Padding(0, 0, 0, 16)
            };
            fSections.Controls.Add(flowNasPools);

            // Alerts header row
            Panel alertHeader = new Panel() { Size = new Size(720, 32), Margin = new Padding(0) };
            alertHeader.Controls.Add(MakeSectionHeader("SYSTEM ALERTS", Color.Salmon));
            Button btnDismissAlerts = new Button() {
                Text = "🔔 DISMISS ALL", Location = new Point(580, 2), Width = 140, Height = 26,
                BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.Salmon,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Bold", 8), Cursor = Cursors.Hand
            };
            btnDismissAlerts.FlatAppearance.BorderSize = 1;
            btnDismissAlerts.FlatAppearance.BorderColor = Color.FromArgb(100, 80, 80);
            btnDismissAlerts.Click += (s, e) => {
                if (MessageBox.Show("Tüm uyarıları temizlemek istiyor musunuz?", "ALERTS", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    OnNasDismissAlerts?.Invoke();
            };
            alertHeader.Controls.Add(btnDismissAlerts);
            fSections.Controls.Add(alertHeader);

            flowNasAlerts = new FlowLayoutPanel() {
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(740, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(15, 15, 20),
                Margin = new Padding(0, 0, 0, 16)
            };
            fSections.Controls.Add(flowNasAlerts);

            fSections.Controls.Add(MakeSectionHeader("ACTIVE SERVICES", Color.MediumSeaGreen));
            flowNasServices = new FlowLayoutPanel() {
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(740, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(15, 15, 20),
                Margin = new Padding(0, 0, 0, 16)
            };
            fSections.Controls.Add(flowNasServices);

            p.Controls.Add(fSections);
            contentContainer.Controls.Add(p);
            tabPanels["NasDashboard"] = p;
        }

        private void SetupNasAppsTab() {
            Panel p = new NoScrollPanel() { Dock = DockStyle.Fill, Visible = false, AutoScroll = true, BackColor = Color.FromArgb(10, 10, 15) };
            ApplyCustomScroll(p, "NasApps");

            Panel hdr = new Panel() { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0, 8, 0, 0) };
            hdr.Controls.Add(new Label() {
                Text = "MANAGED APPLICATIONS",
                Font = new Font("Impact", 14),
                ForeColor = AccentGlow,
                Location = new Point(0, 10),
                AutoSize = true
            });
            p.Controls.Add(hdr);

            flowRealApps = new FlowLayoutPanel() {
                Dock = DockStyle.Top,
                AutoScroll = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(740, 0),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(15, 15, 20)
            };
            p.Controls.Add(flowRealApps);
            contentContainer.Controls.Add(p);
            tabPanels["NasApps"] = p;
        }

        private void SetupShortcutsTab() {
            Panel p = new Panel() { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(10, 10, 15), Padding = new Padding(0, 8, 0, 0) };

            Label hdr = new Label() {
                Text = "QUICK ACTIONS",
                Font = new Font("Impact", 14),
                ForeColor = AccentGlow,
                Location = new Point(0, 0),
                AutoSize = true
            };
            p.Controls.Add(hdr);

            Label sub = new Label() {
                Text = "Sık kullandığınız uygulamalar ve araçlar",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 140),
                Location = new Point(0, 32),
                AutoSize = true
            };
            p.Controls.Add(sub);

            lstShortcuts = new ListBox() {
                Location = new Point(0, 62),
                Size = new Size(500, 460),
                BackColor = Color.FromArgb(20, 20, 30),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle,
                ItemHeight = 28
            };
            p.Controls.Add(lstShortcuts);

            int bx = 520;
            ModernButton bA = new ModernButton() {
                Text = "+  EKLE",
                Location = new Point(bx, 62),
                Width = 160,
                Height = 38,
                BackColor = Color.FromArgb(20, 80, 30)
            };
            bA.Click += (s, e) => AddShortcutDialog();

            ModernButton bD = new ModernButton() {
                Text = "✕  SİL",
                Location = new Point(bx, 108),
                Width = 160,
                Height = 38,
                BackColor = Color.FromArgb(80, 20, 20)
            };
            bD.Click += (s, e) => {
                if (lstShortcuts.SelectedIndex >= 0) {
                    config.Shortcuts.RemoveAt(lstShortcuts.SelectedIndex);
                    config.Save();
                    RefreshShortcuts();
                    OnShortcutsUpdate?.Invoke();
                }
            };

            p.Controls.Add(bA);
            p.Controls.Add(bD);
            contentContainer.Controls.Add(p);
            tabPanels["Shortcuts"] = p;
            RefreshShortcuts();
        }

        private void SetupSettingsTab() {
            Panel p = new NoScrollPanel() { Dock = DockStyle.Fill, Visible = false, AutoScroll = true, BackColor = Color.FromArgb(10, 10, 15) };
            ApplyCustomScroll(p, "Settings");

            int y = 8;

            p.Controls.Add(new Label() { Text = "SYSTEM CONFIGURATION", Font = new Font("Impact", 13), ForeColor = AccentGlow, Location = new Point(0, y), AutoSize = true }); y += 40;

            // TrueNAS section
            p.Controls.Add(new Label() { Text = "— TrueNAS Bağlantısı —", Font = new Font("Segoe UI Semibold", 9), ForeColor = Color.FromArgb(100, 140, 200), Location = new Point(0, y), AutoSize = true }); y += 28;
            p.Controls.Add(new Label() { Text = "IP Adresi:", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Silver }); y += 22;
            txtNasIp = new TextBox() { Location = new Point(0, y), Width = 260, BackColor = BgCard, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            p.Controls.Add(txtNasIp); y += 40;

            p.Controls.Add(new Label() { Text = "API Token:", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Silver }); y += 22;
            txtNasKey = new TextBox() { Location = new Point(0, y), Width = 420, PasswordChar = '●', BackColor = BgCard, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10) };
            p.Controls.Add(txtNasKey); y += 24;

            // A5: sifreli anahtar cozulemediyse kullaniciyi bilgilendir — sessiz bos alan yaniltici olur.
            lblSecretWarning = new Label() {
                Text = "⚠ Kayıtlı anahtar çözülemedi, lütfen yeniden girin.",
                Location = new Point(0, y),
                AutoSize = true,
                ForeColor = Color.Tomato,
                Font = new Font("Segoe UI Semibold", 8),
                Visible = false
            };
            p.Controls.Add(lblSecretWarning); y += 24;

            // Arduino section
            p.Controls.Add(new Label() { Text = "— Arduino Bağlantısı —", Font = new Font("Segoe UI Semibold", 9), ForeColor = Color.FromArgb(100, 140, 200), Location = new Point(0, y), AutoSize = true }); y += 28;
            p.Controls.Add(new Label() { Text = "Serial COM Port:", Location = new Point(0, y), AutoSize = true, ForeColor = Color.Silver }); y += 22;
            cmbComPort = new ComboBox() {
                Location = new Point(0, y), Width = 150,
                BackColor = BgCard, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10)
            };
            try { cmbComPort.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames()); } catch { }
            p.Controls.Add(cmbComPort); y += 48;

            // Toggles section
            p.Controls.Add(new Label() { Text = "— Modül Ayarları —", Font = new Font("Segoe UI Semibold", 9), ForeColor = Color.FromArgb(100, 140, 200), Location = new Point(0, y), AutoSize = true }); y += 28;
            chkEnableNas       = CreateCh("NAS İzleme Aktif",       0, y, p); y += 32;
            chkEnableArduino   = CreateCh("Arduino Senkronizasyon", 0, y, p); y += 32;
            chkEnableShortcuts = CreateCh("Hızlı Eylemler Aktif",   0, y, p); y += 32;
            chkStartWithWindows = CreateCh("Windows ile Başlat",    0, y, p); y += 52;

            ModernButton bS = new ModernButton() {
                Text = "KAYDET",
                Location = new Point(0, y),
                Width = 180,
                Height = 42,
                BackColor = Accent,
                Font = new Font("Segoe UI Bold", 10)
            };
            bS.Click += (s, e) => SaveConfig();
            p.Controls.Add(bS);

            contentContainer.Controls.Add(p);
            tabPanels["Settings"] = p;
        }

        private void ApplyCustomScroll(Panel p, string tabKey) {
            p.AutoScroll = true;

            Panel thumb = new Panel() {
                Width = 5,
                BackColor = Color.FromArgb(100, 0, 180, 255),
                Cursor = Cursors.Hand,
                Visible = false
            };
            p.Controls.Add(thumb);
            thumb.BringToFront();

            thumb.MouseEnter += (s, e) => thumb.BackColor = Color.FromArgb(200, 0, 200, 255);
            thumb.MouseLeave += (s, e) => thumb.BackColor = Color.FromArgb(100, 0, 180, 255);

            bool dragging = false; int dragStartY = 0; int dragStartScroll = 0;
            thumb.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragStartY = Cursor.Position.Y; dragStartScroll = -p.AutoScrollPosition.Y; } };
            thumb.MouseMove += (s, e) => {
                if (!dragging) return;
                int delta = Cursor.Position.Y - dragStartY;
                int vH = p.DisplayRectangle.Height, pH = p.Height;
                int maxScroll = vH - pH;
                if (maxScroll > 0) {
                    int tH = Math.Max(40, (int)(pH * (double)pH / vH));
                    int newScroll = dragStartScroll + (int)(delta * (double)maxScroll / (pH - tH));
                    p.AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(newScroll, maxScroll)));
                }
            };
            thumb.MouseUp += (s, e) => dragging = false;

            // Timer stopped by default — SwitchTab starts it only for the visible panel
            System.Windows.Forms.Timer tScroll = new System.Windows.Forms.Timer() { Interval = 80 };
            tScroll.Tick += (s, e) => {
                if (p.IsDisposed) return;
                int vHeight = p.DisplayRectangle.Height;
                int pHeight = p.Height;
                if (vHeight > pHeight + 2) {
                    int tHeight = Math.Max(30, (int)(pHeight * (double)pHeight / vHeight));
                    int maxTop = pHeight - tHeight;
                    int scrolled = -p.AutoScrollPosition.Y;
                    int top = maxTop == 0 ? 0 : (int)((double)scrolled * maxTop / (vHeight - pHeight));
                    thumb.Height = tHeight;
                    thumb.Left = p.Width - thumb.Width - 2;
                    thumb.Top = Math.Max(0, Math.Min(top, maxTop));
                    thumb.Visible = true;
                    thumb.BringToFront();
                } else {
                    thumb.Visible = false;
                }
            };
            scrollTimers[tabKey] = tScroll;

            p.MouseWheel += (s, e) => {
                int current = -p.AutoScrollPosition.Y;
                int maxScroll = Math.Max(0, p.DisplayRectangle.Height - p.Height);
                int target = Math.Max(0, Math.Min(current - e.Delta, maxScroll));
                p.AutoScrollPosition = new Point(0, target);
            };
        }

        public void UpdateNasStats(int c, double rx, double tx, double l, int t, JArray pools, JArray apps, JArray alerts, string uptime, string mem, JArray services) {
            if (this.IsDisposed) return;
            this.Invoke((MethodInvoker)delegate {
                try {
                    dntNasCpu.Value = c; dntNasCpu.Invalidate();
                    dntNasTemp.Value = t; dntNasTemp.Invalidate();
                    lblNasLoad.Text = $"Load Avg: {l:F2}";
                    lblNasMem.Text  = $"Memory: {mem}";

                    double totalMbps = rx + tx;
                    lblNasNet.Text = totalMbps < 0.1
                        ? $"{(rx * 1024):F0} RX / {(tx * 1024):F0} TX KB/s"
                        : $"{rx:F1} RX / {tx:F1} TX Mbps";
                    chartNasNet.AddData((float)totalMbps);

                    // Pools
                    if (pools != null) {
                        string pSig = string.Join("|", pools.Select(x => $"{x["name"]}_{x["used"]}"));
                        if (pSig != lastPoolSig) {
                            lastPoolSig = pSig;
                            ClearAndDispose(flowNasPools.Controls);
                            foreach (var j in pools) {
                                Panel pan = new Panel() { Size = new Size(220, 68), BackColor = BgCard, Margin = new Padding(0, 0, 10, 0) };
                                pan.Controls.Add(new Label() { Text = j["name"]?.ToString(), Location = new Point(10, 10), Width = 150, AutoSize = false, Font = FontRowTitle, ForeColor = Color.White });
                                pan.Controls.Add(new Label() { Text = $"{j["used"]}%", Location = new Point(170, 10), AutoSize = true, ForeColor = Color.Gainsboro });
                                ProgressBar pb = new ProgressBar() { Location = new Point(10, 38), Size = new Size(200, 12), Value = Math.Min(100, (int)(j["used"] ?? 0)), Style = ProgressBarStyle.Continuous };
                                pan.Controls.Add(pb);
                                flowNasPools.Controls.Add(pan);
                            }
                        }
                    }

                    // Alerts
                    if (alerts != null) {
                        string aSig = string.Join("|", alerts.Select(x => x["id"]?.ToString() ?? x["text"]?.ToString() ?? ""));
                        if (aSig != lastAlertSig) {
                            lastAlertSig = aSig;
                            ClearAndDispose(flowNasAlerts.Controls);
                            if (alerts.Count == 0)
                                flowNasAlerts.Controls.Add(new Label() { Text = "✓ Aktif uyarı yok.", ForeColor = Color.LimeGreen, AutoSize = true, Margin = new Padding(4, 4, 0, 0), Font = FontSemibold9 });
                            else foreach (var al in alerts) {
                                Panel ap = new Panel() { Size = new Size(700, 44), BackColor = Color.FromArgb(40, 20, 25), Margin = new Padding(0, 0, 0, 4) };
                                string alText = al["formatted"]?.ToString() ?? al["text"]?.ToString() ?? "Alert";
                                ap.Controls.Add(new Label() { Text = "⚠ " + alText, Location = new Point(10, 12), Width = 680, AutoSize = false, ForeColor = Color.Gainsboro, Font = FontRowSmall });
                                flowNasAlerts.Controls.Add(ap);
                            }
                        }
                    }

                    // Services
                    if (services != null) {
                        string sSig = string.Join("|", services.Select(x => $"{x["service"]}_{x["state"]}"));
                        if (sSig != lastSvcSig) {
                            lastSvcSig = sSig;
                            ClearAndDispose(flowNasServices.Controls);
                            foreach (var s in services) {
                                string sName  = s["service"]?.ToString() ?? "Unknown";
                                string sState = s["state"]?.ToString() ?? "STOPPED";
                                bool isRunning = sState == "RUNNING";
                                Panel sp = new Panel() { Size = new Size(700, 44), BackColor = Color.FromArgb(22, 22, 30), Margin = new Padding(0, 0, 0, 6) };
                                Panel pInd = new Panel() { Dock = DockStyle.Left, Width = 4, BackColor = isRunning ? Color.LimeGreen : Color.Tomato };
                                sp.Controls.Add(pInd);
                                sp.Controls.Add(new Label() { Text = sName.ToUpper(), Location = new Point(16, 12), Width = 230, Font = FontRowTitle, ForeColor = Color.White });
                                sp.Controls.Add(new Label() { Text = isRunning ? "RUNNING" : "STOPPED", Location = new Point(258, 12), Width = 100, ForeColor = isRunning ? Color.LimeGreen : Color.Tomato, Font = FontRowState });
                                Button btnAct = new Button() {
                                    Text = isRunning ? "DURDUR" : "BAŞLAT",
                                    Location = new Point(550, 8), Width = 140, Height = 28,
                                    FlatStyle = FlatStyle.Flat,
                                    BackColor = isRunning ? Color.FromArgb(70, 30, 30) : Color.FromArgb(30, 70, 30),
                                    ForeColor = Color.White,
                                    Font = FontRowState,
                                    Cursor = Cursors.Hand
                                };
                                btnAct.FlatAppearance.BorderSize = 0;
                                btnAct.Click += (sender, ev) => OnServiceAction?.Invoke("1", sName, isRunning ? "STOP" : "START");
                                sp.Controls.Add(btnAct);
                                flowNasServices.Controls.Add(sp);
                            }
                        }
                    }

                    // Apps (NasApps tab)
                    if (apps != null) {
                        var sorted = new JArray(apps.OrderBy(v => v["name"]?.ToString() ?? ""));
                        string sig = string.Join("|", sorted.Select(v => v["name"]?.ToString() ?? ""));
                        if (sig != lastAppSig) {
                            lastAppSig = sig;
                            ClearAndDispose(flowRealApps.Controls);
                            foreach (var v in sorted) flowRealApps.Controls.Add(CreateAppCtrl(v));
                        } else {
                            for (int i = 0; i < sorted.Count && i < flowRealApps.Controls.Count; i++)
                                UpdateAppCtrl(flowRealApps.Controls[i], sorted[i]);
                        }
                    }
                } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("UpdateNasStats error: " + ex.Message); }
            });
        }

        public void UpdateStats(int cpu, int ram, int gpuU, int gpuT, int gpuF, double ghz, double net) {
            if (this.IsDisposed) return;
            this.Invoke((MethodInvoker)delegate {
                try {
                    dntPcCpu.Value     = cpu;  dntPcCpu.Invalidate();
                    dntPcRam.Value     = ram;  dntPcRam.Invalidate();
                    dntPcGpu.Value     = gpuU; dntPcGpu.Invalidate();
                    dntPcGpuTemp.Value = gpuT; dntPcGpuTemp.Invalidate();
                    dntPcGpuFan.Value  = gpuF; dntPcGpuFan.Invalidate();
                    lblCpuFreq.Text = $"{ghz:F2} GHz";
                    lblPcNet.Text   = $"PC AĞ HIZI: {net:F1} Mbps";
                    chartPcNet.AddData((float)net);
                } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("UpdateStats error: " + ex.Message); }
            });
        }

        private Control CreateAppCtrl(JToken a) {
            Panel p = new Panel() { Size = new Size(710, 52), BackColor = BgCard, Margin = new Padding(0, 0, 0, 6) };
            string n = a["name"]?.ToString(), st = a["state"]?.ToString();
            bool running = st == "RUNNING" || st == "ACTIVE";
            Panel pInd = new Panel() { Dock = DockStyle.Left, Width = 4, BackColor = running ? Color.LimeGreen : Color.FromArgb(80, 80, 80) };
            p.Controls.Add(pInd);
            p.Controls.Add(new Label() { Text = n, Location = new Point(16, 16), Width = 290, Font = FontRowTitleL, AutoEllipsis = true, ForeColor = Color.White });
            Label ls = new Label() { Text = st, Location = new Point(315, 16), Width = 110, ForeColor = running ? Color.LimeGreen : Color.Salmon, Tag = "state", Font = FontRowBody };
            p.Controls.Add(ls);
            Btn(438, Color.FromArgb(30, 70, 30),  "▶ START",  () => OnAppAction?.Invoke(n, "START"),   p);
            Btn(530, Color.FromArgb(70, 30, 30),  "■ STOP",   () => OnAppAction?.Invoke(n, "STOP"),    p);
            Btn(622, Color.FromArgb(50, 50, 30),  "↺ RESET",  () => OnAppAction?.Invoke(n, "RESTART"), p);
            return p;
        }

        private void Btn(int x, Color c, string t, Action a, Panel p) {
            Button b = new Button() {
                Text = t, Location = new Point(x, 11), Width = 86, Height = 30,
                FlatStyle = FlatStyle.Flat, BackColor = c, Cursor = Cursors.Hand,
                Font = FontRowButton, ForeColor = Color.White
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => a();
            p.Controls.Add(b);
        }

        private void UpdateAppCtrl(Control c, JToken a) {
            foreach (Control sub in c.Controls)
                if (sub is Label l && l.Tag?.ToString() == "state") {
                    string st = a["state"]?.ToString();
                    l.Text = st;
                    l.ForeColor = (st == "RUNNING" || st == "ACTIVE") ? Color.LimeGreen : Color.Salmon;
                }
        }

        private CheckBox CreateCh(string t, int x, int y, Panel p) {
            var c = new CheckBox() { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = Color.LightGray, Font = new Font("Segoe UI", 10) };
            p.Controls.Add(c);
            return c;
        }

        private void RefreshShortcuts() {
            lstShortcuts.Items.Clear();
            foreach (var s in config.Shortcuts) lstShortcuts.Items.Add("  " + s.Name);
        }

        private void LoadConfigToUI() {
            txtNasIp.Text   = config.TruenasIp;
            txtNasKey.Text  = config.TruenasApiKey;
            lblSecretWarning.Visible = config.SecretUnreadable;
            cmbComPort.Text = config.ArduinoPort;
            chkEnableNas.Checked       = config.EnableNasModule;
            chkEnableArduino.Checked   = config.EnableArduinoModule;
            chkEnableShortcuts.Checked = config.EnableShortcutsModule;
            chkStartWithWindows.Checked = IsSt();
        }

        private void SaveConfig() {
            // Once sirri isle. Telemetri config'i surekli kirli tuttugu icin, buradan
            // sonra yapilan HER atama 30 saniyelik flush timer'iyla diske iner —
            // "hicbir sey yazilmadi" garantisi ancak atamalardan ONCE cikarsak dogru olur.
            if (Secrets != null)
            {
                string previousKey = config.TruenasApiKey;
                config.TruenasApiKey = txtNasKey.Text;

                if (config.ApplyProtection(Secrets) == SecretApplyResult.Failed)
                {
                    config.TruenasApiKey = previousKey;   // bellegi de geri al
                    MessageBox.Show(
                        "API anahtarı şifrelenemedi. Kayıtlı anahtar korundu, değişiklik uygulanmadı.\n" +
                        "Lütfen tekrar deneyin.",
                        "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                config.TruenasApiKey = txtNasKey.Text;
            }

            // Buradan itibaren kalici degisiklikler.
            config.TruenasIp     = txtNasIp.Text;
            config.ArduinoPort   = cmbComPort.Text;
            config.EnableNasModule       = chkEnableNas.Checked;
            config.EnableArduinoModule   = chkEnableArduino.Checked;
            config.EnableShortcutsModule = chkEnableShortcuts.Checked;
            St(chkStartWithWindows.Checked);
            config.Save();
            // Etiket her zaman config.SecretUnreadable'dan turetilir — UI-yerel bir
            // override tutmuyoruz, boylece form yeniden olusturulsa bile dogru kalir.
            lblSecretWarning.Visible = config.SecretUnreadable;
            OnSettingsSaved?.Invoke();
            MessageBox.Show("Yapılandırma başarıyla kaydedildi.", "KontroXXL", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void AddShortcutDialog() {
            using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Executables (*.exe)|*.exe" })
                if (ofd.ShowDialog() == DialogResult.OK) {
                    string n = ShowInputDialog("Kısayol adı:", "Kısayol Ekle", Path.GetFileNameWithoutExtension(ofd.FileName));
                    if (!string.IsNullOrEmpty(n)) {
                        config.Shortcuts.Add(new ShortcutItem { Name = n, Path = ofd.FileName });
                        config.Save();
                        RefreshShortcuts();
                        OnShortcutsUpdate?.Invoke();
                    }
                }
        }

        private static string ShowInputDialog(string prompt, string title, string defaultValue = "") {
            using (Form f = new Form() {
                Text = title, ClientSize = new Size(340, 120),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(20, 20, 30)
            }) {
                var lbl   = new Label()   { Text = prompt, Location = new Point(12, 14), AutoSize = true, ForeColor = Color.Gainsboro };
                var txt   = new TextBox() { Text = defaultValue, Location = new Point(12, 38), Width = 312, BackColor = Color.FromArgb(30, 30, 40), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                var btnOk = new Button()  { Text = "Tamam", DialogResult = DialogResult.OK,     Location = new Point(155, 76), Width = 84, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnCan= new Button()  { Text = "İptal",  DialogResult = DialogResult.Cancel, Location = new Point(248, 76), Width = 80, BackColor = Color.FromArgb(40, 40, 50), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                btnOk.FlatAppearance.BorderSize = 0; btnCan.FlatAppearance.BorderSize = 0;
                f.AcceptButton = btnOk; f.CancelButton = btnCan;
                f.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCan });
                return f.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
            }
        }

        private bool IsSt() {
            using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                return k?.GetValue("KontroXXL") != null;
        }

        private void St(bool e) {
            try {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (e) k?.SetValue("KontroXXL", "\"" + Application.ExecutablePath + "\"");
                    else   k?.DeleteValue("KontroXXL", false);
                }
            } catch { }
        }
    }
}
