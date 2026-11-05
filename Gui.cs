namespace IoInspector;

public sealed class MainForm : Form
{
    private readonly Sampler _sampler = new();
    private Tracer _tracer = new();
    private bool _running;
    private readonly Dictionary<int, (Label label, Sparkline spark)> _diskRows = new();
    private readonly DataGridView _grid = new();
    private readonly ComboBox _filter = new();
    private readonly List<string?> _filterValues = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ToolStripStatusLabel _status = new();
    private readonly Button _startStop = new();
    private List<FileIoRow> _lastRows = new();
    private string _sortKey = "total";
    private bool _sortAsc;

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
        _filter.Width = 220; _filter.Left = 240; _filter.Top = 5;
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

        _startStop.Text = "Stop";
        _startStop.Left = 8; _startStop.Top = 4; _startStop.Width = 70;
        _startStop.Click += (_, _) => ToggleRunning();
        var clear = new Button { Text = "Clear", Left = 86, Top = 4, Width = 70 };
        clear.Click += (_, _) => ClearAll();
        var filterLabel = new Label { Text = "Filter:", Left = 170, Top = 9, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 34 };
        toolbar.Controls.Add(_startStop);
        toolbar.Controls.Add(clear);
        toolbar.Controls.Add(filterLabel);
        toolbar.Controls.Add(_filter);

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
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.Columns["total"]!.HeaderCell.SortGlyphDirection = SortOrder.Descending;
        _grid.ColumnHeaderMouseClick += OnSortClick;
        typeof(DataGridView).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(_grid, true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Detail", null, (_, _) => ShowDetail());
        menu.Items.Add("Terminate", null, (_, _) => TerminateSelected());
        _grid.ContextMenuStrip = menu;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[0];
            }
        };

        var strip = new StatusStrip();
        strip.Items.Add(_status);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(toolbar);
        Controls.Add(strip);

        StartTracing();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void StartTracing()
    {
        _tracer = new Tracer();
        _tracer.Start();
        _running = true;
        _startStop.Text = "Stop";
        _status.Text = _tracer.Active ? "ETW: tracing file IO" : "ETW: OFF - " + _tracer.Error + (Program.IsAdmin ? "" : " (run as administrator)");
    }

    private void ToggleRunning()
    {
        if (_running)
        {
            _tracer.Dispose();
            _running = false;
            _startStop.Text = "Start";
            _status.Text = "capture stopped (view frozen)";
        }
        else
        {
            StartTracing();
        }
    }

    private void ClearAll()
    {
        foreach (var row in _diskRows.Values) row.spark.Clear();
        _lastRows = new List<FileIoRow>();
        _grid.Rows.Clear();
    }

    private void OnSortClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var col = _grid.Columns[e.ColumnIndex];
        if (_sortKey == col.Name) _sortAsc = !_sortAsc;
        else
        {
            _sortKey = col.Name;
            _sortAsc = col.Name is "proc" or "file";
        }
        foreach (DataGridViewColumn c in _grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
        col.HeaderCell.SortGlyphDirection = _sortAsc ? SortOrder.Ascending : SortOrder.Descending;
        ShowRows(Sorted(_lastRows));
    }

    private List<FileIoRow> Sorted(List<FileIoRow> rows)
    {
        Func<FileIoRow, object> key = _sortKey switch
        {
            "proc" => r => r.Process,
            "pid" => r => r.Pid,
            "file" => r => r.File,
            "read" => r => r.ReadBytes,
            "write" => r => r.WriteBytes,
            _ => r => r.Total,
        };
        return (_sortAsc ? rows.OrderBy(key) : rows.OrderByDescending(key)).ToList();
    }

    private void ShowRows(List<FileIoRow> rows)
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var r in rows)
            _grid.Rows.Add(r.Process, r.Pid, r.File, Fmt(r.ReadBytes), Fmt(r.WriteBytes), Fmt(r.Total));
        _grid.ResumeLayout();
    }

    private void Tick()
    {
        if (!_running) return;
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
        _lastRows = _tracer.Snapshot().Where(r => Tracer.MatchesFile(filter, r.File, disks)).Take(50).ToList();
        ShowRows(Sorted(_lastRows));
    }

    private FileIoRow? SelectedRow()
    {
        if (_grid.CurrentRow?.Cells["pid"]?.Value is int pid)
            return _lastRows.FirstOrDefault(r => r.Pid == pid);
        return null;
    }

    private void ShowDetail()
    {
        var row = SelectedRow();
        if (row == null) return;
        using var dlg = new Form
        {
            Text = $"Process detail - {row.Process} ({row.Pid})",
            Width = 800, Height = 480,
            StartPosition = FormStartPosition.CenterParent,
        };
        var text = new TextBox
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10),
            Text = ProcessInfo.Describe(row.Pid),
        };
        dlg.Controls.Add(text);
        dlg.Controls.Add(new Label
        {
            Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft,
            Text = "  File: " + row.File,
        });
        dlg.ShowDialog(this);
    }

    private void TerminateSelected()
    {
        var row = SelectedRow();
        if (row == null) return;
        if (MessageBox.Show(this, $"Kill {row.Process} (pid {row.Pid}) and its entire process tree?",
                "Terminate", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var err = ProcessInfo.Kill(row.Pid);
        if (err != null)
            MessageBox.Show(this, $"Kill failed: {err}", "Terminate", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    public void Clear()
    {
        _values.Clear();
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
