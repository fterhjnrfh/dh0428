using System.Runtime.InteropServices;
using System.Text;
using DashCapture.Core.Configuration;
using DashCapture.Core.Models;

namespace DashCapture.Analysis;

public sealed class FftResultWriter : IDisposable
{
    private const int FileMagic = 0x54464644; // DFFT
    private const int FrameMagic = 0x46544646; // FFTF
    private const int FormatVersion = 3;
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly int _flushIntervalMs;
    private long _framesWritten;
    private long _lastFlushTicks;
    private bool _disposed;

    public FftResultWriter(AnalysisSettings settings, string initialBackend, string initialDevice)
    {
        Directory.CreateDirectory(settings.ResultRootPath);
        CurrentPath = Path.Combine(settings.ResultRootPath, $"DashCaptureFft_{DateTime.Now:yyyyMMdd_HHmmss}.dhfft");
        _stream = new FileStream(
            CurrentPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4 * 1024 * 1024,
            FileOptions.SequentialScan);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
        _flushIntervalMs = 1000;
        WriteHeader(settings, initialBackend, initialDevice);
    }

    public string CurrentPath { get; }
    public long FramesWritten => Interlocked.Read(ref _framesWritten);
    public long BytesWritten => _stream.Position;

    public void WriteFrame(
        ChannelDescriptor channel,
        long sourceSampleTime,
        long windowIndex,
        long windowEndSample,
        int fftSize,
        string computeBackend,
        string computeDevice,
        ReadOnlySpan<float> magnitudes)
    {
        ThrowIfDisposed();
        float sampleRate = IsValidSampleRate(channel.SampleRate) ? channel.SampleRate : 1;
        _writer.Write(FrameMagic);
        _writer.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _writer.Write(sourceSampleTime);
        _writer.Write(windowIndex);
        _writer.Write(windowEndSample - fftSize);
        _writer.Write(windowEndSample);
        _writer.Write(channel.DeviceId);
        _writer.Write(channel.ChannelId);
        _writer.Write(channel.DeviceIp ?? string.Empty);
        _writer.Write(channel.Name ?? string.Empty);
        _writer.Write(channel.Unit ?? string.Empty);
        _writer.Write(Normalize(computeBackend));
        _writer.Write(Normalize(computeDevice));
        _writer.Write(sampleRate);
        _writer.Write(fftSize);
        _writer.Write(magnitudes.Length);
        _writer.Write(sampleRate / fftSize);
        _writer.Write(magnitudes.Length * sizeof(float));
        WriteFloatSpan(magnitudes);
        Interlocked.Increment(ref _framesWritten);
        FlushIfDue();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }

    private void WriteHeader(AnalysisSettings settings, string initialBackend, string initialDevice)
    {
        _writer.Write(FileMagic);
        _writer.Write(FormatVersion);
        _writer.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _writer.Write(settings.UseSampleRateWindowing ? 0 : settings.WindowSampleCount);
        _writer.Write(settings.UseSampleRateWindowing ? 0 : settings.HopSampleCount);
        _writer.Write(settings.MaxChannels);
        _writer.Write(settings.MaxFftChannels);
        _writer.Write("magnitude_float32");
        _writer.Write(settings.FftBackend.ToString());
        _writer.Write(Normalize(initialBackend));
        _writer.Write(Normalize(initialDevice));
        _writer.Write(settings.UseSampleRateWindowing ? "sample_rate_resolution" : "fixed_samples");
        _writer.Write(settings.FftResolutionHz);
        _writer.Write(settings.FftOverlapRatio);
        _writer.Flush();
        Volatile.Write(ref _lastFlushTicks, Environment.TickCount64);
    }

    private void WriteFloatSpan(ReadOnlySpan<float> values)
    {
        if (BitConverter.IsLittleEndian)
        {
            _stream.Write(MemoryMarshal.AsBytes(values));
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            _writer.Write(values[i]);
        }
    }

    private void FlushIfDue()
    {
        long now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastFlushTicks) < _flushIntervalMs)
        {
            return;
        }

        _writer.Flush();
        Volatile.Write(ref _lastFlushTicks, now);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FftResultWriter));
        }
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }
}
