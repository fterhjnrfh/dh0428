using System;
using System.Runtime.InteropServices;

namespace DashCapture.Native;

public static class DashSdkNative
{
    public const int StandardCapacity = 204800;
    private const string LibName = "Hardware_Standard_C_Interface.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public delegate void SampleDataChangeEventHandle(
        long sampleTime,
        int groupIdSize,
        IntPtr groupInfo,
        int nMessageType,
        int nGroupID,
        int nChannelStyle,
        int nChannelID,
        int nMachineID,
        long nTotalDataCount,
        int nDataCountPerChannel,
        int nBufferCount,
        int nBlockIndex,
        long varSampleData);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int SetDataChangeCallBackFun(SampleDataChangeEventHandle pText);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int DA_ReleaseBuffer(long point);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void InitMacControl(string dll_dir);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void QuitMacControl();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool RefindAndConnecMac();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetAllMacOnlineCount();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetMacInfoFromIndex(int nIndex, out int pMacID, IntPtr strMacIp, int nMacBuffer, out int nUseBuffer);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetMacCurrentChnCount(int nMachineID, string strMacIp);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern byte GetMacLinkStatus(int nMachineID, string strMacIp);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetChannelIDFromAllChannelIndex(int nMachineID, string pMacIp, int nIndex, out int nMacChnId, out int bOnLine);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetOneMacDataIndex(int nMachineID, int nChnId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetOneMacChnData_New(
        int nMachineID,
        out long nReceiveCount,
        out long nChnCount,
        out long lTotalPos,
        int lBufferSize,
        IntPtr pBufferAddr);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetAllMacChnData(
        int lBufferSize,
        IntPtr pBufferAddr,
        out long lTotalPos,
        out long nReceiveCount,
        out long nChnCount);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int GetAllMacDataIndex(int nMachineID, int nChnId);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool GetMacSampleFreqList(IntPtr pFreqList, int nFreqBuffer, out int nUsedBuffer);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern float GetMacCurrentSampleFreq();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool SetMacSampleFreq(float fltSampleFreq);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int LoadMacParameter(string pFilePath);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool GetMacSampleFreqListEx(int nMachineID, IntPtr pFreqList, int nFreqBuffer, out int nUsedBuffer);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern float GetMacCurrentSampleFreqEx(int nMachineID);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool SetMacSampleFreqEx(int nMachineID, float fltSampleFreq);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int ChangeGetDataStatus(bool nSingleGetData);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int SetGetDataCountEveryTime(int nDataCount);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void StartMacSample();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern void StopMacSample();
}
