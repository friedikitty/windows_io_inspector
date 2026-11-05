namespace IoInspector;

public static class SelfTest
{
    public static bool Run()
    {
        bool ok = true;
        void Check(bool cond, string name, string detail = "")
        {
            Console.WriteLine($"[{(cond ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " - " + detail : "")}");
            if (!cond) ok = false;
        }

        // 1. PDH sampler
        List<DiskSample> disks;
        using (var sampler = new Sampler())
        {
            disks = sampler.Sample();
            Check(disks.Count > 0, "pdh: disks enumerated", $"{disks.Count} disks");
            Check(disks.All(d => d.Number >= 0), "pdh: instance names parsed",
                string.Join(", ", disks.Select(d => $"{d.Number}={d.Letters}")));
            Thread.Sleep(1100);
            var s2 = sampler.Sample();
            Check(s2.All(d => d.Queue >= 0 && d.BytesPerSec >= 0), "pdh: counter values readable");
        }

        // 2. volume -> drive letter map
        var vols = Tracer.BuildVolumeMap();
        Check(vols.Count > 0, "volumes mapped", $"{vols.Count} entries");

        // 3. ETW file attribution (needs admin)
        if (!Program.IsAdmin)
        {
            Console.WriteLine("[SKIP] etw: not elevated, run as administrator to test file attribution");
        }
        else
        {
            string probe = Path.Combine(Path.GetTempPath(), $"io_inspector_selftest_{Environment.ProcessId}.bin");
            using var tracer = new Tracer();
            tracer.Start();
            Check(tracer.Active, "etw: session started", tracer.Error ?? "");
            bool seen = false;
            if (tracer.Active)
            {
                var buf = new byte[8192];
                new Random(1).NextBytes(buf);
                using (var fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None))
                    for (int i = 0; i < 1024; i++) fs.Write(buf); // 8 MB
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline && !seen)
                {
                    Thread.Sleep(500);
                    seen = tracer.Snapshot().Any(r => r.File.Contains("io_inspector_selftest", StringComparison.OrdinalIgnoreCase));
                }
            }
            Check(seen, "etw: own file write observed");
            Check(tracer.Totals().Count > 0, "etw: cumulative totals accumulated");
            try { File.Delete(probe); } catch { }
        }

        // 4. filters
        var d3 = new DiskSample { Instance = "3 d: f: u:", Number = 3, Letters = "D: F: U:" };
        Check(Cli.Matches("d:", d3) && Cli.Matches("3", d3) && !Cli.Matches("C", d3), "filter: disk matching");
        Check(Tracer.MatchesFile("3", @"F:\x.bin", new List<DiskSample> { d3 })
            && Tracer.MatchesFile("u:", @"U:\x.bin", new List<DiskSample> { d3 })
            && !Tracer.MatchesFile("C:", @"F:\x.bin", new List<DiskSample> { d3 }), "filter: file matching");

        // 5. process info (self)
        Check(ProcessInfo.Describe(Environment.ProcessId).Contains("IoInspector", StringComparison.OrdinalIgnoreCase),
            "process info: self describe");

        // 6. summary sorting
        var sample = new List<FileIoRow>
        {
            new() { Pid = 1, Process = "a", File = "z", ReadBytes = 10, WriteBytes = 5 },
            new() { Pid = 2, Process = "b", File = "y", ReadBytes = 1, WriteBytes = 50 },
        };
        Check(Cli.SortedRows(sample, "write", false)[0].Pid == 2
            && Cli.SortedRows(sample, "pid", true)[0].Pid == 1
            && Cli.SortedRows(sample, "file", false)[0].File == "z", "summary sort ordering");

        Console.WriteLine(ok ? "SELFTEST OK" : "SELFTEST FAILED");
        return ok;
    }
}
