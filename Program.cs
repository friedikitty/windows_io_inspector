namespace IoInspector;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!Options.TryParse(args, out var o))
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }
        if (o.Cli) return Cli.Run(o);
        Console.WriteLine("io_inspector: GUI not implemented yet (iteration 2). Use --cli.");
        return 0;
    }
}
