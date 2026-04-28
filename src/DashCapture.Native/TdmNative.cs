using System;
using System.Runtime.InteropServices;

namespace DashCapture.Native;

public enum DdcDataType
{
    UInt8 = 5,
    Int16 = 2,
    Int32 = 3,
    Float = 9,
    Double = 10,
    String = 23,
    Timestamp = 30
}

public static class TdmNative
{
    private const string LibName = "nilibddc.dll";
    public const string TdmsFileType = "TDMS";

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int DDC_CreateFile(
        string filePath,
        string fileType,
        string name,
        string description,
        string title,
        string author,
        out IntPtr file);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int DDC_AddChannelGroup(
        IntPtr file,
        string name,
        string description,
        out IntPtr channelGroup);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int DDC_AddChannel(
        IntPtr channelGroup,
        DdcDataType dataType,
        string name,
        string description,
        string unitString,
        out IntPtr channel);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall)]
    public static extern int DDC_AppendDataValues(IntPtr channel, IntPtr values, UIntPtr numValues);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall)]
    public static extern int DDC_SaveFile(IntPtr file);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall)]
    public static extern int DDC_CloseFile(IntPtr file);

    [DllImport(LibName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern IntPtr DDC_GetLibraryErrorDescription(int errorCode);

    public static void ThrowIfError(int errorCode, string operation)
    {
        if (errorCode >= 0)
        {
            return;
        }

        string description = Marshal.PtrToStringAnsi(DDC_GetLibraryErrorDescription(errorCode)) ?? "Unknown TDMS error";
        throw new TdmNativeException(operation, errorCode, description);
    }
}

public sealed class TdmNativeException : Exception
{
    public TdmNativeException(string operation, int errorCode, string description)
        : base($"{operation} failed with {errorCode}: {description}")
    {
        Operation = operation;
        ErrorCode = errorCode;
    }

    public string Operation { get; }
    public int ErrorCode { get; }
}
