using DashCapture.Analysis;
using DashCapture.Storage;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

if (args.Length == 0)
{
    return Fail("Usage: DashCapture.Verify <file.tdms> [audit.raw.csv] [tdm-runtime-dir]\n       DashCapture.Verify stats <file-or-folder> [tdm-runtime-dir]\n       DashCapture.Verify analysis <file-or-folder> [maxChannels] [tdm-runtime-dir]\n       DashCapture.Verify fft <file-or-folder> [expectedHz] [toleranceHz] [maxFrames]");
}

if (string.Equals(args[0], "analysis", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        return Fail("Usage: DashCapture.Verify analysis <file-or-folder> [maxChannels] [tdm-runtime-dir]");
    }

    string inputPath = args[1];
    int maxChannels = args.Length >= 3 && int.TryParse(args[2], out int parsedChannels)
        ? Math.Max(1, parsedChannels)
        : 8;
    string runtimeDir = args.Length >= 4
        ? args[3]
        : Path.GetFullPath(@".\TDM C DLL[官方源文件]\dev\bin\64-bit");

    try
    {
        using TdmsFileReader reader = TdmsFileReader.Open(inputPath, runtimeDir);
        TdmsChannelInfo[] channels = reader.FileInfo.Groups
            .SelectMany(group => group.Channels)
            .Take(maxChannels)
            .ToArray();
        HistoricalSignalProcessingResult result = HistoricalSignalProcessor.Process(
            reader,
            SignalProcessingModuleDefinition.BuiltInAmplitudeAnalysis,
            channels,
            new HistoricalSignalProcessingOptions(),
            CancellationToken.None);

        Console.WriteLine($"Analysis source: {result.SourcePath}");
        Console.WriteLine($"Module: {result.ModuleName}, Channels: {result.Channels.Count}, Elapsed: {result.Elapsed.TotalSeconds:0.000}s");
        Console.WriteLine("Device\tChannel\tSampleRate\tSamples\tMin\tMax\tPeakToPeak\tRMS\tMean\tStdDev");
        foreach (SignalProcessingChannelResult channel in result.Channels)
        {
            Console.WriteLine(
                $"{channel.DeviceId + 1}\t{channel.ChannelName}\t{channel.SampleRate:0.###}\t{channel.SampleCount}\t{Metric(channel, SignalProcessingAlgorithmType.Minimum):0.######}\t{Metric(channel, SignalProcessingAlgorithmType.Maximum):0.######}\t{Metric(channel, SignalProcessingAlgorithmType.PeakToPeak):0.######}\t{Metric(channel, SignalProcessingAlgorithmType.Rms):0.######}\t{Metric(channel, SignalProcessingAlgorithmType.Mean):0.######}\t{Metric(channel, SignalProcessingAlgorithmType.StandardDeviation):0.######}");
        }

        return 0;
    }
    catch (Exception ex)
    {
        return Fail(ex.Message);
    }
}

if (string.Equals(args[0], "fft", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        return Fail("Usage: DashCapture.Verify fft <file-or-folder> [expectedHz] [toleranceHz] [maxFrames]");
    }

    string inputPath = args[1];
    double? expectedHz = args.Length >= 3 && double.TryParse(args[2], out double expected) ? expected : null;
    double toleranceHz = args.Length >= 4 && double.TryParse(args[3], out double tolerance) ? Math.Max(0, tolerance) : 1.0;
    int maxFrames = args.Length >= 5 && int.TryParse(args[4], out int frames) ? Math.Max(1, frames) : 512;

    try
    {
        string[] files = ResolveFftFiles(inputPath);
        if (files.Length == 0)
        {
            return Fail($"No .dhfft file found: {inputPath}");
        }

        bool allPassed = true;
        foreach (string file in files)
        {
            FftFileVerificationSummary summary = VerifyFftFile(file, expectedHz, toleranceHz, maxFrames);
            PrintFftSummary(summary, expectedHz, toleranceHz);
            if (expectedHz.HasValue && !summary.Passed)
            {
                allPassed = false;
            }
        }

        return expectedHz.HasValue && !allPassed ? 1 : 0;
    }
    catch (Exception ex)
    {
        return Fail(ex.Message);
    }
}

if (string.Equals(args[0], "stats", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        return Fail("Usage: DashCapture.Verify stats <file-or-folder> [tdm-runtime-dir]");
    }

    string inputPath = args[1];
    string runtimeDir = args.Length >= 3
        ? args[2]
        : Path.GetFullPath(@".\TDM C DLL[官方源文件]\dev\bin\64-bit");

    try
    {
        using TdmsFileReader reader = TdmsFileReader.Open(inputPath, runtimeDir);
        Console.WriteLine($"TDMS source: {reader.FileInfo.Path}");
        Console.WriteLine($"Groups: {reader.FileInfo.Groups.Count}, Channels: {reader.FileInfo.ChannelCount}, MaxSamples: {reader.FileInfo.MaxSampleCount:N0}");
        Console.WriteLine("Device\tGroup\tChannel\tSamples\tSampleRate\tMin\tMax\tPeakToPeak\tHalfAmplitude");

        foreach (TdmsGroupInfo group in reader.FileInfo.Groups)
        {
            foreach (TdmsChannelInfo channel in group.Channels)
            {
                if (channel.SampleCount == 0)
                {
                    Console.WriteLine($"{group.DeviceId + 1}\t{group.Name}\t{channel.Name}\t0\t{channel.SampleRate:0.###}\t0\t0\t0\t0");
                    continue;
                }

                TdmsChannelEnvelope envelope = reader.ReadEnvelope(channel, 0, channel.SampleCount, 1, CancellationToken.None);
                TdmsEnvelopePoint point = envelope.Points.Count > 0
                    ? envelope.Points[0]
                    : new TdmsEnvelopePoint(0, 0, 0, 0, 0);
                double peakToPeak = point.Maximum - point.Minimum;
                double halfAmplitude = peakToPeak / 2.0;
                Console.WriteLine(
                    $"{group.DeviceId + 1}\t{group.Name}\t{channel.Name}\t{channel.SampleCount}\t{channel.SampleRate:0.###}\t{point.Minimum:0.######}\t{point.Maximum:0.######}\t{peakToPeak:0.######}\t{halfAmplitude:0.######}");
            }
        }

        return 0;
    }
    catch (Exception ex)
    {
        return Fail(ex.Message);
    }
}

string tdmsPath = args[0];
string? auditPath = args.Length >= 2 ? args[1] : null;
string tdmRuntimeDir = args.Length >= 3
    ? args[2]
    : Path.GetFullPath(@".\TDM C DLL[官方源文件]\dev\bin\64-bit");

try
{
    TdmsAuditVerificationResult result = TdmsAuditVerifier.Verify(tdmsPath, auditPath, tdmRuntimeDir);

    Console.WriteLine($"TDMS: {result.TdmsPath}");
    Console.WriteLine($"Audit: {result.AuditCsvPath}");
    Console.WriteLine($"Checked blocks: {result.CheckedBlocks}");
    Console.WriteLine($"Checked bytes: {result.CheckedBytes}");

    if (result.Success)
    {
        Console.WriteLine("Result: PASS");
        return 0;
    }

    Console.WriteLine("Result: FAIL");
    foreach (TdmsAuditMismatch mismatch in result.Mismatches.Take(20))
    {
        Console.WriteLine(
            $"Block={mismatch.BlockIndex}, Group={mismatch.GroupId}, Pos={mismatch.TotalDataCount}, Expected={mismatch.ExpectedCrc32}, Actual={mismatch.ActualCrc32}, Reason={mismatch.Reason}");
    }

    return 1;
}
catch (Exception ex)
{
    return Fail(ex.Message);
}

static string[] ResolveFftFiles(string inputPath)
{
    if (File.Exists(inputPath))
    {
        return new[] { inputPath };
    }

    if (!Directory.Exists(inputPath))
    {
        return Array.Empty<string>();
    }

    return Directory.EnumerateFiles(inputPath, "*.dhfft", SearchOption.TopDirectoryOnly)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .ToArray();
}

static FftFileVerificationSummary VerifyFftFile(string path, double? expectedHz, double toleranceHz, int maxFrames)
{
    using FftResultReader reader = FftResultReader.Open(path);
    var channelSummaries = new Dictionary<string, FftChannelPeakSummary>(StringComparer.OrdinalIgnoreCase);
    int frameCount = 0;
    int passCount = 0;
    int failCount = 0;
    foreach (FftResultFrame frame in reader.ReadFrames(maxFrames))
    {
        frameCount++;
        bool ignoreDc = expectedHz.HasValue && expectedHz.Value > 0;
        FftPeak peak = frame.FindPeak(ignoreDc: ignoreDc);
        string key = $"{frame.DeviceIp}/{frame.DeviceId}/{frame.ChannelId}";
        if (!channelSummaries.TryGetValue(key, out FftChannelPeakSummary? summary))
        {
            summary = new FftChannelPeakSummary(frame.DeviceId, frame.ChannelId, frame.DeviceIp, frame.ChannelName, frame.SampleRate, frame.FrequencyResolution);
            channelSummaries[key] = summary;
        }

        summary.Add(frame, peak);
        if (expectedHz.HasValue)
        {
            if (Math.Abs(peak.FrequencyHz - expectedHz.Value) <= toleranceHz)
            {
                passCount++;
            }
            else
            {
                failCount++;
            }
        }
    }

    return new FftFileVerificationSummary(
        path,
        reader.FileInfo,
        frameCount,
        channelSummaries.Values.OrderBy(item => item.DeviceId).ThenBy(item => item.ChannelId).ToArray(),
        passCount,
        failCount);
}

static void PrintFftSummary(FftFileVerificationSummary summary, double? expectedHz, double toleranceHz)
{
    Console.WriteLine($"FFT: {summary.Path}");
    Console.WriteLine($"Created: {summary.FileInfo.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}");
    if (IsSampleRateWindowing(summary.FileInfo))
    {
        Console.WriteLine($"Window: per-channel sample-rate window, TargetResolution: {summary.FileInfo.TargetResolutionHz:0.###} Hz, Overlap: {summary.FileInfo.OverlapRatio:P1}, MaxChannels: {summary.FileInfo.MaxChannels}, MaxFftChannels: {summary.FileInfo.MaxFftChannels}, Format: v{summary.FileInfo.FormatVersion}");
        if (summary.FileInfo.WindowSampleCount > 0 || summary.FileInfo.HopSampleCount > 0)
        {
            Console.WriteLine($"CompatibilityWindow/Hop: {summary.FileInfo.WindowSampleCount:N0}/{summary.FileInfo.HopSampleCount:N0}");
        }
    }
    else
    {
        Console.WriteLine($"Window/Hop: {summary.FileInfo.WindowSampleCount:N0}/{summary.FileInfo.HopSampleCount:N0}, MaxChannels: {summary.FileInfo.MaxChannels}, MaxFftChannels: {summary.FileInfo.MaxFftChannels}, Format: v{summary.FileInfo.FormatVersion}");
        if (summary.FileInfo.FormatVersion >= 3)
        {
            Console.WriteLine($"WindowMode: {summary.FileInfo.WindowMode}, TargetResolution: {summary.FileInfo.TargetResolutionHz:0.###} Hz, Overlap: {summary.FileInfo.OverlapRatio:P1}");
        }
    }

    if (!string.IsNullOrWhiteSpace(summary.FileInfo.ConfiguredBackend) ||
        !string.IsNullOrWhiteSpace(summary.FileInfo.InitialBackend) ||
        !string.IsNullOrWhiteSpace(summary.FileInfo.InitialDevice))
    {
        Console.WriteLine($"Backend: configured={ValueOrUnknown(summary.FileInfo.ConfiguredBackend)}, initial={ValueOrUnknown(summary.FileInfo.InitialBackend)}, device={ValueOrUnknown(summary.FileInfo.InitialDevice)}");
    }

    Console.WriteLine($"Frames checked: {summary.FrameCount:N0}, Channels: {summary.Channels.Count:N0}");
    Console.WriteLine("Device\tChannel\tName\tBackend\tComputeDevice\tFrames\tPeaks\tSampleRate\tResolutionHz\tPeakAvgHz\tPeakMinHz\tPeakMaxHz\tPeakAvgMag");
    foreach (FftChannelPeakSummary channel in summary.Channels)
    {
        Console.WriteLine(
            $"{channel.DeviceId + 1}\t{channel.ChannelId}\t{channel.ChannelName}\t{channel.BackendSummary}\t{channel.DeviceSummary}\t{channel.ObservedFrameCount}\t{channel.PeakFrameCount}\t{channel.SampleRate:0.###}\t{channel.FrequencyResolution:0.######}\t{channel.AverageFrequencyHz:0.###}\t{channel.MinFrequencyHz:0.###}\t{channel.MaxFrequencyHz:0.###}\t{channel.AverageMagnitude:0.######}");
    }

    if (!expectedHz.HasValue)
    {
        return;
    }

    Console.WriteLine($"Expected: {expectedHz.Value:0.###} Hz ± {toleranceHz:0.###} Hz");
    Console.WriteLine($"Result: {(summary.Passed ? "PASS" : "FAIL")} ({summary.PassCount} pass, {summary.FailCount} fail)");
}

static string ValueOrUnknown(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
}

static bool IsSampleRateWindowing(FftResultFileInfo fileInfo)
{
    return fileInfo.FormatVersion >= 3 &&
        string.Equals(fileInfo.WindowMode, "sample_rate_resolution", StringComparison.OrdinalIgnoreCase);
}

static double Metric(SignalProcessingChannelResult channel, SignalProcessingAlgorithmType type)
{
    return channel.MetricValue(type) ?? double.NaN;
}

sealed record FftFileVerificationSummary(
    string Path,
    FftResultFileInfo FileInfo,
    int FrameCount,
    IReadOnlyList<FftChannelPeakSummary> Channels,
    int PassCount,
    int FailCount)
{
    public bool Passed => FrameCount > 0 && FailCount == 0;
}

sealed class FftChannelPeakSummary
{
    private double _frequencySum;
    private double _magnitudeSum;
    private readonly HashSet<string> _backends = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _devices = new(StringComparer.OrdinalIgnoreCase);

    public FftChannelPeakSummary(int deviceId, int channelId, string deviceIp, string channelName, float sampleRate, float frequencyResolution)
    {
        DeviceId = deviceId;
        ChannelId = channelId;
        DeviceIp = deviceIp;
        ChannelName = channelName;
        SampleRate = sampleRate;
        FrequencyResolution = frequencyResolution;
        MinFrequencyHz = 0;
        MaxFrequencyHz = 0;
    }

    public int DeviceId { get; }
    public int ChannelId { get; }
    public string DeviceIp { get; }
    public string ChannelName { get; }
    public float SampleRate { get; }
    public float FrequencyResolution { get; }
    public int ObservedFrameCount { get; private set; }
    public int PeakFrameCount { get; private set; }
    public double MinFrequencyHz { get; private set; }
    public double MaxFrequencyHz { get; private set; }
    public string BackendSummary => Summarize(_backends);
    public string DeviceSummary => Summarize(_devices);
    public double AverageFrequencyHz => PeakFrameCount == 0 ? 0 : _frequencySum / PeakFrameCount;
    public double AverageMagnitude => PeakFrameCount == 0 ? 0 : _magnitudeSum / PeakFrameCount;

    public void Add(FftResultFrame frame, FftPeak peak)
    {
        ObservedFrameCount++;
        AddIfPresent(_backends, frame.ComputeBackend);
        AddIfPresent(_devices, frame.ComputeDevice);
        if (peak.BinIndex < 0)
        {
            return;
        }

        PeakFrameCount++;
        _frequencySum += peak.FrequencyHz;
        _magnitudeSum += peak.Magnitude;
        if (PeakFrameCount == 1 || peak.FrequencyHz < MinFrequencyHz)
        {
            MinFrequencyHz = peak.FrequencyHz;
        }

        if (PeakFrameCount == 1 || peak.FrequencyHz > MaxFrequencyHz)
        {
            MaxFrequencyHz = peak.FrequencyHz;
        }
    }

    private static void AddIfPresent(HashSet<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static string Summarize(HashSet<string> values)
    {
        return values.Count == 0 ? "unknown" : string.Join(",", values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }
}
