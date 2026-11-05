using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IoInspector;

public sealed class DiskSample
{
    public required string Instance;   // e.g. "3 d: f: u:"
    public int Number = -1;            // physical disk number
    public string Letters = "";        // e.g. "D: F: U:"
    public double Queue;               // current disk queue length
    public double BytesPerSec;         // total disk throughput
}

// PDH wrapper using English counter paths: immune to OS display locale
// (PerformanceCounter with English names breaks on non-English Windows).
public sealed class Sampler : IDisposable
{
    private IntPtr _query;
    private IntPtr _queueCounter;
    private IntPtr _bpsCounter;

    public Sampler()
    {
        Check(PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
        Check(PdhAddEnglishCounterW(_query, @"\PhysicalDisk(*)\Current Disk Queue Length", IntPtr.Zero, out _queueCounter), "PdhAddEnglishCounter(queue)");
        Check(PdhAddEnglishCounterW(_query, @"\PhysicalDisk(*)\Disk Bytes/sec", IntPtr.Zero, out _bpsCounter), "PdhAddEnglishCounter(bytes/sec)");
        // rate counters need two collects to produce a value
        PdhCollectQueryData(_query);
        Thread.Sleep(150);
        Check(PdhCollectQueryData(_query), "PdhCollectQueryData(prime)");
    }

    public List<DiskSample> Sample()
    {
        Check(PdhCollectQueryData(_query), "PdhCollectQueryData");
        var queue = ReadArray(_queueCounter);
        var bps = ReadArray(_bpsCounter);
        var list = new List<DiskSample>();
        foreach (var (instance, q) in queue)
        {
            if (instance.Equals("_total", StringComparison.OrdinalIgnoreCase)) continue;
            var d = new DiskSample { Instance = instance, Queue = q };
            var m = Regex.Match(instance, @"^(\d+)\s+(.*)$");
            if (m.Success)
            {
                d.Number = int.Parse(m.Groups[1].Value);
                d.Letters = string.Join(" ", Regex.Matches(m.Groups[2].Value, @"([A-Za-z]):")
                    .Select(x => x.Groups[1].Value.ToUpperInvariant() + ":"));
            }
            if (bps.TryGetValue(instance, out double b)) d.BytesPerSec = b;
            list.Add(d);
        }
        return list.OrderBy(d => d.Number).ToList();
    }

    private static Dictionary<string, double> ReadArray(IntPtr counter)
    {
        int bufSize = 0, itemCount = 0;
        uint rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref bufSize, ref itemCount, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || bufSize == 0)
            throw new InvalidOperationException($"PdhGetFormattedCounterArray size query failed: 0x{rc:X8}");
        IntPtr buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            Check(PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref bufSize, ref itemCount, buf), "PdhGetFormattedCounterArray");
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int itemSize = IntPtr.Size + 16; // PDH_FMT_COUNTERVALUE_ITEM: name ptr + PDH_FMT_COUNTERVALUE
            for (int i = 0; i < itemCount; i++)
            {
                IntPtr item = buf + i * itemSize;
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                uint status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                double val = status == 0 ? BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8)) : 0;
                result[name] = val;
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    private static void Check(uint rc, string what)
    {
        if (rc != 0) throw new InvalidOperationException($"{what} failed: 0x{rc:X8}");
    }

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);
    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);
    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr hCounter, uint dwFormat, ref int lpdwBufferSize, ref int lpdwItemCount, IntPtr itemBuffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr hQuery);
}
