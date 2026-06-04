using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DashCapture.Core.Configuration;
using DashCapture.Core.Models;

namespace DashCapture.Core.Sources;

public sealed class RemoteDataSourceAcquisitionSource : IAcquisitionSource
{
    private const string HandshakeLine = "DHCAPSIM1\n";
    private const int BlockHeaderLength = 64;
    private const int BlockMagic = 0x314C4244; // DBL1, little-endian.
    private const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DataSourceClientSettings _settings;
    private readonly List<DeviceDescriptor> _devices = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int _running;

    public RemoteDataSourceAcquisitionSource(DataSourceClientSettings settings)
    {
        _settings = settings;
    }

    public event Action<SdkSampleData>? SampleReceived;
    public IReadOnlyList<DeviceDescriptor> Devices => _devices;
    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(100, _settings.ConnectTimeoutMs));

        var client = new TcpClient
        {
            NoDelay = true
        };

        try
        {
            await client.ConnectAsync(_settings.Host, _settings.Port).WaitAsync(timeout.Token).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            await ReadHandshakeAsync(stream, timeout.Token).ConfigureAwait(false);
            DataSourceMetadata metadata = await ReadMetadataAsync(stream, timeout.Token).ConfigureAwait(false);

            _devices.Clear();
            _devices.AddRange(CreateDevices(metadata));
            _client = client;
            _stream = stream;
            IsConnected = true;

            _readerCts = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadBlocksAsync(_readerCts.Token), CancellationToken.None);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Remote data source is not connected.");
        }

        Volatile.Write(ref _running, 1);
        await SendCommandAsync("START", cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 0);
        if (IsConnected)
        {
            await SendCommandAsync("STOP", cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        _readerCts?.Cancel();
        _client?.Close();

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        _readerCts?.Dispose();
        _readerCts = null;
        _readerTask = null;
        _stream = null;
        _client?.Dispose();
        _client = null;
        IsConnected = false;
    }

    private async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        NetworkStream stream = _stream ?? throw new InvalidOperationException("Remote data source stream is not available.");
        byte[] bytes = Encoding.ASCII.GetBytes(command + "\n");
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private async Task ReadBlocksAsync(CancellationToken cancellationToken)
    {
        NetworkStream? stream = _stream;
        if (stream is null)
        {
            return;
        }

        byte[] headerBuffer = new byte[BlockHeaderLength];
        byte[] payloadBuffer = Array.Empty<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool hasHeader = await ReadFullAsync(stream, headerBuffer, BlockHeaderLength, cancellationToken).ConfigureAwait(false);
                if (!hasHeader)
                {
                    break;
                }

                RemoteBlockHeader header = DecodeHeader(headerBuffer);
                if (header.PayloadByteCount <= 0)
                {
                    continue;
                }

                if (payloadBuffer.Length < header.PayloadByteCount)
                {
                    payloadBuffer = new byte[header.PayloadByteCount];
                }

                bool hasPayload = await ReadFullAsync(stream, payloadBuffer, header.PayloadByteCount, cancellationToken).ConfigureAwait(false);
                if (!hasPayload)
                {
                    break;
                }

                if (Volatile.Read(ref _running) == 0)
                {
                    continue;
                }

                IntPtr nativeBuffer = Marshal.AllocHGlobal(header.PayloadByteCount);
                try
                {
                    Marshal.Copy(payloadBuffer, 0, nativeBuffer, header.PayloadByteCount);
                    SampleReceived?.Invoke(new SdkSampleData(
                        header.SampleTime,
                        "RemoteDataSource",
                        header.MessageType,
                        header.GroupId,
                        ChannelStyle: 0,
                        header.ChannelId,
                        header.MachineId,
                        header.TotalDataCount,
                        header.DataCountPerChannel,
                        header.PayloadByteCount,
                        header.BlockIndex,
                        nativeBuffer,
                        header.Layout));
                }
                finally
                {
                    Marshal.FreeHGlobal(nativeBuffer);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsConnected = false;
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task ReadHandshakeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] expected = Encoding.ASCII.GetBytes(HandshakeLine);
        byte[] actual = new byte[expected.Length];
        if (!await ReadFullAsync(stream, actual, actual.Length, cancellationToken).ConfigureAwait(false) ||
            !actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Remote data source handshake is invalid.");
        }
    }

    private async Task<DataSourceMetadata> ReadMetadataAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        if (!await ReadFullAsync(stream, lengthBuffer, lengthBuffer.Length, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Remote data source closed before metadata was received.");
        }

        int metadataLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        int maxMetadataBytes = Math.Clamp(_settings.MaxMetadataBytes, 1024, 64 * 1024 * 1024);
        if (metadataLength <= 0 || metadataLength > maxMetadataBytes)
        {
            throw new InvalidOperationException($"Remote data source metadata length is invalid: {metadataLength}.");
        }

        byte[] json = new byte[metadataLength];
        if (!await ReadFullAsync(stream, json, json.Length, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Remote data source closed while metadata was being read.");
        }

        return JsonSerializer.Deserialize<DataSourceMetadata>(json, JsonOptions) ??
               throw new InvalidOperationException("Remote data source metadata is empty.");
    }

    private static IReadOnlyList<DeviceDescriptor> CreateDevices(DataSourceMetadata metadata)
    {
        var devices = new List<DeviceDescriptor>();
        foreach (DataSourceDeviceMetadata device in metadata.Devices ?? Array.Empty<DataSourceDeviceMetadata>())
        {
            var channels = new List<ChannelDescriptor>();
            foreach (DataSourceChannelMetadata channel in device.Channels ?? Array.Empty<DataSourceChannelMetadata>())
            {
                int localIndex = channel.LocalDataIndex >= 0 ? channel.LocalDataIndex : channels.Count;
                int dataIndex = channel.DataIndex >= 0 ? channel.DataIndex : localIndex;
                channels.Add(new ChannelDescriptor(
                    device.DeviceId,
                    string.IsNullOrWhiteSpace(device.IpAddress) ? $"SIM-{device.DeviceId + 1:000}" : device.IpAddress,
                    channel.ChannelId,
                    dataIndex,
                    localIndex,
                    channel.Online,
                    string.IsNullOrWhiteSpace(channel.Name) ? $"SIM{device.DeviceId + 1}-CH{channel.ChannelId + 1}" : channel.Name,
                    string.IsNullOrWhiteSpace(channel.Unit) ? "raw" : channel.Unit,
                    IsValidSampleRate(channel.SampleRate) ? channel.SampleRate : device.SampleRate));
            }

            float sampleRate = IsValidSampleRate(device.SampleRate)
                ? device.SampleRate
                : channels.Select(channel => channel.SampleRate).FirstOrDefault(IsValidSampleRate);
            if (!IsValidSampleRate(sampleRate))
            {
                sampleRate = 1;
            }

            devices.Add(new DeviceDescriptor(
                device.DeviceId,
                string.IsNullOrWhiteSpace(device.IpAddress) ? $"SIM-{device.DeviceId + 1:000}" : device.IpAddress,
                sampleRate,
                device.Online,
                channels));
        }

        return devices;
    }

    private static RemoteBlockHeader DecodeHeader(ReadOnlySpan<byte> buffer)
    {
        int magic = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(0, 4));
        if (magic != BlockMagic)
        {
            throw new InvalidOperationException("Remote data source block magic is invalid.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4));
        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(8, 4));
        if (version != ProtocolVersion || headerLength != BlockHeaderLength)
        {
            throw new InvalidOperationException($"Remote data source block header is unsupported: version={version}, length={headerLength}.");
        }

        return new RemoteBlockHeader(
            BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(12, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(24, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(28, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(32, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(36, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(44, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(48, 4)),
            DecodeLayout(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(52, 4))),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(56, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(60, 4)));
    }

    private static SampleDataLayout DecodeLayout(int value)
    {
        return value switch
        {
            1 => SampleDataLayout.ChannelContiguousFloat32,
            _ => SampleDataLayout.SampleInterleavedFloat32
        };
    }

    private static async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool IsValidSampleRate(float sampleRate)
    {
        return sampleRate > 0 && !float.IsNaN(sampleRate) && !float.IsInfinity(sampleRate);
    }

    private readonly record struct RemoteBlockHeader(
        long SampleTime,
        int MessageType,
        int GroupId,
        int ChannelId,
        int MachineId,
        long TotalDataCount,
        int DataCountPerChannel,
        int ChannelCount,
        SampleDataLayout Layout,
        int PayloadByteCount,
        int BlockIndex);

    private sealed class DataSourceMetadata
    {
        public int Version { get; set; }
        public DataSourceDeviceMetadata[] Devices { get; set; } = Array.Empty<DataSourceDeviceMetadata>();
    }

    private sealed class DataSourceDeviceMetadata
    {
        public int DeviceId { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public float SampleRate { get; set; } = 1;
        public bool Online { get; set; } = true;
        public DataSourceChannelMetadata[] Channels { get; set; } = Array.Empty<DataSourceChannelMetadata>();
    }

    private sealed class DataSourceChannelMetadata
    {
        public int ChannelId { get; set; }
        public int DataIndex { get; set; } = -1;
        public int LocalDataIndex { get; set; } = -1;
        public bool Online { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = "raw";
        public float SampleRate { get; set; } = 1;
    }
}
