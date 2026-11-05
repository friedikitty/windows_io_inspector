using System.Security.Principal;

namespace IoInspector;

internal static class Program
{
    public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    [STAThread]
    private static int Main(string[] args)
    {
        if (!Options.TryParse(args, out var o))
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }
        if (o.SelfTest) return SelfTest.Run() ? 0 : 1;
        if (o.Cli) return Cli.Run(o);
        Console.WriteLine("io_inspector: GUI not implemented yet (iteration 3). Use --cli.");
        return 0;
    }
}
