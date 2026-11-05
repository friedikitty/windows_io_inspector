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
        if (o.DetailPid is int dp)
        {
            Console.Write(ProcessInfo.Describe(dp));
            return 0;
        }
        if (o.KillPid is int kp)
        {
            var err = ProcessInfo.Kill(kp);
            Console.WriteLine(err == null ? $"killed pid {kp}" : $"kill failed: {err}");
            return err == null ? 0 : 1;
        }
        if (o.Cli) return Cli.Run(o);
        if (o.Demo) o.NoElevate = true;   // demo never needs admin
        if (!IsAdmin && !o.NoElevate)
        {
            // embedded elevation: restart self with UAC instead of relying on run.bat
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = true, Verb = "runas" });
            return 0;
        }
        FreeConsole();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(o));
        return 0;
    }
}
