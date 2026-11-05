using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace IoInspector;

public sealed class FileIoRow
{
    public int Pid;
    public string Process = "";
    public string File = "";
    public long ReadBytes;
    public long WriteBytes;
    public long Total => ReadBytes + WriteBytes;
}

// ETW kernel trace (same data source as Resource Monitor's disk tab).
// Requires elevation; Start() fails softly into Error when not admin.
public sealed class Tracer : IDisposable
{
    private TraceEventSession? _session;
    private Thread? _thread;
    private ConcurrentDictionary<(int pid, string file), long[]> _current = new();
    private readonly ConcurrentDictionary<(int pid, string file), long[]> _totals = new();
    private readonly ConcurrentDictionary<int, string> _procNames = new();
    private Dictionary<string, string> _volumeToDrive = new(StringComparer.OrdinalIgnoreCase);

    public bool Active { get; private set; }
    public string? Error { get; private set; }

    public void Start()
    {
        _volumeToDrive = BuildVolumeMap();
        try
        {
            _session = new TraceEventSession("IoInspectorSession")
            {
                StopOnDispose = true
            };
            _session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.DiskFileIO |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.Process);
            _session.Source.Kernel.FileIORead += e => Add(e.ProcessID, e.ProcessName, e.FileName, e.IoSize, 0);
            _session.Source.Kernel.FileIOWrite += e => Add(e.ProcessID, e.ProcessName, e.FileName, 0, e.IoSize);
            _thread = new Thread(() =>
            {
                try { _session.Source.Process(); }
                catch (Exception ex) { Error = ex.Message; Active = false; }
            }) { IsBackground = true, Name = "etw-process" };
            _thread.Start();
            Active = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Active = false;
            Dispose();
        }
    }

    private void Add(int pid, string? eventProcName, string fileName, long read, long write)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        // resolve at event time: the process is certainly alive now; it may be dead by snapshot time
        _procNames.GetOrAdd(pid, _ => !string.IsNullOrEmpty(eventProcName) ? eventProcName : LookupName(pid));
        string file = ToDrivePath(fileName);
        var cell = _current.GetOrAdd((pid, file), _ => new long[2]);
        Interlocked.Add(ref cell[0], read);
        Interlocked.Add(ref cell[1], write);
    }

    // Returns bytes since the previous snapshot (call once per display interval).
    public List<FileIoRow> Snapshot()
    {
        var snap = Interlocked.Exchange(ref _current, new ConcurrentDictionary<(int, string), long[]>());
        foreach (var (key, rw) in snap)
        {
            var total = _totals.GetOrAdd(key, _ => new long[2]);
            Interlocked.Add(ref total[0], Interlocked.Read(ref rw[0]));
            Interlocked.Add(ref total[1], Interlocked.Read(ref rw[1]));
        }
        return RowsFrom(snap);
    }

    // Cumulative bytes since Start() (only counts intervals that were Snapshot()ed).
    public List<FileIoRow> Totals() => RowsFrom(_totals);

    public void ClearTotals() => _totals.Clear();

    private List<FileIoRow> RowsFrom(IEnumerable<KeyValuePair<(int pid, string file), long[]>> data)
    {
        var rows = new List<FileIoRow>();
        foreach (var ((pid, file), rw) in data)
        {
            long r = Interlocked.Read(ref rw[0]), w = Interlocked.Read(ref rw[1]);
            if (r == 0 && w == 0) continue;
            rows.Add(new FileIoRow { Pid = pid, Process = ProcessName(pid), File = file, ReadBytes = r, WriteBytes = w });
        }
        rows.Sort((a, b) => b.Total.CompareTo(a.Total));
        return rows;
    }

    private string ProcessName(int pid) => _procNames.TryGetValue(pid, out var n) ? n : LookupName(pid);

    // never cache failures: a pid unknown now may be resolvable at the next event
    private static string LookupName(int pid)
    {
        if (pid == 0) return "System";
        try { using var proc = System.Diagnostics.Process.GetProcessById(pid); return proc.ProcessName; }
        catch { return "pid " + pid; }
    }

    // "\Device\HarddiskVolume5\foo\bar" -> "C:\foo\bar"
    private string ToDrivePath(string path)
    {
        foreach (var (vol, drive) in _volumeToDrive)
            if (path.StartsWith(vol, StringComparison.OrdinalIgnoreCase))
                return drive + path[vol.Length..];
        return path;
    }

    public static Dictionary<string, string> BuildVolumeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder(256);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            sb.Clear();
            if (QueryDosDeviceW(c + ":", sb, sb.Capacity) != 0)
                map[sb.ToString()] = c + ":";
        }
        return map;
    }

    public static bool MatchesFile(string? filter, string filePath, List<DiskSample> disks)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        filter = filter.Trim();
        if (filePath.Length < 2 || filePath[1] != ':') return false;
        if (int.TryParse(filter, out int n))
        {
            var disk = disks.FirstOrDefault(d => d.Number == n);
            return disk != null && disk.Letters.Contains(char.ToUpperInvariant(filePath[0]) + ":");
        }
        return char.ToUpperInvariant(filePath[0]) == char.ToUpperInvariant(filter[0]);
    }

    public void Dispose()
    {
        try { _session?.Dispose(); } catch { }
        _session = null;
        Active = false;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
}
