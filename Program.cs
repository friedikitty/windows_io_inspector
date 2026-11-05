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
        Console.WriteLine("io_inspector skeleton (iteration 1)");
        return 0;
    }
}
