namespace IoInspector;

public sealed class MainForm : Form
{
    private readonly Sampler _sampler = new();
    private readonly Tracer _tracer = new();
    private readonly Dictionary<int, (Label label, Sparkline spark)> _diskRows = new();
    private readonly DataGridView _grid = new();
    private readonly ComboBox _filter = new();
    private readonly List<string?> _filterValues = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ToolStripStatusLabel _status = new();

    public MainForm()
    {
        Text = "IO Inspector" + (Program.IsAdmin ? "" : "  (not elevated - no file attribution)");
        Width = 1150; Height = 680;
        StartPosition = FormStartPosition.CenterScreen;

        var disks = _sampler.Sample();

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowCount = disks.Count;
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var label = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 0, 0, 0) };
            var spark = new Sparkline { Dock = DockStyle.Fill, Margin = new Padding(2) };
            top.Controls.Add(label, 0, i);
            top.Controls.Add(spark, 1, i);
            _diskRows[d.Number] = (label, spark);
        }

        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.Dock = DockStyle.Top;
        _filterValues.Add(null); _filter.Items.Add("All disks");
        foreach (var d in disks)
        {
            _filterValues.Add(d.Number.ToString()); _filter.Items.Add($"Disk {d.Number} ({d.Letters})");
            foreach (var l in d.Letters.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                _filterValues.Add(l); _filter.Items.Add($"Drive {l}");
            }
        }
        _filter.SelectedIndex = 0;

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("proc", "Process");
        _grid.Columns.Add("pid", "PID");
        _grid.Columns.Add("file", "File");
        _grid.Columns.Add("read", "Read/s");
        _grid.Columns.Add("write", "Write/s");
        _grid.Columns.Add("total", "Total/s");
        _grid.Columns["pid"]!.FillWeight = 30;
        _grid.Columns["proc"]!.FillWeight = 70;
        _grid.Columns["file"]!.FillWeight = 140;
        _grid.Columns["read"]!.FillWeight = 45;
        _grid.Columns["write"]!.FillWeight = 45;
        _grid.Columns["total"]!.FillWeight = 45;
        typeof(DataGridView).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(_grid, true);

        var strip = new StatusStrip();
        strip.Items.Add(_status);

        Controls.Add(_grid);
        Controls.Add(_filter);
        Controls.Add(top);
        Controls.Add(strip);

        _tracer.Start();
        _status.Text = _tracer.Active ? "ETW: tracing file IO" : "ETW: OFF - " + _tracer.Error + (Program.IsAdmin ? "" : " (run as administrator)");
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        List<DiskSample> disks;
        try { disks = _sampler.Sample(); }
        catch { return; }
        foreach (var d in disks)
        {
            if (!_diskRows.TryGetValue(d.Number, out var row)) continue;
            row.label.Text = $"disk {d.Number} ({d.Letters})  queue {d.Queue:F2}  {d.BytesPerSec / 1048576.0:F1} MB/s";
            row.spark.Add(d.Queue);
        }
        if (!_tracer.Active) return;
        string? filter = _filterValues[_filter.SelectedIndex];
        var rows = _tracer.Snapshot().Where(r => Tracer.MatchesFile(filter, r.File, disks)).Take(50).ToList();
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var r in rows)
            _grid.Rows.Add(r.Process, r.Pid, r.File, Fmt(r.ReadBytes), Fmt(r.WriteBytes), Fmt(r.Total));
        _grid.ResumeLayout();
    }

    private static string Fmt(long bytes)
    {
        double mb = bytes / 1048576.0;
        return mb >= 10 ? $"{mb:F0} MB" : mb >= 0.1 ? $"{mb:F1} MB" : $"{bytes / 1024.0:F0} KB";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _tracer.Dispose();
        _sampler.Dispose();
        base.OnFormClosed(e);
    }
}

public sealed class Sparkline : Panel
{
    private readonly Queue<double> _values = new();
    private const int Cap = 120;

    public void Add(double v)
    {
        _values.Enqueue(v);
        if (_values.Count > Cap) _values.Dequeue();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(30, 30, 30));
        if (_values.Count < 2) return;
        double max = Math.Max(4, _values.Max());
        var vals = _values.ToArray();
        var pts = new PointF[vals.Length];
        for (int i = 0; i < vals.Length; i++)
            pts[i] = new PointF((float)i * Width / (Cap - 1),
                Height - 2 - (float)(vals[i] / max * (Height - 6)));
        using var pen = new Pen(Color.LimeGreen, 1.5f);
        g.DrawLines(pen, pts);
        using var brush = new SolidBrush(Color.Gray);
        g.DrawString($"max {max:F0}", Font, brush, 4, 2);
    }
}
