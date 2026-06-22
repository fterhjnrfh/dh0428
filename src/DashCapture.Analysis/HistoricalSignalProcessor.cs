using System.Globalization;
using System.Text;
using System.Text.Json;
using DashCapture.Storage;

namespace DashCapture.Analysis;

public static class HistoricalSignalProcessor
{
    private const int DefaultChunkSamples = 1_048_576;

    public static HistoricalSignalProcessingResult Process(
        TdmsFileReader reader,
        SignalProcessingModuleDefinition module,
        IReadOnlyList<TdmsChannelInfo> channels,
        HistoricalSignalProcessingOptions options,
        CancellationToken cancellationToken,
        IProgress<HistoricalSignalProcessingProgress>? progress = null)
    {
        if (channels.Count == 0)
        {
            throw new InvalidOperationException("No channels were selected for historical processing.");
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var results = new List<SignalProcessingChannelResult>(channels.Count);
        int chunkSamples = Math.Clamp(options.ChunkSamples <= 0 ? DefaultChunkSamples : options.ChunkSamples, 16_384, DefaultChunkSamples * 8);
        var buffer = new float[chunkSamples];

        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TdmsChannelInfo channel = channels[channelIndex];
            var accumulator = new SignalStatisticsAccumulator();
            ulong offset = 0;
            progress?.Report(new HistoricalSignalProcessingProgress(channel.DisplayName, channelIndex, channels.Count, offset, channel.SampleCount));

            while (offset < channel.SampleCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min((ulong)buffer.Length, channel.SampleCount - offset);
                int read = reader.ReadSamples(channel, offset, count, buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                accumulator.Add(buffer.AsSpan(0, read));
                offset += (ulong)read;
                progress?.Report(new HistoricalSignalProcessingProgress(channel.DisplayName, channelIndex, channels.Count, offset, channel.SampleCount));
            }

            SignalProcessingMetricValue[] metrics = module.Algorithms
                .Select(algorithm => new SignalProcessingMetricValue(
                    algorithm.Name,
                    algorithm.Type,
                    accumulator.ValueOf(algorithm.Type)))
                .ToArray();

            results.Add(new SignalProcessingChannelResult(
                channel.DeviceId,
                channel.GroupName,
                channel.ChannelId,
                channel.Name,
                channel.Unit,
                channel.SampleRate,
                accumulator.Count,
                channel.DurationSeconds,
                metrics));

            progress?.Report(new HistoricalSignalProcessingProgress(channel.DisplayName, channelIndex + 1, channels.Count, offset, channel.SampleCount));
        }

        return new HistoricalSignalProcessingResult(
            module.Name,
            module.Algorithms,
            reader.Path,
            startedAt,
            DateTimeOffset.UtcNow,
            results);
    }
}

public sealed record HistoricalSignalProcessingOptions(int ChunkSamples = 1_048_576);

public sealed record HistoricalSignalProcessingProgress(
    string ChannelName,
    int CompletedChannels,
    int TotalChannels,
    ulong ChannelSamplesDone,
    ulong ChannelSamplesTotal)
{
    public double Percent
    {
        get
        {
            if (TotalChannels <= 0)
            {
                return 0;
            }

            double channelProgress = ChannelSamplesTotal == 0 ? 1 : Math.Clamp(ChannelSamplesDone / (double)ChannelSamplesTotal, 0, 1);
            return Math.Clamp((CompletedChannels + channelProgress) / TotalChannels * 100, 0, 100);
        }
    }
}

public sealed record HistoricalSignalProcessingResult(
    string ModuleName,
    IReadOnlyList<SignalProcessingAlgorithmDefinition> Algorithms,
    string SourcePath,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<SignalProcessingChannelResult> Channels)
{
    public TimeSpan Elapsed => CompletedAtUtc - StartedAtUtc;
}

public sealed record SignalProcessingChannelResult(
    int DeviceId,
    string GroupName,
    int ChannelId,
    string ChannelName,
    string Unit,
    double SampleRate,
    ulong SampleCount,
    double DurationSeconds,
    IReadOnlyList<SignalProcessingMetricValue> Metrics)
{
    public string DisplayName => $"{GroupName}/{ChannelName}";

    public double? MetricValue(SignalProcessingAlgorithmType type)
    {
        return Metrics.FirstOrDefault(metric => metric.Type == type)?.Value;
    }
}

public sealed record SignalProcessingMetricValue(
    string Name,
    SignalProcessingAlgorithmType Type,
    double Value);

public static class SignalProcessingResultWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void WriteJson(HistoricalSignalProcessingResult result, string path)
    {
        EnsureParentDirectory(path);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, result, SignalProcessingJson.Options);
    }

    public static void WriteCsv(HistoricalSignalProcessingResult result, string path)
    {
        EnsureParentDirectory(path);
        using var writer = new StreamWriter(path, append: false, Utf8WithBom);
        writer.Write("source_path,module,device_id,group_name,channel_id,channel_name,unit,sample_rate_hz,sample_count,duration_seconds");
        foreach (SignalProcessingAlgorithmDefinition algorithm in result.Algorithms)
        {
            writer.Write(',');
            writer.Write(EscapeCsv(algorithm.Name));
        }

        writer.WriteLine();

        foreach (SignalProcessingChannelResult channel in result.Channels)
        {
            writer.Write(EscapeCsv(result.SourcePath));
            writer.Write(',');
            writer.Write(EscapeCsv(result.ModuleName));
            writer.Write(',');
            writer.Write(channel.DeviceId + 1);
            writer.Write(',');
            writer.Write(EscapeCsv(channel.GroupName));
            writer.Write(',');
            writer.Write(channel.ChannelId + 1);
            writer.Write(',');
            writer.Write(EscapeCsv(channel.ChannelName));
            writer.Write(',');
            writer.Write(EscapeCsv(channel.Unit));
            writer.Write(',');
            writer.Write(channel.SampleRate.ToString("0.######", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(channel.SampleCount.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(channel.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture));

            foreach (SignalProcessingAlgorithmDefinition algorithm in result.Algorithms)
            {
                double value = channel.MetricValue(algorithm.Type) ?? double.NaN;
                writer.Write(',');
                writer.Write(double.IsNaN(value) ? string.Empty : value.ToString("G9", CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

internal struct SignalStatisticsAccumulator
{
    private double _minimum;
    private double _maximum;
    private double _mean;
    private double _m2;
    private double _sumSquares;

    public ulong Count { get; private set; }

    public void Add(ReadOnlySpan<float> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            Add(values[i]);
        }
    }

    private void Add(double value)
    {
        if (Count == 0)
        {
            _minimum = value;
            _maximum = value;
            _mean = value;
            _m2 = 0;
            _sumSquares = value * value;
            Count = 1;
            return;
        }

        Count++;
        if (value < _minimum) _minimum = value;
        if (value > _maximum) _maximum = value;
        double delta = value - _mean;
        _mean += delta / Count;
        double delta2 = value - _mean;
        _m2 += delta * delta2;
        _sumSquares += value * value;
    }

    public double ValueOf(SignalProcessingAlgorithmType type)
    {
        if (Count == 0)
        {
            return double.NaN;
        }

        return type switch
        {
            SignalProcessingAlgorithmType.Maximum => _maximum,
            SignalProcessingAlgorithmType.Minimum => _minimum,
            SignalProcessingAlgorithmType.PeakToPeak => _maximum - _minimum,
            SignalProcessingAlgorithmType.Mean => _mean,
            SignalProcessingAlgorithmType.Rms => Math.Sqrt(_sumSquares / Count),
            SignalProcessingAlgorithmType.StandardDeviation => Math.Sqrt(_m2 / Count),
            _ => double.NaN
        };
    }
}
