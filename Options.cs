namespace IoInspector;

public sealed class Options
{
    public bool Cli;
    public bool SelfTest;
    public bool NoElevate;    // skip GUI self-elevation (used by tests)
    public bool Demo;         // GUI with synthetic data (privacy-safe screenshots)
    public string? Disk;      // "D:" or physical disk number "3"
    public int Top = 10;
    public int Seconds;       // 0 = run forever
    public string? Log;       // tee CLI output to this file
    public string Sort = "total";   // total|read|write|process|pid|file
    public bool Asc;
    public int? DetailPid;
    public int? KillPid;

    public static bool TryParse(string[] args, out Options o)
    {
        o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a.ToLowerInvariant())
            {
                case "--cli": o.Cli = true; break;
                case "--selftest": o.SelfTest = true; break;
                case "--noelevate": o.NoElevate = true; break;
                case "--demo": o.Demo = true; break;
                case "--disk":
                    o.Disk = Next();
                    if (o.Disk == null) return false;
                    break;
                case "--top":
                    if (!int.TryParse(Next(), out int t)) return false;
                    o.Top = t; break;
                case "--seconds":
                case "--timeout":
                    if (!int.TryParse(Next(), out int s)) return false;
                    o.Seconds = s; break;
                case "--sort":
                    o.Sort = (Next() ?? "").ToLowerInvariant();
                    if (o.Sort is not ("total" or "read" or "write" or "process" or "pid" or "file")) return false;
                    break;
                case "--asc": o.Asc = true; break;
                case "--detail":
                    if (!int.TryParse(Next(), out int dp)) return false;
                    o.DetailPid = dp; break;
                case "--kill":
                    if (!int.TryParse(Next(), out int kp)) return false;
                    o.KillPid = kp; break;
                case "--log":
                    o.Log = Next();
                    if (o.Log == null) return false;
                    break;
                case "--help": case "-h": case "/?": return false;
                default:
                    Console.Error.WriteLine($"unknown argument: {a}");
                    return false;
            }
        }
        return true;
    }

    public const string Usage = """
        io_inspector - disk queue monitor with per-file culprit attribution
          (no args)          GUI mode (self-elevates; --noelevate to skip)
          --cli              console mode
          --disk D:|3        filter files by drive letter or physical disk number
          --top N            show top N files (default 10)
          --seconds N        stop after N seconds (default: forever)
          --timeout N        alias of --seconds (stateless capture for scripts/AI)
          --sort KEY         summary sort: total|read|write|process|pid|file (default total)
          --asc              ascending sort (default descending)
          --log FILE         tee CLI output to FILE
          --detail PID       print full process info and exit
          --kill PID         terminate process tree and exit (destructive, no prompt)
          --demo             GUI with synthetic data (privacy-safe, for screenshots)
          --selftest         run self-checks and exit
        """;
}
