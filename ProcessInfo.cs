using System.Diagnostics;
using System.Management;
using System.Text;

namespace IoInspector;

public static class ProcessInfo
{
    public static string Describe(int pid)
    {
        var sb = new StringBuilder();
        void Field(string name, Func<string> get)
        {
            string value;
            try { value = get(); } catch { value = "(unavailable)"; }
            sb.AppendLine($"{name,-14}: {value}");
        }

        Field("PID", () => pid.ToString());
        try
        {
            using var p = Process.GetProcessById(pid);
            Field("Name", () => p.ProcessName);
            Field("Path", () => p.MainModule?.FileName ?? "");
            Field("Started", () => p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            Field("Threads", () => p.Threads.Count.ToString());
            Field("Working set", () => $"{p.WorkingSet64 / 1048576.0:F1} MB");
            Field("Peak working set", () => $"{p.PeakWorkingSet64 / 1048576.0:F1} MB");
            Field("Paged memory", () => $"{p.PagedMemorySize64 / 1048576.0:F1} MB");
            Field("Handles", () => p.HandleCount.ToString());
            Field("Priority", () => p.PriorityClass.ToString());
            Field("Responding", () => p.Responding.ToString());
        }
        catch (Exception ex) { sb.AppendLine($"process: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine, ExecutablePath, ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject mo in searcher.Get())
            {
                Field("Command line", () => mo["CommandLine"]?.ToString() ?? "");
                Field("Exe path (WMI)", () => mo["ExecutablePath"]?.ToString() ?? "");
                int ppid = Convert.ToInt32(mo["ParentProcessId"]);
                Field("Parent PID", () =>
                {
                    string pname;
                    try { pname = Process.GetProcessById(ppid).ProcessName; } catch { pname = "?"; }
                    return $"{ppid} ({pname})";
                });
                string user = "";
                try
                {
                    var outParams = (ManagementBaseObject)mo.InvokeMethod("GetOwner", null);
                    if (outParams?["User"] is string u)
                        user = (outParams["Domain"] is string d ? d + "\\" : "") + u;
                }
                catch { }
                Field("User", () => user);
            }
        }
        catch (Exception ex) { sb.AppendLine($"wmi: {ex.Message}"); }

        return sb.ToString();
    }

    // Returns null on success, error message otherwise.
    public static string? Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
