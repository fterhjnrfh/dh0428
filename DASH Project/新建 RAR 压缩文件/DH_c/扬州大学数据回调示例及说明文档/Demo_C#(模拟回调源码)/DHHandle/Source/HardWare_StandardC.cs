using System;
using System.Runtime.InteropServices;

namespace DHHandle
{
    public class HardWare_StandardC
    {
        public static readonly int StandardCapacity = 204800;
        private const string LibName = "Hardware_Standard_C_Interface.dll";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public delegate void SampleDataChangeEventHandle(long sampleTime, int groupIdSize, IntPtr groupInfo, int nMessageType, int nGroupID, int nChannelStyle, int nChannelID, int nMachineID, long nTotalDataCount, int nDataCountPerChannel, int nBufferCount, int nBlockIndex, long varSampleData);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetDataChangeCallBackFun(SampleDataChangeEventHandle pText);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int DA_ReleaseBuffer(long point);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int InitMacControl(string dll_dir);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void QuitMacControl();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool RefindAndConnecMac();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetAllMacOnlineCount();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "IsExistChnAutoCheck")]
        public static extern byte IsExistChnAutoCheck();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int AllChannelBalance();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int AllChannelClearZeroEx(int nGND);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void StartMacSample();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void StopMacSample();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool GetMacSampleFreqList(IntPtr pFreqList, int nFreqBuffer, out int nUsedBuffer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern float GetMacCurrentSampleFreq();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool SetMacSampleFreq(float fltSampleFreq);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetMacInfoFromIndex(int nIndex, out int pMacID, IntPtr strMacIp, int nMacBuffer, out int nUseBuffer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetMacCurrentChnCount(int nMachineID, string strMacIp);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern byte GetMacLinkStatus(int nMachineID, string strMacIp);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetChannelIDFromAllChannelIndex(int nMachineID, string pMacIp, int nIndex, out int nMacChnId, out int bOnLine);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int DownBalanceData(int nBufferSize, string pXMLChannel);

        /// <summary>
        /// 加载参数
        /// </summary>
        /// <param name="pFilePath"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int LoadMacParameter(string pFilePath);

        /// <summary>
        /// 保存参数
        /// </summary>
        /// <param name="pFilePath"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SaveMacParameter(string pFilePath);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetChannelMeasureType(int nMachineID, string strMachineIP, int nChannelID, out int nMeasureType);


        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool GetMacSampleFreqListEx(int nMachineID, IntPtr pFreqList, int nFreqBuffer, out int nUsedBuffer);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern float GetMacCurrentSampleFreqEx(int nMachineID);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern bool SetMacSampleFreqEx(int nMachineID, float fltSampleFreq);

        /// <summary>
        /// 单通道平衡清零
        /// </summary>
        /// <param name="nBufferSize"></param>
        /// <param name="pXMLChannel"></param>
        /// <param name="bGND"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int ChannelClearZeroEx(int nBufferSize, string pXMLChannel, int bGND);

        /// <summary>
        /// 更改仪器目标IP
        /// </summary>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetUpInterface();

        /// <summary>
        /// 导出平衡文件
        /// </summary>
        /// <param name="paramDir"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetAllChannelBalanceAndZeroValue(string paramDir);

        /// <summary>
        /// 导入平衡文件
        /// </summary>
        /// <param name="paramDir"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetAllChannelBalanceAndZeroValue(string paramDir);

        #region 设置/获取参数
        /// <summary>
        /// 获取参数可选列表
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="strMachineIP"></param>
        /// <param name="nChannelID"></param>
        /// <param name="ShowParamID"></param>
        /// <param name="?"></param>
        /// <param name="?"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetMacChnParamListValue(int nMachineID, string strMachineIP, int nChannelID, int ShowParamID, long pXMLChannel, int nBufferSize, out int nSize);

        /// <summary>
        /// 修改通道参数
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="strMachineIP"></param>
        /// <param name="nChannelID"></param>
        /// <param name="ShowParamID"></param>
        /// <param name="strParamValue"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int ModifyMacChnParam(int nMachineID, string strMachineIP, int nChannelID, int ShowParamID, string strParamValue);

        /// <summary>
        /// 获取参数值
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChannelID"></param>
        /// <param name="strMachineIP"></param>
        /// <param name="ShowParamID"></param>
        /// <param name="?"></param>
        /// <param name="?"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetMacChnCurrentParam(int nMachineID, int nChannelID, string strMachineIP, int ShowParamID, long pXMLChannel, int nBufferSize, out int nSize);

        /// <summary>
        /// 下发参数到硬件
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="strMachineIP"></param>
        /// <param name="nChannelID"></param>
        /// <param name="ShowParamID"></param>
        /// <param name="strParamValue"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SendChannelParamCode(int nBufferSize, string pXMLChannel);

        #endregion

        #region 外部数据源信息读取及设置
        /// <summary>
        /// 获取所有外部数据源类型
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="strMachineIP"></param>
        /// <param name="nChannelID"></param>
        /// <param name="nMeasureType"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOutDataSourceType(int nBufferSize, IntPtr pDataTypeList, out int nSize);

        /// <summary>
        /// 获取多个外部数据源中某一个状态
        /// </summary>
        /// <param name=""></param>
        /// <param name=""></param>
        /// <param name="nOutDataType"></param>
        /// <param name="nBufferSize"></param>
        /// <param name="pInfo"></param>
        /// <param name=""></param>
        /// <param name=""></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOneTypeOutDataSourceStatus(out int nUseOutDataSource, int nOutDataType, int nBufferSize, IntPtr BufferPoint, out int nReceiveCount);

        /// <summary>
        /// 设置多个外部数据源某一个状态
        /// </summary>
        /// <param name="nUseOutDataSource"></param>
        /// <param name="nOutDataType"></param>
        /// <param name="pInfo">strIP,Port</param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetOneTypeOutDataSourceStatus(int nUseOutDataSource, int nOutDataType, string pInfo);


        /// <summary>
        /// 获取外部数据源Count
        /// </summary>
        /// <param name="nUseOutDataSource"></param>
        /// <param name="nOutDataType"></param>
        /// <param name="pInfo"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOutDataSourceCount(out int nOutDataSourceCount);

        /// <summary>
        /// 获取单种外部数据源信息
        /// </summary>
        /// <param name="nIndex"></param>
        /// <param name="nMachineID"></param>
        /// <param name="strMacIp"></param>
        /// <param name="nChnCount"></param>
        /// <param name="nOutType"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOutDataSourceMacIDFromIndex(int nIndex, out int nMachineID, string strMacIp, out int nChnCount, out int nOutType);

        /// <summary>
        /// 获取单种外部数据源单个通道信息
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChnIndex"></param>
        /// <param name="nType">0,通道名称</param>
        /// <param name="strParam"></param>
        /// <param name="nSize"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOutDataSourceChannelFromIndex(int nMachineID, int nChnIndex, int nType, IntPtr strParam, out int nSize);
        #endregion

        #region 获取仪器数据接口
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int ChangeGetDataStatus(bool nSingleGetData);

        /// <summary>
        /// 获取单台仪器数据，某个通道在数据中位置
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChnId"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOneMacDataIndex(int nMachineID, int nChnId);

        /// <summary>
        /// 获取单台仪器数据
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nChnCount"></param>
        /// <param name="lTotalPos"></param>
        /// <param name="lBufferSize"></param>
        /// <param name="pBufferAddr"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOneMacChnData_New(int nMachineID, out long nReceiveCount, out long nChnCount, out long lTotalPos, int lBufferSize, IntPtr pBufferAddr);

        /// <summary>
        /// 不同通道不同采样频率获取数据
        /// </summary>
        /// <param name="nMacId"></param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nChnCount"></param>
        /// <param name="lTotalPos"></param>
        /// <param name="lBufferSize"></param>
        /// <param name="pBufferAddr"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOneMacChnData_CommonRate(int nMacId, out long nReceiveCount, out long nChnCount, out long lTotalPos, int lBufferSize, IntPtr pBufferAddr);

        /// <summary>
        /// 获取485通道数据
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChannelID"></param>
        /// <param name="nBufferSize"></param>
        /// <param name="pBufferAddr"></param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nSignalCount"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetRS485ChnData_New(int nMachineID, int nChannelID, int nBufferSize, IntPtr pBufferAddr, out long nReceiveCount, out long nSignalCount);

        /// <summary>
        /// 获取所有数据，某个通道在数据中位置
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChnId"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetAllMacDataIndex(int nMachineID, int nChnId);

        /// <summary>
        /// 获取所有仪器数据
        /// </summary>
        /// <param name="lBufferSize"></param>
        /// <param name="pBufferAddr"></param>
        /// <param name="lTotalPos"></param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nChnCount"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetAllMacChnData(int lBufferSize, IntPtr pBufferAddr, out long lTotalPos, out long nReceiveCount, out long nChnCount);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOneMacPluseData(int nMachineID, out long nReceiveCount, out long nChnCount, int lBufferSize, IntPtr pBufferAddr, int lBufferPosSize, IntPtr pBufferPosAddr);

        /// <summary>
        /// 设置每次获取的数据量
        /// </summary>
        /// <param name="nChnId"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetGetDataCountEveryTime(int nDataCount);

        #region 5972N(155)&&5974N(295)特殊
        /// <summary>
        /// 获取通道分组id
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="ip"></param>
        /// <param name="nChnId"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void GetChannelTeamID(int nMachineID, string ip, int nChnId, out int nTeamID);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void GetOneMacTeamChnData_New(int nMachineID, int nTeamID, IntPtr point, int nBufferSize, out long nTotalDataPos, out long nReceiveCount, out long nChnCount, out int ReturnValue);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void GetDataIndexInTeam(int nMachineID, string pIP, int nChnId, int nTeamID, out int nDataIndex, out int ReturnValue);
        #endregion

        #region IO通道读取
        /// <summary>
        /// 获取IO通道数据
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChannelID"></param>
        /// <param name="nIndex">DO通道对应ID</param>
        /// <param name="nBufferSize">bufferPoint对应内存的字节大小</param>
        /// <param name="BufferPoint">存储通道数据的内存地址</param>
        /// <param name="nReceiveCount">通道数据量</param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int DA_GetIOChnData(int nMachineID, int nChannelID, int nIndex, int nBufferSize, IntPtr BufferPoint, out long nReceiveCount);

        /// <summary>
        /// 获取IO通道输入/输出
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="nChannelID"></param>
        /// <param name="nBufferSize"></param>
        /// <param name="strInfo"> IO通道对应DO/DI状态 0：DO 1：DI</param>
        /// <param name="nReturnSize"></param>
        /// <param name="nIOChnCount">IO通道DI+DO通道总数</param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int DA_GetIOChnStatus(int nMachineID, int nChannelID, int nBufferSize, IntPtr strInfo, out int nReturnSize, out int nIOChnCount);

        /// <summary>
        /// 写IO通道输入信息
        /// </summary>
        /// <param name="nGroupChannelID">仪器ID</param>
        /// <param name="nSize"></param>
        /// <param name="pIP">仪器IP</param>
        /// <param name="nChannelID">获取数据的通道号</param>
        /// <param name="nCellID">通道拆分的ID(一般情况下为0)</param>
        /// <param name="nOutValue"></param>
        /// <returns>–返回值 0 – 发送失败, 1 – 发送成功</returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int WriteIOChnOutputValue(int nGroupChannelID, int nSize, string pIP, int nChannelID, int nCellID, int nOutValue);
        #endregion

        /// <summary>
        /// 获取单种外部数据源数据
        /// </summary>
        /// <param name="nMachineID"></param>
        /// <param name="bufferSize"></param>
        /// <param name="pBuffer"></param>
        /// <param name="lTotalPos"></param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nChnCount"></param>
        /// <returns></returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetOutDataData(int nMachineID, int bufferSize, IntPtr pBuffer, out long lTotalPos, out long nReceiveCount, out long nChnCount);

        /// <summary>
        /// 获取仪器采集的统计信息(GPS、转速等)
        /// </summary>
        /// <param name="nMachineID"> -仪器ID</param>
        /// <param name="nChannelStyle">–获取数据的类型(1 –控制卡转速数据，4 – GPS数据)</param>
        /// <param name="nValueType">- 值类型(获取控制卡转速时代表通道ID；获取GPS数据时，参见GPSEnum)</param>
        /// <param name="fltTime">采集的统计数据的时间</param>
        /// <param name="fltValue">采集的统计数据的值</param>
        /// <returns>0 未获取到数据;  1获取到数据</returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetSampleStatValue(int nMachineID, int nChannelStyle, int nValueType, out float fltTime, out float fltValue);

        /// <summary>
        /// 获取仪器采集的CAN通道原始数据
        /// </summary>
        /// <param name="nMachineID">仪器ID</param>
        /// <param name="nChannelID">CAN通道ID</param>
        /// <param name="nBufferSize">pBufferAddr对应内存的字节大小</param>
        /// <param name="pBufferAddr">存储通道数据的内存地址（格式为 pos + CAN_DATA）</param>
        /// <param name="nValueSize">通道数据量</param>
        /// <returns>返回int– 
        /// 0  未获取到数据  
        /// 1  获取到数据 
        /// -1 没有此机号的can数据
        /// -2 没有此通道号的can数据
        /// -3 此通道号未有can数据
        /// -4 输入的指针太小
        /// </returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int GetExtraCANChnData(int nMachineID, int nChannelID, int nBufferSize, IntPtr pBufferAddr, out int nValueSize);
        #endregion
    }
}
