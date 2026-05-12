namespace DashCapture.Core.Configuration;

public sealed class CaptureSettings
{
    public SdkSettings Sdk { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public QueueSettings Queues { get; set; } = new();
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
    public int CompressionQueueCapacityBlocks { get; set; } = 128;
    public int WriteQueueCapacityBlocks { get; set; } = 128;
    public string TdmRuntimeDir { get; set; } = ".\\TDM C DLL[\u5B98\u65B9\u6E90\u6587\u4EF6]\\dev\\bin\\64-bit";
    public bool EnableRawBlockAudit { get; set; } = true;
    public FileNamingMode NamingMode { get; set; } = FileNamingMode.Time;
    public string CustomFileName { get; set; } = "DashCapture";
    public CompressionSettings Compression { get; set; } = new();
}

public enum FileNamingMode
{
    Time,
    Custom
}

public sealed class CompressionSettings
{
    public bool Enabled { get; set; } = true;
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Zstd;
    public CompressionPreprocessor Preprocessor { get; set; } = CompressionPreprocessor.ByteShuffle;
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
    public int TargetFps { get; set; } = 30;
    public int WindowSeconds { get; set; } = 5;
    public int MaxVisibleChannels { get; set; } = 16;
    public int MaxDisplayPointsPerSecond { get; set; } = 4000;
    public float DefaultYAxisAmplitude { get; set; }
}

public sealed class QueueSettings
{
    public int StorageCapacityBlocks { get; set; } = 512;
    public int DisplayCapacityBlocks { get; set; } = 32;
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
