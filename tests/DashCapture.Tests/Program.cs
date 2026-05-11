using DashCapture.Display;
using DashCapture.Core.Configuration;
using DashCapture.Core.Memory;
using DashCapture.Core.Models;
using DashCapture.Storage;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static (CompressedCaptureManifest Manifest, CompressedCaptureIndex Index) ReadDhcapMetadata(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
    byte[] magic = reader.ReadBytes(CompressedCaptureFormat.Magic.Length);
    Assert(magic.SequenceEqual(CompressedCaptureFormat.Magic), "DHCAP file must contain the expected magic header.");
    int version = reader.ReadInt32();
    Assert(version == CompressedCaptureFormat.Version, "DHCAP file must be written with the current format version.");
    int manifestLength = reader.ReadInt32();
    byte[] manifestBytes = reader.ReadBytes(manifestLength);
    CompressedCaptureManifest manifest = JsonSerializer.Deserialize<CompressedCaptureManifest>(manifestBytes, CompressedCaptureFormat.JsonOptions)
        ?? throw new InvalidOperationException("DHCAP manifest must deserialize.");

    stream.Position = stream.Length - CompressedCaptureFormat.FooterMagic.Length;
    byte[] footerMagic = reader.ReadBytes(CompressedCaptureFormat.FooterMagic.Length);
    Assert(footerMagic.SequenceEqual(CompressedCaptureFormat.FooterMagic), "DHCAP file must end with a v3 footer index marker.");
    stream.Position = stream.Length - CompressedCaptureFormat.FooterMagic.Length - sizeof(int);
    int indexLength = reader.ReadInt32();
    stream.Position = stream.Length - CompressedCaptureFormat.FooterMagic.Length - sizeof(int) - indexLength;
    byte[] indexBytes = reader.ReadBytes(indexLength);
    CompressedCaptureIndex index = JsonSerializer.Deserialize<CompressedCaptureIndex>(indexBytes, CompressedCaptureFormat.JsonOptions)
        ?? throw new InvalidOperationException("DHCAP index must deserialize.");
    return (manifest, index);
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

byte[] payload = Enumerable.Range(0, 8195)
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

    var rawSettings = new CompressionSettings
    {
        Enabled = false,
        Algorithm = CompressionAlgorithm.Zstd,
        Preprocessor = CompressionPreprocessor.FloatXorDelta,
        LpcOrder = 2
    };
    byte[] rawEncoded = CompressedTdmsCodec.EncodePayload(payload, rawSettings, out int rawTransformedLength, out byte rawFlags);
    byte[] rawRestored = CompressedTdmsCodec.DecodePayload(
        rawEncoded,
        rawSettings.Algorithm,
        rawSettings.Preprocessor,
        rawSettings.LpcOrder,
        payload.Length,
        rawTransformedLength,
        rawFlags);
    Assert(rawEncoded.SequenceEqual(payload), "Disabled compression must store original bytes without preprocessing.");
    Assert(rawRestored.SequenceEqual(payload), "Disabled compression .dhcap records must restore original bytes.");

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
        string compressedRunFolder;
        string compressedCurrentPath;
        CaptureStorageStatistics compressedStats;
        using (var writer = new CompressedCaptureWriter(streamSettings, devices))
        {
            writer.AppendBlock(block);
            writer.Save();
            compressedRunFolder = writer.CurrentFolder;
            compressedCurrentPath = writer.CurrentPath;
            compressedStats = writer.Statistics;
            block.Release();
        }

        var compressedMetadata = ReadDhcapMetadata(compressedCurrentPath);
        Assert(compressedMetadata.Manifest.EffectiveCodec == CompressionAlgorithm.Zstd, "Compressed DHCAP manifest must record the effective codec.");
        Assert(compressedMetadata.Manifest.EffectivePreprocessor == CompressionPreprocessor.Delta1, "Compressed DHCAP manifest must record the effective preprocessor.");
        Assert(compressedMetadata.Manifest.RawType == "float32", "DHCAP manifest must record raw type.");
        Assert(!string.IsNullOrWhiteSpace(compressedMetadata.Manifest.ByteOrder), "DHCAP manifest must record byte order.");
        Assert(compressedMetadata.Manifest.CallbackBlockSchema.Contains("SdkSampleData", StringComparison.Ordinal), "DHCAP manifest must describe original callback block metadata.");
        Assert(compressedMetadata.Index.Records.All(record => record.Codec == CompressionAlgorithm.Zstd), "DHCAP index records must carry block-level codec.");
        Assert(compressedMetadata.Index.Records.All(record => record.Preprocessor == CompressionPreprocessor.Delta1), "DHCAP index records must carry block-level preprocessor.");
        Assert(compressedMetadata.Index.Records.All(record => record.CallbackBlocks.Count > 0), "DHCAP index records must preserve source callback block info.");
        Assert(compressedStats.RawBytes > 0 && compressedStats.WrittenBytes > 0, "Storage statistics must expose raw and written sizes.");

        using (TdmsFileReader reader = TdmsFileReader.Open(compressedRunFolder, string.Empty))
        {
            TdmsChannelInfo firstChannel = reader.FileInfo.Groups.Single().Channels.First();
            TdmsChannelEnvelope streamEnvelope = reader.ReadEnvelope(firstChannel, 0, firstChannel.SampleCount, 4, CancellationToken.None);
            Assert(firstChannel.SampleCount == 4, "Streaming compressed capture must preserve channel sample count.");
            Assert(streamEnvelope.Points.Select(point => point.Maximum).SequenceEqual(new[] { 1f, 2f, 3f, 4f }), "Streaming compressed capture must restore exact float values.");
        }

        RentedNativeBuffer rawRented = pool.Rent(interleaved.Length * sizeof(float));
        Marshal.Copy(interleaved, 0, rawRented.Pointer, interleaved.Length);
        var rawHeader = header with { DataPointer = rawRented.Pointer, BlockIndex = 2 };
        var rawBlock = new AcquisitionBlock(rawRented, rawHeader, channelCount: 2);
        streamSettings.Compression.Enabled = false;
        streamSettings.Compression.Preprocessor = CompressionPreprocessor.FloatXorDelta;
        string rawRunFolder;
        string rawCurrentPath;
        using (var writer = new CompressedCaptureWriter(streamSettings, devices))
        {
            writer.AppendBlock(rawBlock);
            writer.Save();
            rawRunFolder = writer.CurrentFolder;
            rawCurrentPath = writer.CurrentPath;
            rawBlock.Release();
        }

        Assert(Path.GetExtension(rawCurrentPath).Equals(".dhcap", StringComparison.OrdinalIgnoreCase), "Capture storage must use .dhcap even when compression is disabled.");
        byte[] rawFileBytes = File.ReadAllBytes(rawCurrentPath);
        Assert(rawFileBytes.AsSpan(rawFileBytes.Length - CompressedCaptureFormat.FooterMagic.Length).SequenceEqual(CompressedCaptureFormat.FooterMagic), "DHCAP v3 files must end with a footer index marker.");
        var rawMetadata = ReadDhcapMetadata(rawCurrentPath);
        Assert(rawMetadata.Manifest.EffectiveCodec == CompressionAlgorithm.None, "Raw DHCAP manifest must record Codec=None.");
        Assert(rawMetadata.Manifest.EffectivePreprocessor == CompressionPreprocessor.None, "Raw DHCAP manifest must record Pre=None.");
        Assert(rawMetadata.Index.Records.All(record => record.Codec == CompressionAlgorithm.None), "Raw DHCAP index records must carry Codec=None.");
        Assert(rawMetadata.Index.Records.All(record => record.Preprocessor == CompressionPreprocessor.None), "Raw DHCAP index records must carry Pre=None.");
        using (TdmsFileReader reader = TdmsFileReader.Open(rawRunFolder, string.Empty))
        {
            TdmsChannelInfo firstChannel = reader.FileInfo.Groups.Single().Channels.First();
            TdmsChannelEnvelope streamEnvelope = reader.ReadEnvelope(firstChannel, 0, firstChannel.SampleCount, 4, CancellationToken.None);
            Assert(firstChannel.SampleCount == 4, "Streaming raw .dhcap capture must preserve channel sample count.");
            Assert(streamEnvelope.Points.Select(point => point.Maximum).SequenceEqual(new[] { 1f, 2f, 3f, 4f }), "Streaming raw .dhcap capture must restore exact float values.");
        }
    }
}
finally
{
    Directory.Delete(tempDir, recursive: true);
}

Console.WriteLine("Display and compression checks passed.");
