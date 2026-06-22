namespace DashCapture.Core.Configuration;

public sealed class CaptureSettings
{
    public PlatformSettings Platform { get; set; } = new();
    public AcquisitionSettings Acquisition { get; set; } = new();
    public SdkSettings Sdk { get; set; } = new();
    public DataSourceClientSettings DataSource { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public AnalysisSettings Analysis { get; set; } = new();
    public QueueSettings Queues { get; set; } = new();
}

public sealed class PlatformSettings
{
    public bool PreferGpu { get; set; } = true;
    public bool AllowCpuFallback { get; set; } = true;
    public bool EnableGpuRendering { get; set; } = true;
    public int GpuResourceCacheMb { get; set; } = 512;
    public string NativeLibraryRoot { get; set; } = @".\native";
    public Dictionary<string, string[]> NativeLibraryDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["win-x64"] = new[] { @".\native\win-x64" },
        ["win-arm64"] = new[] { @".\native\win-arm64" },
        ["linux-x64"] = new[] { "./native/linux-x64" },
        ["linux-arm64"] = new[] { "./native/linux-arm64" },
        ["osx-x64"] = new[] { "./native/osx-x64" },
        ["osx-arm64"] = new[] { "./native/osx-arm64" }
    };
}

public sealed class AcquisitionSettings
{
    public AcquisitionSourceMode Source { get; set; } = AcquisitionSourceMode.DashSdk;
}

public enum AcquisitionSourceMode
{
    DashSdk,
    RemoteDataSource
}

public sealed class DataSourceClientSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5055;
    public int ConnectTimeoutMs { get; set; } = 5000;
    public int MaxMetadataBytes { get; set; } = 4 * 1024 * 1024;
}

public sealed class SdkSettings
{
    public string DashRoot { get; set; } = @".\DASH Project\DASH";
    public string ConfigDir { get; set; } = @".\DASH Project\DASH\Config";
    public int DataCountEveryTime { get; set; } = 65536;
    public SdkReadoutMode ReadoutMode { get; set; } = SdkReadoutMode.PollEachDevice;
    public GetDataType GetDataType { get; set; } = GetDataType.SingleMachine;
    public string ParamDir { get; set; } = @".\DASH Project\DASH\Params";
    public int PollIntervalMs { get; set; } = 1;
    public int PollBufferMb { get; set; } = 64;
    public int MaxPollBlocksPerDevice { get; set; } = 32;
    public int MaxDeviceCount { get; set; }
}

public sealed class StorageSettings
{
    public bool Enabled { get; set; } = true;
    public string RootPath { get; set; } = @".\Data";
    public int FileSplitGb { get; set; } = 8;
    public int FileSplitMb { get; set; } = 65536;
    public int FlushIntervalMs { get; set; } = 1000;
    public int DrainTimeoutMs { get; set; } = 300000;
    public int CompressionWorkerCount { get; set; }
    public int CompressionQueueCapacityBlocks { get; set; } = 1024;
    public int WriteQueueCapacityBlocks { get; set; } = 1024;
    public string TdmRuntimeDir { get; set; } = ".\\TDM C DLL[\u5B98\u65B9\u6E90\u6587\u4EF6]\\dev\\bin\\64-bit";
    public bool EnableRawBlockAudit { get; set; } = false;
    public FileNamingMode NamingMode { get; set; } = FileNamingMode.Time;
    public string CustomFileName { get; set; } = "DashCapture";
    public StorageChannelSelectionSettings ChannelSelection { get; set; } = new();
    public CompressionSettings Compression { get; set; } = new();
}

public enum FileNamingMode
{
    Time,
    Custom
}

public sealed class StorageChannelSelectionSettings
{
    public StorageChannelSelectionMode Mode { get; set; } = StorageChannelSelectionMode.AllChannels;
    public double? SampleRateMinHz { get; set; }
    public double? SampleRateMaxHz { get; set; }
    public List<MonitorChannelSettings> Channels { get; set; } = new();
}

public enum StorageChannelSelectionMode
{
    AllChannels,
    SelectedChannels,
    SampleRateRange
}

public sealed class CompressionSettings
{
    public bool Enabled { get; set; } = true;
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Lz4;
    public CompressionPreprocessor Preprocessor { get; set; } = CompressionPreprocessor.Delta1;
    public int ChunkSizeMb { get; set; } = 4;
    public int ZstdLevel { get; set; } = 3;
    public int ZstdWindowLog { get; set; } = 0;
    public int Lz4Level { get; set; } = 0;
    public int Lz4HcLevel { get; set; } = 9;
    public int ZlibLevel { get; set; } = 6;
    public int BZip2BlockSize { get; set; } = 9;
    public int LpcOrder { get; set; } = 2;
}

public enum CompressionAlgorithm
{
    None,
    Zstd,
    Lz4,
    Snappy,
    Zlib,
    Lz4Hc,
    BZip2
}

public enum CompressionPreprocessor
{
    None,
    Delta1,
    Delta2,
    Lpc,
    ByteShuffle,
    FloatXorDelta,
    DeltaFloatPredictor,
    IntDeltaZigZag
}

public sealed class DisplaySettings
{
    public int TargetFps { get; set; } = 60;
    public int WindowSeconds { get; set; } = 5;
    public int MaxVisibleChannels { get; set; } = 16;
    public int MaxDisplayPointsPerSecond { get; set; } = 4000;
    public double RenderBucketScale { get; set; } = 1.0;
    public float DefaultYAxisAmplitude { get; set; }
    public List<MonitorViewSettings> Views { get; set; } = new();
}

public sealed class AnalysisSettings
{
    public bool Enabled { get; set; } = false;
    public int WindowSampleCount { get; set; } = 1_000_000;
    public int HopSampleCount { get; set; } = 100_000;
    public int MaxChannels { get; set; } = 256;
    public bool ComputeFft { get; set; } = true;
    public FftComputeBackend FftBackend { get; set; } = FftComputeBackend.Auto;
    public int MaxFftChannels { get; set; } = 256;
    public bool UseSampleRateWindowing { get; set; } = true;
    public double FftResolutionHz { get; set; } = 1.0;
    public double FftOverlapRatio { get; set; } = 0.9;
    public int FftWindowQueueCapacity { get; set; } = 8192;
    public int FftBatchSize { get; set; } = 64;
    public int FftDrainTimeoutMs { get; set; } = 300000;
    public bool KeepWindowSamples { get; set; } = false;
    public bool PersistResults { get; set; } = false;
    public string ResultRootPath { get; set; } = @".\Analysis";
}

public enum FftComputeBackend
{
    Auto,
    Gpu,
    Cpu
}

public sealed class MonitorViewSettings
{
    public string Name { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public List<MonitorChannelSettings> Channels { get; set; } = new();
}

public sealed class MonitorChannelSettings
{
    public string DeviceIp { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public int ChannelId { get; set; }
}

public sealed class QueueSettings
{
    public int StorageCapacityBlocks { get; set; } = 512;
    public int StorageWriteTimeoutMs { get; set; } = 5000;
    public int DisplayCapacityBlocks { get; set; } = 32;
    public int AnalysisCapacityBlocks { get; set; } = 64;
    public int SlabSizeMb { get; set; } = 8;
    public int SlabCount { get; set; } = 256;
}

public enum GetDataType
{
    SingleMachine,
    MultiMachine,
    TeamMachine
}

public enum SdkReadoutMode
{
    Callback,
    PollEachDevice
}
