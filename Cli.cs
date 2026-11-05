namespace IoInspector;

public static class Cli
{
    // filter: null = all; integer = physical disk number; letter = drive letter
    public static bool Matches(string? filter, DiskSample d)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();
        if (int.TryParse(filter, out int n)) return d.Number == n;
        char letter = char.ToUpperInvariant(filter[0]);
        return d.Letters.Contains(letter + ":");
    }

    public static List<FileIoRow> SortedRows(IEnumerable<FileIoRow> rows, string key, bool asc)
    {
        Func<FileIoRow, object> k = key switch
        {
            "read" => r => r.ReadBytes,
            "write" => r => r.WriteBytes,
            "process" => r => r.Process,
            "pid" => r => r.Pid,
            "file" => r => r.File,
            _ => r => r.Total,
        };
        return (asc ? rows.OrderBy(k) : rows.OrderByDescending(k)).ToList();
    }

    public static int Run(Options o)
    {
        using var sampler = new Sampler();
        using var log = o.Log != null ? new StreamWriter(o.Log, append: false) { AutoFlush = true } : null;
        void Out(string s) { Console.WriteLine(s); log?.WriteLine(s); }

        var tracer = new Tracer();
        tracer.Start();
        var disks = sampler.Sample();
        Out($"# io_inspector cli  disks: {disks.Count}  etw: {(tracer.Active ? "ok" : "OFF - " + tracer.Error)}");
        if (!tracer.Active && !Program.IsAdmin)
            Out("# run as administrator for per-file attribution");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (o.Seconds == 0 || sw.Elapsed.TotalSeconds < o.Seconds)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            disks = sampler.Sample();
            foreach (var d in disks.Where(d => Matches(o.Disk, d)))
                Out($"{ts}  disk {d.Number,-2} ({(d.Letters.Length > 0 ? d.Letters : "-"),-11}) queue {d.Queue,6:F2}  {d.BytesPerSec / 1048576.0,8:F1} MB/s");
            if (tracer.Active)
            {
                var rows = tracer.Snapshot().Where(r => Tracer.MatchesFile(o.Disk, r.File, disks)).Take(o.Top).ToList();
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    Out($"    {i + 1,2}. R {Fmt(r.ReadBytes),9} W {Fmt(r.WriteBytes),9}  {r.Process}({r.Pid})  {r.File}");
                }
            }
            Thread.Sleep(1000);
        }
        tracer.Snapshot(); // flush the final partial interval into totals
        tracer.Dispose();

        if (tracer.Error == null)
        {
            Out("");
            Out($"== summary over {sw.Elapsed.TotalSeconds:F0}s  (sort: {o.Sort} {(o.Asc ? "asc" : "desc")}) ==");
            var totals = SortedRows(tracer.Totals().Where(r => Tracer.MatchesFile(o.Disk, r.File, disks)), o.Sort, o.Asc);
            for (int i = 0; i < totals.Count && i < o.Top; i++)
            {
                var r = totals[i];
                Out($"    {i + 1,2}. R {Fmt(r.ReadBytes),9} W {Fmt(r.WriteBytes),9}  {r.Process}({r.Pid})  {r.File}");
            }
        }
        return 0;
    }

    private static string Fmt(long bytes)
    {
        double mb = bytes / 1048576.0;
        return mb >= 10 ? $"{mb,7:F0} MB" : mb >= 0.1 ? $"{mb,7:F1} MB" : $"{bytes / 1024.0,7:F0} KB";
    }
}
