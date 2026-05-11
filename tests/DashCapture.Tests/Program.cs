using DashCapture.Display;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;
using DashCapture.Storage;
using System.Runtime.InteropServices;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

float[] spike = new float[10_000];
spike[4321] = 100;
EnvelopePoint[] envelope = EnvelopeDownsampler.Downsample(spike, 100);
Assert(envelope.Any(p => p.Maximum == 100), "Envelope downsampling must preserve spikes.");

float[] square = Enumerable.Range(0, 1000).Select(i => i < 500 ? -1f : 1f).ToArray();
EnvelopePoint[] squareEnvelope = EnvelopeDownsampler.Downsample(square, 50);
Assert(squareEnvelope.Any(p => p.Minimum < 0) && squareEnvelope.Any(p => p.Maximum > 0), "Envelope downsampling must preserve square wave levels.");

float[] noisy = { float.NaN, -2, float.PositiveInfinity, 3, float.NegativeInfinity };
EnvelopePoint[] noisyEnvelope = EnvelopeDownsampler.Downsample(noisy, 1);
Assert(noisyEnvelope[0].Minimum == -2 && noisyEnvelope[0].Maximum == 3, "Envelope downsampling must ignore non-finite samples.");

EnvelopePoint[] mergedEnvelope = EnvelopeDownsampler.Downsample(new[]
{
    new EnvelopePoint(0, 1, 2, -10, 4),
    new EnvelopePoint(1, 2, 3, -1, 99),
    new EnvelopePoint(2, 3, 4, -3, 5)
}, 1);
Assert(mergedEnvelope[0].Minimum == -10 && mergedEnvelope[0].Maximum == 99, "Envelope merging must preserve extrema.");

var ring = new WaveformRingBuffer(5);
ring.Append(new float[] { 1, 2, 3, 4, 5 });
WaveformRingBuffer resized = ring.Resize(3);
Assert(resized.Snapshot().SequenceEqual(new float[] { 3, 4, 5 }), "Ring buffer resize must preserve newest samples.");

var store = new WaveformStore(8);
var channel = new ChannelDescriptor(0, "127.0.0.1", 0, 0, 0, true, "AI1", SampleRate: 1_000_000);
store.SetVisibleChannels(new[] { channel });
store.AppendEnvelope(channel, new[] { new EnvelopePoint(0, 1, 2, -5, 7) }, 4000);
var snapshot = store.SnapshotSeries(new[] { channel }).Single();
Assert(snapshot.Channel.SampleRate == 1_000_000, "Waveform store must keep the raw hardware sample rate on channel metadata.");
Assert(snapshot.DisplaySampleRate == 4000, "Waveform store must expose a separate display sample rate.");
Assert(snapshot.Points[0].Minimum == -5 && snapshot.Points[0].Maximum == 7, "Waveform store must keep envelope extrema.");
Assert(store.SnapshotSeries(Array.Empty<ChannelDescriptor>()).Count == 0, "Empty monitor views must not render all channels.");

byte[] payload = Enumerable.Range(0, 8192)
    .Select(i => (byte)((i * 17 + i / 11) & 0xFF))
    .ToArray();
string tempDir = Path.Combine(Path.GetTempPath(), "DashCaptureCompressionTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
try
{
    foreach (CompressionAlgorithm algorithm in Enum.GetValues<CompressionAlgorithm>())
    {
        foreach (CompressionPreprocessor preprocessor in Enum.GetValues<CompressionPreprocessor>())
        {
            var settings = new CompressionSettings
            {
                Enabled = true,
                Algorithm = algorithm,
                Preprocessor = preprocessor,
                ChunkSizeMb = 1,
                ZstdLevel = 3,
                Lz4HcLevel = 9,
                ZlibLevel = 6,
                BZip2BlockSize = 9,
                LpcOrder = 2
            };

            byte[] encoded = CompressedTdmsCodec.EncodePayload(payload, settings, out int transformedLength, out byte flags);
            byte[] restored = CompressedTdmsCodec.DecodePayload(
                encoded,
                algorithm,
                preprocessor,
                settings.LpcOrder,
                payload.Length,
                transformedLength,
                flags);
            Assert(restored.SequenceEqual(payload), $"{algorithm}+{preprocessor} must round-trip losslessly.");
        }
    }

    string streamRoot = Path.Combine(tempDir, "streaming");
    Directory.CreateDirectory(streamRoot);
    var streamSettings = new StorageSettings
    {
        Enabled = true,
        RootPath = streamRoot,
        FileSplitMb = 1024,
        EnableRawBlockAudit = false,
        Compression = new CompressionSettings
        {
            Enabled = true,
            Algorithm = CompressionAlgorithm.Zstd,
            Preprocessor = CompressionPreprocessor.Delta1,
            ZstdLevel = 3,
            LpcOrder = 2
        }
    };
    var devices = new[]
    {
        new DeviceDescriptor(
            0,
            "127.0.0.1",
            1000,
            true,
            new[]
            {
                new ChannelDescriptor(0, "127.0.0.1", 0, 0, 0, true, "AI1", SampleRate: 1000),
                new ChannelDescriptor(0, "127.0.0.1", 1, 1, 1, true, "AI2", SampleRate: 1000)
            })
    };
    using (var pool = new NativeSlabPool(1024, 1))
    {
        float[] interleaved = { 1, 10, 2, 20, 3, 30, 4, 40 };
        RentedNativeBuffer rented = pool.Rent(interleaved.Length * sizeof(float));
        Marshal.Copy(interleaved, 0, rented.Pointer, interleaved.Length);
        var header = new SdkSampleData(
            SampleTime: 0,
            GroupInfo: string.Empty,
            MessageType: DashSampleMessageType.AnalogData,
            GroupId: 0,
            ChannelStyle: 0,
            ChannelId: 0,
            MachineId: 0,
            TotalDataCount: interleaved.Length,
            DataCountPerChannel: 4,
            BufferCount: interleaved.Length * sizeof(float),
            BlockIndex: 1,
            DataPointer: rented.Pointer);
        var block = new AcquisitionBlock(rented, header, channelCount: 2);
        using (var writer = new CompressedCaptureWriter(streamSettings, devices))
        {
            writer.AppendBlock(block);
            writer.Save();
            string runFolder = writer.CurrentFolder;
            block.Release();

            using TdmsFileReader reader = TdmsFileReader.Open(runFolder, string.Empty);
            TdmsChannelInfo firstChannel = reader.FileInfo.Groups.Single().Channels.First();
            TdmsChannelEnvelope streamEnvelope = reader.ReadEnvelope(firstChannel, 0, firstChannel.SampleCount, 4, CancellationToken.None);
            Assert(firstChannel.SampleCount == 4, "Streaming compressed capture must preserve channel sample count.");
            Assert(streamEnvelope.Points.Select(point => point.Maximum).SequenceEqual(new[] { 1f, 2f, 3f, 4f }), "Streaming compressed capture must restore exact float values.");
        }
    }
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

Console.WriteLine("Display and compression checks passed.");
