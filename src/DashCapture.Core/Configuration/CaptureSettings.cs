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
    public int DataCountEveryTime { get; set; } = 1024;
    public GetDataType GetDataType { get; set; } = GetDataType.SingleMachine;
}

public sealed class StorageSettings
{
    public string RootPath { get; set; } = @".\Data";
    public int FileSplitGb { get; set; } = 8;
    public int FlushIntervalMs { get; set; } = 1000;
    public string TdmRuntimeDir { get; set; } = @".\TDM C DLL[官方源文件]\dev\bin\64-bit";
    public FileNamingMode NamingMode { get; set; } = FileNamingMode.Time;
    public string CustomFileName { get; set; } = "DashCapture";
}

public enum FileNamingMode
{
    Time,
    Custom
}

public sealed class DisplaySettings
{
    public int TargetFps { get; set; } = 30;
    public int WindowSeconds { get; set; } = 5;
    public int MaxVisibleChannels { get; set; } = 16;
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
