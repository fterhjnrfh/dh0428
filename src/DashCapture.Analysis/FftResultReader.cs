using System.Runtime.InteropServices;

namespace DashCapture.Analysis;

public sealed class FftResultReader : IDisposable
{
    private const int FileMagic = 0x54464644; // DFFT
    private const int FrameMagic = 0x46544646; // FFTF
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private bool _disposed;

    private FftResultReader(string path, FileStream stream, BinaryReader reader, FftResultFileInfo fileInfo)
    {
        Path = path;
        _stream = stream;
        _reader = reader;
        FileInfo = fileInfo;
    }

    public string Path { get; }
    public FftResultFileInfo FileInfo { get; }
    public long Position => _stream.Position;
    public long Length => _stream.Length;

    public static FftResultReader Open(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4 * 1024 * 1024,
            FileOptions.SequentialScan);
        var reader = new BinaryReader(stream);
        try
        {
            int magic = reader.ReadInt32();
            if (magic != FileMagic)
            {
                throw new InvalidDataException("The file is not a DashCapture FFT result file.");
            }

            int version = reader.ReadInt32();
            if (version != 1 && version != 2 && version != 3)
            {
                throw new InvalidDataException($"Unsupported FFT result format version {version}.");
            }

            long createdAtMs = reader.ReadInt64();
            int windowSampleCount = reader.ReadInt32();
            int hopSampleCount = reader.ReadInt32();
            int maxChannels = reader.ReadInt32();
            int maxFftChannels = reader.ReadInt32();
            string payloadKind = reader.ReadString();
            string configuredBackend = string.Empty;
            string initialBackend = string.Empty;
            string initialDevice = string.Empty;
            string windowMode = "fixed_samples";
            double targetResolutionHz = 0;
            double overlapRatio = 0;
            if (version >= 2)
            {
                configuredBackend = reader.ReadString();
                initialBackend = reader.ReadString();
                initialDevice = reader.ReadString();
            }

            if (version >= 3)
            {
                windowMode = reader.ReadString();
                targetResolutionHz = reader.ReadDouble();
                overlapRatio = reader.ReadDouble();
            }

            var info = new FftResultFileInfo(
                path,
                version,
                DateTimeOffset.FromUnixTimeMilliseconds(createdAtMs),
                windowSampleCount,
                hopSampleCount,
                maxChannels,
                maxFftChannels,
                payloadKind,
                configuredBackend,
                initialBackend,
                initialDevice,
                windowMode,
                targetResolutionHz,
                overlapRatio);
            return new FftResultReader(path, stream, reader, info);
        }
        catch
        {
            reader.Dispose();
            stream.Dispose();
            throw;
        }
    }

    public IEnumerable<FftResultFrame> ReadFrames(int maxFrames = int.MaxValue)
    {
        return ReadFrames(null, maxFrames);
    }

    public IEnumerable<FftResultFrame> ReadFrames(Func<FftResultFrameHeader, bool>? predicate, int maxFrames = int.MaxValue)
    {
        ThrowIfDisposed();
        int emitted = 0;
        while (emitted < maxFrames && _stream.Position < _stream.Length)
        {
            FftResultFrameHeader? header = TryReadFrameHeader();
            if (header is null)
            {
                yield break;
            }

            if (predicate is not null && !predicate(header))
            {
                SkipPayload(header);
                continue;
            }

            float[]? magnitudes = TryReadMagnitudes(header);
            if (magnitudes is null)
            {
                yield break;
            }

            emitted++;
            yield return CreateFrame(header, magnitudes);
        }
    }

    public IEnumerable<FftResultFrameHeader> ReadFrameHeaders(int maxFrames = int.MaxValue)
    {
        ThrowIfDisposed();
        int emitted = 0;
        while (emitted < maxFrames && _stream.Position < _stream.Length)
        {
            FftResultFrameHeader? header = TryReadFrameHeader();
            if (header is null)
            {
                yield break;
            }

            SkipPayload(header);
            emitted++;
            yield return header;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
        _stream.Dispose();
    }

    private FftResultFrameHeader? TryReadFrameHeader()
    {
        try
        {
            long frameOffset = _stream.Position;
            int magic = _reader.ReadInt32();
            if (magic != FrameMagic)
            {
                throw new InvalidDataException($"Invalid FFT frame magic 0x{magic:X8} at offset {_stream.Position - sizeof(int)}.");
            }

            long frameTimestampMs = _reader.ReadInt64();
            long sourceSampleTime = _reader.ReadInt64();
            long windowIndex = _reader.ReadInt64();
            long windowStartSample = _reader.ReadInt64();
            long windowEndSample = _reader.ReadInt64();
            int deviceId = _reader.ReadInt32();
            int channelId = _reader.ReadInt32();
            string deviceIp = _reader.ReadString();
            string channelName = _reader.ReadString();
            string unit = _reader.ReadString();
            string computeBackend = FileInfo.InitialBackend;
            string computeDevice = FileInfo.InitialDevice;
            if (FileInfo.FormatVersion >= 2)
            {
                computeBackend = _reader.ReadString();
                computeDevice = _reader.ReadString();
            }

            float sampleRate = _reader.ReadSingle();
            int fftSize = _reader.ReadInt32();
            int binCount = _reader.ReadInt32();
            float frequencyResolution = _reader.ReadSingle();
            int payloadBytes = _reader.ReadInt32();
            int expectedBytes = checked(binCount * sizeof(float));
            if (payloadBytes != expectedBytes)
            {
                throw new InvalidDataException($"FFT frame payload length {payloadBytes} does not match bin count {binCount}.");
            }

            return new FftResultFrameHeader(
                frameOffset,
                _stream.Position,
                payloadBytes,
                DateTimeOffset.FromUnixTimeMilliseconds(frameTimestampMs),
                sourceSampleTime,
                windowIndex,
                windowStartSample,
                windowEndSample,
                deviceId,
                channelId,
                deviceIp,
                channelName,
                unit,
                computeBackend,
                computeDevice,
                sampleRate,
                fftSize,
                binCount,
                frequencyResolution);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private float[]? TryReadMagnitudes(FftResultFrameHeader header)
    {
        byte[] payload = _reader.ReadBytes(header.PayloadBytes);
        if (payload.Length != header.PayloadBytes)
        {
            return null;
        }

        float[] magnitudes = new float[header.BinCount];
        MemoryMarshal.Cast<byte, float>(payload).CopyTo(magnitudes);
        return magnitudes;
    }

    private void SkipPayload(FftResultFrameHeader header)
    {
        long next = _stream.Position + header.PayloadBytes;
        if (next > _stream.Length)
        {
            _stream.Position = _stream.Length;
            return;
        }

        _stream.Position = next;
    }

    private static FftResultFrame CreateFrame(FftResultFrameHeader header, float[] magnitudes)
    {
        return new FftResultFrame(
            header.FrameTimestamp,
            header.SourceSampleTime,
            header.WindowIndex,
            header.WindowStartSample,
            header.WindowEndSample,
            header.DeviceId,
            header.ChannelId,
            header.DeviceIp,
            header.ChannelName,
            header.Unit,
            header.ComputeBackend,
            header.ComputeDevice,
            header.SampleRate,
            header.FftSize,
            header.BinCount,
            header.FrequencyResolution,
            magnitudes);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FftResultReader));
        }
    }
}

public sealed record FftResultFileInfo(
    string Path,
    int FormatVersion,
    DateTimeOffset CreatedAt,
    int WindowSampleCount,
    int HopSampleCount,
    int MaxChannels,
    int MaxFftChannels,
    string PayloadKind,
    string ConfiguredBackend = "",
    string InitialBackend = "",
    string InitialDevice = "",
    string WindowMode = "fixed_samples",
    double TargetResolutionHz = 0,
    double OverlapRatio = 0);

public sealed record FftResultFrameHeader(
    long FrameOffset,
    long PayloadOffset,
    int PayloadBytes,
    DateTimeOffset FrameTimestamp,
    long SourceSampleTime,
    long WindowIndex,
    long WindowStartSample,
    long WindowEndSample,
    int DeviceId,
    int ChannelId,
    string DeviceIp,
    string ChannelName,
    string Unit,
    string ComputeBackend,
    string ComputeDevice,
    float SampleRate,
    int FftSize,
    int BinCount,
    float FrequencyResolution)
{
    public FftChannelKey ChannelKey => new(DeviceId, ChannelId, DeviceIp ?? string.Empty);
    public double WindowStartSeconds => SampleRate > 0 ? WindowStartSample / SampleRate : 0;
    public double WindowEndSeconds => SampleRate > 0 ? WindowEndSample / SampleRate : WindowStartSeconds;
}

public readonly record struct FftChannelKey(int DeviceId, int ChannelId, string DeviceIp);

public sealed record FftResultFrame(
    DateTimeOffset FrameTimestamp,
    long SourceSampleTime,
    long WindowIndex,
    long WindowStartSample,
    long WindowEndSample,
    int DeviceId,
    int ChannelId,
    string DeviceIp,
    string ChannelName,
    string Unit,
    string ComputeBackend,
    string ComputeDevice,
    float SampleRate,
    int FftSize,
    int BinCount,
    float FrequencyResolution,
    float[] Magnitudes)
{
    public double WindowStartSeconds => SampleRate > 0 ? WindowStartSample / SampleRate : 0;
    public double WindowEndSeconds => SampleRate > 0 ? WindowEndSample / SampleRate : WindowStartSeconds;

    public FftPeak FindPeak(
        bool ignoreDc = true,
        float minFrequencyHz = 0,
        float maxFrequencyHz = float.PositiveInfinity,
        float minMagnitude = 0)
    {
        int start = ignoreDc ? 1 : 0;
        int bestIndex = -1;
        float bestMagnitude = float.NegativeInfinity;
        for (int i = start; i < Magnitudes.Length; i++)
        {
            float frequency = i * FrequencyResolution;
            if (frequency < minFrequencyHz || frequency > maxFrequencyHz)
            {
                continue;
            }

            float magnitude = Magnitudes[i];
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
            {
                continue;
            }

            if (magnitude <= minMagnitude)
            {
                continue;
            }

            if (magnitude > bestMagnitude)
            {
                bestMagnitude = magnitude;
                bestIndex = i;
            }
        }

        return bestIndex < 0
            ? new FftPeak(-1, 0, 0)
            : new FftPeak(bestIndex, bestIndex * FrequencyResolution, bestMagnitude);
    }
}

public readonly record struct FftPeak(int BinIndex, float FrequencyHz, float Magnitude);
