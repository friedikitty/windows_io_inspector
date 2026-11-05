namespace IoInspector;

public sealed class Options
{
    public bool Cli;
    public bool SelfTest;
    public string? Disk;      // "D:" or physical disk number "3"
    public int Top = 10;
    public int Seconds;       // 0 = run forever
    public string? Log;       // tee CLI output to this file

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
                case "--disk":
                    o.Disk = Next();
                    if (o.Disk == null) return false;
                    break;
                case "--top":
                    if (!int.TryParse(Next(), out int t)) return false;
                    o.Top = t; break;
                case "--seconds":
                    if (!int.TryParse(Next(), out int s)) return false;
                    o.Seconds = s; break;
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
          (no args)          GUI mode
          --cli              console mode
          --disk D:|3        filter files by drive letter or physical disk number
          --top N            show top N files (default 10)
          --seconds N        stop after N seconds (default: forever)
          --log FILE         tee CLI output to FILE
          --selftest         run self-checks and exit
        """;
}
