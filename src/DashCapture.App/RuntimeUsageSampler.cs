using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DashCapture.App;

internal sealed class RuntimeUsageSampler : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly PdhSampler? _pdh;
    private TimeSpan _lastProcessCpu;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.UtcNow;

    public RuntimeUsageSampler()
    {
        _lastProcessCpu = _process.TotalProcessorTime;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _pdh = PdhSampler.TryCreate();
        }
    }

    public RuntimeUsageSnapshot Sample()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        double elapsedSeconds = Math.Max(0.001, (now - _lastSampleAt).TotalSeconds);
        _lastSampleAt = now;

        double processCpu = 0;
        try
        {
            _process.Refresh();
            TimeSpan currentCpu = _process.TotalProcessorTime;
            processCpu = (currentCpu - _lastProcessCpu).TotalMilliseconds / (elapsedSeconds * 1000.0 * _processorCount) * 100.0;
            _lastProcessCpu = currentCpu;
        }
        catch
        {
            processCpu = 0;
        }

        PdhUsageSnapshot? pdh = _pdh?.Sample(_process.Id);
        return new RuntimeUsageSnapshot(
            ClampPercent(processCpu),
            pdh?.SystemCpuPercent,
            pdh?.GpuTotalPercent,
            pdh?.GpuProcessPercent,
            pdh?.GpuEngines ?? Array.Empty<GpuEngineUsage>());
    }

    public void Dispose()
    {
        _pdh?.Dispose();
        _process.Dispose();
    }

    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private sealed class PdhSampler : IDisposable
    {
        private const uint ErrorSuccess = 0;
        private const uint PdhMoreData = 0x800007D2;
        private const uint PdhFmtDouble = 0x00000200;
        private readonly IntPtr _query;
        private readonly IntPtr _cpuCounter;
        private readonly IntPtr _gpuCounter;

        private PdhSampler(IntPtr query, IntPtr cpuCounter, IntPtr gpuCounter)
        {
            _query = query;
            _cpuCounter = cpuCounter;
            _gpuCounter = gpuCounter;
            PdhCollectQueryData(_query);
        }

        public static PdhSampler? TryCreate()
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out IntPtr query) != ErrorSuccess)
            {
                return null;
            }

            IntPtr cpuCounter = IntPtr.Zero;
            IntPtr gpuCounter = IntPtr.Zero;
            PdhAddEnglishCounter(query, @"\Processor(_Total)\% Processor Time", UIntPtr.Zero, out cpuCounter);
            PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out gpuCounter);

            if (cpuCounter == IntPtr.Zero && gpuCounter == IntPtr.Zero)
            {
                PdhCloseQuery(query);
                return null;
            }

            return new PdhSampler(query, cpuCounter, gpuCounter);
        }

        public PdhUsageSnapshot Sample(int processId)
        {
            PdhCollectQueryData(_query);
            double? systemCpu = _cpuCounter == IntPtr.Zero ? null : ReadSingle(_cpuCounter);
            IReadOnlyList<GpuEngineUsage> engines = _gpuCounter == IntPtr.Zero
                ? Array.Empty<GpuEngineUsage>()
                : ReadGpuEngines(_gpuCounter, processId);
            double? gpuTotal = engines.Count == 0 ? null : Math.Clamp(engines.Sum(engine => engine.TotalPercent), 0, 100);
            double? gpuProcess = engines.Count == 0 ? null : Math.Clamp(engines.Sum(engine => engine.ProcessPercent), 0, 100);
            return new PdhUsageSnapshot(systemCpu, gpuTotal, gpuProcess, engines);
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
            }
        }

        private static double? ReadSingle(IntPtr counter)
        {
            uint type;
            uint result = PdhGetFormattedCounterValue(counter, PdhFmtDouble, out type, out PdhFmtCounterValue value);
            if (result != ErrorSuccess || value.CStatus != ErrorSuccess)
            {
                return null;
            }

            return Math.Clamp(value.DoubleValue, 0, 100);
        }

        private static IReadOnlyList<GpuEngineUsage> ReadGpuEngines(IntPtr counter, int processId)
        {
            uint bufferSize = 0;
            uint itemCount = 0;
            uint result = PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
            if (result != PdhMoreData || bufferSize == 0 || itemCount == 0)
            {
                return Array.Empty<GpuEngineUsage>();
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                result = PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref bufferSize, ref itemCount, buffer);
                if (result != ErrorSuccess)
                {
                    return Array.Empty<GpuEngineUsage>();
                }

                var totals = new Dictionary<string, (double Total, double Process)>(StringComparer.OrdinalIgnoreCase);
                int size = Marshal.SizeOf<PdhFmtCounterValueItem>();
                string processToken = "pid_" + processId.ToString(CultureInfo.InvariantCulture) + "_";
                for (int i = 0; i < itemCount; i++)
                {
                    var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(IntPtr.Add(buffer, i * size));
                    if (item.Value.CStatus != ErrorSuccess)
                    {
                        continue;
                    }

                    string? name = Marshal.PtrToStringUni(item.Name);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string engine = ParseEngineType(name);
                    double value = Math.Clamp(item.Value.DoubleValue, 0, 100);
                    totals.TryGetValue(engine, out (double Total, double Process) current);
                    current.Total += value;
                    if (name.Contains(processToken, StringComparison.OrdinalIgnoreCase))
                    {
                        current.Process += value;
                    }

                    totals[engine] = current;
                }

                return totals
                    .OrderByDescending(pair => pair.Value.Total)
                    .Select(pair => new GpuEngineUsage(
                        pair.Key,
                        Math.Clamp(pair.Value.Total, 0, 100),
                        Math.Clamp(pair.Value.Process, 0, 100)))
                    .ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ParseEngineType(string instanceName)
        {
            const string marker = "engtype_";
            int index = instanceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return "Other";
            }

            string engine = instanceName[(index + marker.Length)..];
            int separator = engine.IndexOf('_');
            if (separator >= 0)
            {
                engine = engine[..separator];
            }

            return string.IsNullOrWhiteSpace(engine) ? "Other" : engine;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? dataSource, UIntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, UIntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(
            IntPtr counter,
            uint format,
            out uint type,
            out PdhFmtCounterValue value);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhGetFormattedCounterArray(
            IntPtr counter,
            uint format,
            ref uint bufferSize,
            ref uint itemCount,
            IntPtr itemBuffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public PdhFmtCounterValue Value;
    }

    private sealed record PdhUsageSnapshot(
        double? SystemCpuPercent,
        double? GpuTotalPercent,
        double? GpuProcessPercent,
        IReadOnlyList<GpuEngineUsage> GpuEngines);
}

internal sealed record RuntimeUsageSnapshot(
    double ProcessCpuPercent,
    double? SystemCpuPercent,
    double? GpuTotalPercent,
    double? GpuProcessPercent,
    IReadOnlyList<GpuEngineUsage> GpuEngines);

internal sealed record GpuEngineUsage(string Engine, double TotalPercent, double ProcessPercent);
