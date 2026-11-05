using System.Runtime.InteropServices;
using System.Security.Principal;

namespace IoInspector;

internal static class Program
{
    public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool FreeConsole();

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
        FreeConsole();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
