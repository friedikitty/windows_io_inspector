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

    public static int Run(Options o)
    {
        using var sampler = new Sampler();
        using var log = o.Log != null ? new StreamWriter(o.Log, append: false) { AutoFlush = true } : null;
        void Out(string s) { Console.WriteLine(s); log?.WriteLine(s); }

        Out($"# io_inspector cli  disks: {sampler.Sample().Count}  (ctrl-c to quit)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (o.Seconds == 0 || sw.Elapsed.TotalSeconds < o.Seconds)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            foreach (var d in sampler.Sample().Where(d => Matches(o.Disk, d)))
                Out($"{ts}  disk {d.Number,-2} ({(d.Letters.Length > 0 ? d.Letters : "-"),-11}) queue {d.Queue,6:F2}  {d.BytesPerSec / 1048576.0,8:F1} MB/s");
            Thread.Sleep(1000);
        }
        return 0;
    }
}
