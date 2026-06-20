using System.Globalization;
using System.Text;

namespace DashCapture.Analysis;

public static class FftTrendCalculator
{
    private const int ProgressIntervalFrames = 4096;

    public static FftFileOverview ReadOverview(
        string path,
        CancellationToken cancellationToken = default,
        IProgress<FftReadProgress>? progress = null)
    {
        using FftResultReader reader = FftResultReader.Open(path);
        var channels = new Dictionary<FftChannelKey, FftChannelAccumulator>();
        long frames = 0;

        foreach (FftResultFrameHeader header in reader.ReadFrameHeaders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames++;
            if (!channels.TryGetValue(header.ChannelKey, out FftChannelAccumulator? channel))
            {
                channel = new FftChannelAccumulator(header.ChannelKey);
                channels[header.ChannelKey] = channel;
            }

            channel.Add(header);
            ReportProgress(progress, reader, frames, matchedFrames: frames);
        }

        progress?.Report(new FftReadProgress(frames, frames, reader.Position, reader.Length));
        return new FftFileOverview(
            reader.FileInfo,
            frames,
            channels.Values
                .Select(item => item.ToOverview())
                .OrderBy(item => item.Key.DeviceId)
                .ThenBy(item => item.Key.ChannelId)
                .ThenBy(item => item.Key.DeviceIp, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public static FftChannelTrend CalculateTrend(
        string path,
        FftChannelKey channelKey,
        CancellationToken cancellationToken = default,
        IProgress<FftReadProgress>? progress = null)
    {
        using FftResultReader reader = FftResultReader.Open(path);
        var points = new List<FftTrendPoint>();
        var channel = new FftChannelAccumulator(channelKey);
        long frames = 0;
        long matched = 0;

        foreach (FftResultFrame frame in reader.ReadFrames(header => header.ChannelKey.Equals(channelKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames++;
            matched++;
            channel.Add(frame);
            FftPeak peak = frame.FindPeak(ignoreDc: true);
            points.Add(new FftTrendPoint(
                frame.WindowStartSeconds,
                frame.WindowIndex,
                frame.FrameTimestamp,
                peak.BinIndex,
                peak.FrequencyHz,
                peak.Magnitude));
            ReportProgress(progress, reader, frames, matched);
        }

        progress?.Report(new FftReadProgress(frames, matched, reader.Position, reader.Length));
        return new FftChannelTrend(reader.FileInfo, channel.ToOverview(), points);
    }

    public static FftResultFrame? ReadChannelFrame(
        string path,
        FftChannelKey channelKey,
        int channelFrameIndex,
        CancellationToken cancellationToken = default,
        IProgress<FftReadProgress>? progress = null)
    {
        int targetIndex = Math.Max(0, channelFrameIndex);
        using FftResultReader reader = FftResultReader.Open(path);
        long frames = 0;
        long matched = 0;

        foreach (FftResultFrame frame in reader.ReadFrames(header => header.ChannelKey.Equals(channelKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames++;
            if (matched == targetIndex)
            {
                progress?.Report(new FftReadProgress(frames, matched + 1, reader.Position, reader.Length));
                return frame;
            }

            matched++;
            ReportProgress(progress, reader, frames, matched);
        }

        progress?.Report(new FftReadProgress(frames, matched, reader.Position, reader.Length));
        return null;
    }

    public static void ExportSpectrumCsv(FftResultFrame frame, string targetPath)
    {
        using var writer = CreateCsvWriter(targetPath);
        writer.WriteLine("frequency_hz,magnitude");
        for (int i = 0; i < frame.Magnitudes.Length; i++)
        {
            float frequency = i * frame.FrequencyResolution;
            writer.Write(FormatNumber(frequency));
            writer.Write(',');
            writer.WriteLine(FormatNumber(frame.Magnitudes[i]));
        }
    }

    public static void ExportTrendCsv(FftChannelTrend trend, string targetPath)
    {
        using var writer = CreateCsvWriter(targetPath);
        writer.WriteLine("time_seconds,window_index,frame_timestamp_utc,peak_bin,peak_frequency_hz,peak_magnitude");
        foreach (FftTrendPoint point in trend.Points)
        {
            WriteTrendPoint(writer, point);
        }
    }

    public static void ExportPeaksCsv(
        string sourcePath,
        string targetPath,
        FftChannelKey? channelKey = null,
        CancellationToken cancellationToken = default,
        IProgress<FftReadProgress>? progress = null)
    {
        using FftResultReader reader = FftResultReader.Open(sourcePath);
        using var writer = CreateCsvWriter(targetPath);
        writer.WriteLine("device_id,channel_id,device_ip,channel_name,unit,sample_rate,fft_size,frequency_resolution_hz,time_seconds,window_index,frame_timestamp_utc,backend,compute_device,peak_bin,peak_frequency_hz,peak_magnitude");

        long frames = 0;
        long matched = 0;
        Func<FftResultFrameHeader, bool>? predicate = channelKey.HasValue
            ? header => header.ChannelKey.Equals(channelKey.Value)
            : null;

        foreach (FftResultFrame frame in reader.ReadFrames(predicate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            frames++;
            matched++;
            FftPeak peak = frame.FindPeak(ignoreDc: true);
            writer.Write(frame.DeviceId);
            writer.Write(',');
            writer.Write(frame.ChannelId);
            writer.Write(',');
            writer.Write(Csv(frame.DeviceIp));
            writer.Write(',');
            writer.Write(Csv(frame.ChannelName));
            writer.Write(',');
            writer.Write(Csv(frame.Unit));
            writer.Write(',');
            writer.Write(FormatNumber(frame.SampleRate));
            writer.Write(',');
            writer.Write(frame.FftSize);
            writer.Write(',');
            writer.Write(FormatNumber(frame.FrequencyResolution));
            writer.Write(',');
            writer.Write(FormatNumber(frame.WindowStartSeconds));
            writer.Write(',');
            writer.Write(frame.WindowIndex);
            writer.Write(',');
            writer.Write(frame.FrameTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(Csv(frame.ComputeBackend));
            writer.Write(',');
            writer.Write(Csv(frame.ComputeDevice));
            writer.Write(',');
            writer.Write(peak.BinIndex);
            writer.Write(',');
            writer.Write(FormatNumber(peak.FrequencyHz));
            writer.Write(',');
            writer.WriteLine(FormatNumber(peak.Magnitude));
            ReportProgress(progress, reader, frames, matched);
        }

        progress?.Report(new FftReadProgress(frames, matched, reader.Position, reader.Length));
    }

    private static void WriteTrendPoint(TextWriter writer, FftTrendPoint point)
    {
        writer.Write(FormatNumber(point.TimeSeconds));
        writer.Write(',');
        writer.Write(point.WindowIndex);
        writer.Write(',');
        writer.Write(point.FrameTimestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        writer.Write(',');
        writer.Write(point.PeakBin);
        writer.Write(',');
        writer.Write(FormatNumber(point.PeakFrequencyHz));
        writer.Write(',');
        writer.WriteLine(FormatNumber(point.PeakMagnitude));
    }

    private static void ReportProgress(IProgress<FftReadProgress>? progress, FftResultReader reader, long frames, long matchedFrames)
    {
        if (progress is null || frames % ProgressIntervalFrames != 0)
        {
            return;
        }

        progress.Report(new FftReadProgress(frames, matchedFrames, reader.Position, reader.Length));
    }

    private static StreamWriter CreateCsvWriter(string path)
    {
        return new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024 * 1024);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private sealed class FftChannelAccumulator
    {
        private readonly HashSet<string> _backends = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _devices = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _firstTimestamp;
        private DateTimeOffset _lastTimestamp;

        public FftChannelAccumulator(FftChannelKey key)
        {
            Key = key;
        }

        public FftChannelKey Key { get; }
        public string ChannelName { get; private set; } = string.Empty;
        public string Unit { get; private set; } = string.Empty;
        public float SampleRate { get; private set; }
        public int FftSize { get; private set; }
        public float FrequencyResolution { get; private set; }
        public long FrameCount { get; private set; }
        public double FirstSeconds { get; private set; }
        public double LastSeconds { get; private set; }

        public void Add(FftResultFrameHeader header)
        {
            Add(
                header.ChannelName,
                header.Unit,
                header.SampleRate,
                header.FftSize,
                header.FrequencyResolution,
                header.ComputeBackend,
                header.ComputeDevice,
                header.FrameTimestamp,
                header.WindowStartSeconds);
        }

        public void Add(FftResultFrame frame)
        {
            Add(
                frame.ChannelName,
                frame.Unit,
                frame.SampleRate,
                frame.FftSize,
                frame.FrequencyResolution,
                frame.ComputeBackend,
                frame.ComputeDevice,
                frame.FrameTimestamp,
                frame.WindowStartSeconds);
        }

        public FftChannelOverview ToOverview()
        {
            return new FftChannelOverview(
                Key,
                ChannelName,
                Unit,
                SampleRate,
                FftSize,
                FrequencyResolution,
                FrameCount,
                FirstSeconds,
                LastSeconds,
                _firstTimestamp,
                _lastTimestamp,
                Summarize(_backends),
                Summarize(_devices));
        }

        private void Add(
            string channelName,
            string unit,
            float sampleRate,
            int fftSize,
            float frequencyResolution,
            string backend,
            string device,
            DateTimeOffset timestamp,
            double seconds)
        {
            if (FrameCount == 0)
            {
                ChannelName = channelName;
                Unit = unit;
                SampleRate = sampleRate;
                FftSize = fftSize;
                FrequencyResolution = frequencyResolution;
                FirstSeconds = seconds;
                LastSeconds = seconds;
                _firstTimestamp = timestamp;
                _lastTimestamp = timestamp;
            }
            else
            {
                LastSeconds = seconds;
                _lastTimestamp = timestamp;
            }

            if (!string.IsNullOrWhiteSpace(backend))
            {
                _backends.Add(backend);
            }

            if (!string.IsNullOrWhiteSpace(device))
            {
                _devices.Add(device);
            }

            FrameCount++;
        }

        private static string Summarize(HashSet<string> values)
        {
            return values.Count == 0 ? string.Empty : string.Join(",", values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }
    }
}

public sealed record FftFileOverview(
    FftResultFileInfo FileInfo,
    long FrameCount,
    IReadOnlyList<FftChannelOverview> Channels);

public sealed record FftChannelOverview(
    FftChannelKey Key,
    string ChannelName,
    string Unit,
    float SampleRate,
    int FftSize,
    float FrequencyResolution,
    long FrameCount,
    double FirstSeconds,
    double LastSeconds,
    DateTimeOffset FirstTimestamp,
    DateTimeOffset LastTimestamp,
    string ComputeBackends,
    string ComputeDevices)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ChannelName)
        ? $"Device {Key.DeviceId + 1}/Channel {Key.ChannelId}"
        : $"Device {Key.DeviceId + 1}/{ChannelName}";

    public double DurationSeconds => Math.Max(0, LastSeconds - FirstSeconds);
}

public sealed record FftTrendPoint(
    double TimeSeconds,
    long WindowIndex,
    DateTimeOffset FrameTimestamp,
    int PeakBin,
    float PeakFrequencyHz,
    float PeakMagnitude);

public sealed record FftChannelTrend(
    FftResultFileInfo FileInfo,
    FftChannelOverview Channel,
    IReadOnlyList<FftTrendPoint> Points)
{
    public double AveragePeakFrequencyHz => Points.Count == 0 ? 0 : Points.Average(point => point.PeakFrequencyHz);
    public double AveragePeakMagnitude => Points.Count == 0 ? 0 : Points.Average(point => point.PeakMagnitude);
    public float MinPeakFrequencyHz => Points.Count == 0 ? 0 : Points.Min(point => point.PeakFrequencyHz);
    public float MaxPeakFrequencyHz => Points.Count == 0 ? 0 : Points.Max(point => point.PeakFrequencyHz);
    public float MaxPeakMagnitude => Points.Count == 0 ? 0 : Points.Max(point => point.PeakMagnitude);
}

public readonly record struct FftReadProgress(long FramesRead, long MatchedFrames, long BytesRead, long TotalBytes)
{
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesRead / TotalBytes * 100, 0, 100);
}
