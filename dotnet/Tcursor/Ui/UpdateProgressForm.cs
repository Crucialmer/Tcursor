using System.ComponentModel;
using System.Drawing.Drawing2D;
using Tcursor.Core;

namespace Tcursor.Ui;

/// <summary>
/// 更新下载进度对话框（macOS 风格，随主题深浅色）。
/// 下载在后台线程执行、进度经 <see cref="Progress{T}"/> 回到界面线程；「取消」即时生效
/// （取消后 <see cref="Cancelled"/> 为 true，调用方不再打扰用户）。
/// </summary>
internal sealed class UpdateProgressForm : MacDialogForm
{
    private readonly UpdateInfo _info;
    private readonly Label _status;
    private readonly Label _detail;
    private readonly Bar _bar;
    private readonly CancellationTokenSource _cts = new();   // 不 Dispose：后台线程可能仍在读令牌，Dispose 会与取消竞态

    /// <summary>下载完成的更新包路径（成功时非空）</summary>
    public string? PackagePath { get; private set; }

    /// <summary>失败原因（成功时为空串）</summary>
    public string Error { get; private set; } = "";

    /// <summary>是否由用户取消</summary>
    public bool Cancelled { get; private set; }

    public UpdateProgressForm(UpdateInfo info)
    {
        _info = info;
        SuspendLayout();
        Text = "Tcursor 更新";
        Font = UiTheme.Font;
        BackColor = UiTheme.WindowBg;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(440, 150 + HeaderDesignPx);

        _status = new Label
        {
            Text = $"正在下载 Tcursor {info.Version}…",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.WindowBg,
            Margin = new Padding(0, 0, 0, 10),
        };
        _bar = new Bar { Width = UiTheme.Dpi(this, 392), Height = 10, Margin = new Padding(0, 0, 0, 10), BackColor = UiTheme.WindowBg };
        _detail = new Label
        {
            Text = "正在连接更新服务…",
            AutoSize = true,
            ForeColor = UiTheme.Dim,
            BackColor = UiTheme.WindowBg,
            Margin = new Padding(0),
        };
        var cancel = UiTheme.Button("取消", width: 96);
        cancel.Click += (_, _) => _cts.Cancel();
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(0, 4, 0, 0),
            Dock = DockStyle.Fill,
        };
        buttons.Controls.Add(cancel);
        CancelButton = cancel;

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = UiTheme.WindowBg,
        };
        content.Controls.Add(_status);
        content.Controls.Add(_bar);
        content.Controls.Add(_detail);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20, 16, 20, 14), BackColor = UiTheme.WindowBg };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Task.Run(RunDownload);
    }

    private void RunDownload()
    {
        var progress = new Progress<(long Done, long Total)>(OnProgress);   // 在界面线程构造：回调自动回到界面线程
        string? package = UpdateService.Download(_info, progress, _cts.Token, out string error);
        Post(() =>
        {
            PackagePath = package;
            Error = error;
            Cancelled = _cts.IsCancellationRequested;
            DialogResult = package is null ? DialogResult.Cancel : DialogResult.OK;
            Close();
        });
    }

    private void OnProgress((long Done, long Total) p)
    {
        _detail.Text = p.Total > 0
            ? $"已下载 {p.Done / 1048576.0:F1} MB / {p.Total / 1048576.0:F1} MB（{p.Done * 100 / p.Total}%）"
            : $"已下载 {p.Done / 1048576.0:F1} MB";
        _bar.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
    }

    private void Post(Action action)
    {
        try
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(action);
        }
        catch (Exception)
        {
            // 窗口已关闭：忽略（下载结果由调用方按 DialogResult 处理）
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts.Cancel();   // 关闭即取消：下载线程随之结束
        base.OnFormClosed(e);
    }

    /// <summary>进度条（macOS 风格：系统轨道色 + 强调色填充）</summary>
    private sealed class Bar : Control
    {
        private double _value;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, 0d, 1d);
                Invalidate();
            }
        }

        public Bar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Math.Min(Height, UiTheme.Dpi(this, 8));
            if (h < 2)
                h = 2;
            var track = new Rectangle(0, (Height - h) / 2, Width, h);
            float radius = h / 2f;
            using (GraphicsPath path = MacDraw.RoundedPath(track, radius))
            using (var brush = new SolidBrush(UiTheme.TrackBg))
                g.FillPath(brush, path);
            int width = (int)Math.Round(track.Width * _value);
            if (width <= 0)
                return;
            var fill = new Rectangle(track.X, track.Y, Math.Max(h, width), h);
            using GraphicsPath fillPath = MacDraw.RoundedPath(fill, radius);
            using var fillBrush = new SolidBrush(UiTheme.Accent);
            g.FillPath(fillBrush, fillPath);
        }
    }
}
