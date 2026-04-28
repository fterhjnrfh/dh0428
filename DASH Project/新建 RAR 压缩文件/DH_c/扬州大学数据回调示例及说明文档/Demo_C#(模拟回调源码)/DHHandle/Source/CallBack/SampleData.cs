using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DHHandle
{
    /// <summary>
    /// 从仪器接收的数据
    /// </summary>
    public class SampleData
    {
        /// <summary>
        /// 所有仪器的模拟通道的数据
        /// </summary>
        public const int SAMPLE_ANALOG_DATA = 0;	    // 
        /// <summary>
        /// 单台仪器静态通道数据
        /// </summary>
        public const int SAMPLE_RECEIVE_STATIC = 1;     // 
        /// <summary>
        /// 单台仪器回收的模拟通道数据
        /// </summary>
        public const int SAMPLE_REBACK_SINGLEGROP_ANALOGDATA = 3;	// 
        /// <summary>
        /// 静态通道回收的数据
        /// </summary>
        public const int SAMPLE_REBACK_STATIC_DATA = 4;	// 
        /// <summary>
        /// 单台仪器的数据
        /// </summary>
        public const int SAMPLE_SINGLEGROUP_ANALOGDATA = 5;
        /// <summary>
        /// 不关联仪器的静态数据
        /// </summary>
        public const int SAMPLE_STATIC_NOTASSICATEINSTR = 6;
        /// <summary>
        /// 外部数据源数据
        /// </summary>
        public const int SAMPLE_OUT_DATASOURCE = 7;
        /// <summary>
        /// 数字量数据
        /// </summary>
        public const int SAMPLE_DIGITAL_DATASOURCE = 8;
        /// <summary>
        /// 回收回收丢失数据
        /// </summary>
        public const int SAMPLE_REBACK_LOST_ANALOG_DATA = 9;
        /// <summary>
        /// 单台仪器不同组数据
        /// </summary>
        public const int SAMPLE_SINGLEGROUP_TEAM_ANALOGDATA = 10;
        /// <summary>
        /// 单台仪器不同组数据回收
        /// </summary>
        public const int SAMPLE_REBACK_TEAM_ANALOGDATA = 11;
        /// <summary>
        /// MOOG的IO通道数据
        /// </summary>
        public const int SAMPLE_MOOG_IO_DATA = 12;
        /// <summary>
        /// 回收丢失的静态数据
        /// </summary>
        public const int SAMPLE_REBACK_LOST_STATIC_DATA = 13;
        /// <summary>
        /// 本地计算机时间
        /// </summary>
        public const int SAMPLE_COMPUTER_TIME_DATA = 14;

        /// <summary>
        /// 成飞MTS模式下的数据
        /// </summary>
        public const int SAMPLE_MTS_STORE_DATA = 15;
        /// <summary>
        /// 成飞MTS模式下的实时显示数据
        /// </summary>
        public const int SAMPLE_MTS_REALTIME_DATA = 16;

        /// <summary>
        /// 单通道数据
        /// </summary>
        public const int SAMPLE_ANALOG_SINGLECHN_DATA = 17;
        /// <summary>
        /// 单通道数据回收
        /// </summary>
        public const int SAMPLE_REBACK_SINGLECHN_DATA = 18;
        /// <summary>
        /// 单台仪器不同通道不同频率数据
        /// </summary>
        public const int SAMPLE_SINGLEGROUP_MULTIFREQ_DATA = 19;
        /// <summary>
        /// 多通道数据（抽点后的数据）
        /// </summary>
        public const int SAMPLE_ANALOG_MULTIFREQCHN_DATA = 20;
        /// <summary>
        /// 多通道数据(未抽点数据，目前用于CS模式时，客户端发送多个通道数据时使用)
        /// </summary>
        public const int SAMPLE_ANALOG_MULTICHN_DATA = 21;
        /// <summary>
        /// 静态通道回收的数据
        /// </summary>
        public const int SAMPLE_REBACK_STATIC_DATA_NOTASSICATEINSTR = 22;

        /// <summary>
        /// 多通道组数据（上层使用）
        /// </summary>
        public const int SAMPLE_RECEIVE_ANALOG_MULTIGROUP = 23;

        /// <summary>
        /// 单台仪器数据，抽点发送
        /// </summary>
        public const int SAMPLE_SINGLEGROUP_ANALOGDATA_JUMP = 24;
        /// <summary>
        /// Team仪器数据发送
        /// </summary>
        public const int SAMPLE_REBACK_TEAM_LOST_DATA = 25;

        /// <summary>
        /// 5972N pci端采样数据
        /// </summary>
        public const int SAMPLE_5972N_PCI_ANALOG_DATA = 26;

        /// <summary>
        /// Linux回收丢包数据
        /// </summary>
        public const int SAMPLE_REBACK_LOST_ANALOG_DATAEX = 27;

        /// <summary>
        /// 回收本地计算机时间
        /// </summary>
        public const int REBACK_COMPUTER_TIME_DATA = 28;

        /// <summary>
        /// 485 422 原始未解析数据(##实时数据##)
        /// </summary>
        public const int SAMPLE_RS422_ORIGINAL_DATA = 29;

        /// <summary>
        /// 硬件传送的扭振数据
        /// </summary>
        public const int SAMPLE_PLUSE_NZ_DATA = 30;

        /// <summary>
        /// 回收不同通道不同采样频率数据
        /// </summary>
        public const int SAMPLE_REBACK_MULTIFREQ_ANALOGDATA = 31;

        /// <summary>
        /// 成飞MTS模式下的采样过程中MTS文件数据更新
        /// </summary>
        public const int SAMPLE_MTS_FILE_UPDATE = 35;

        /// <summary>
        /// 2002G单台仪器数据之前工程
        /// </summary>
        public const int SAMPLE_MACHINE_ANALOG_DATA_BEFORE = 39;
        /// <summary>
        /// 单通道数据(抽点后数据)
        /// </summary>
        public const int SAMPLE_ANALOG_MUTLI_SINGLECHN_DATA = 40;
        /// <summary>
        /// 单台仪器 单通道数据，抽点发送
        /// </summary>
        public const int SAMPLE_SINGLEGROUP_ONECHNDATA_JUMP = 41;

        /// <summary>
        /// 单通道数据丢包回收
        /// </summary>
        public const int SAMPLE_REBACK_LOST_ANALOG_ONECHN_DATA = 42;

        /// <summary>
        /// 回收二进制文件数据
        /// </summary>
        public const int SAMPLE_REBACK_BINARAY_DATA = 43;

        /// <summary>
        /// 回收二进制文件外部数据源数据
        /// </summary>
        public const int SAMPLE_REBACK_BINARAY_OUT_DATA = 44;

        /// <summary>
        /// 北京声望试飞院 倍频程数据
        /// </summary>
        public const int SAMPLE_ANALYSIS_SIGNAL_DATA = 45;  // 信号分析数据(5955R倍频程)

        /// <summary>
        /// 单板卡数据(##实时数据##)
        /// </summary>
        public const int SAMPLE_ANALOG_ONECARD_DATA = 46;
        /// <summary>
        /// 单板卡数据回收
        /// </summary>
        public const int SAMPLE_REBACK_ANALOG_ONECARD_DATA = 47;
        /// <summary>
        /// 单板卡数据丢包回收
        /// </summary>
        public const int SAMPLE_LOST_ANALOG_ONECARD_DATA = 48;
        /// <summary>
        /// 实时数采数据丢包回收
        /// </summary>
        public const int SAMPLE_REALTIME_REBACK_ANALOG_LOSTDATA = 49;
        /// <summary>
        /// 实时静态数据丢包回收
        /// </summary>
        public const int SAMPLE_REALTIME_REBACK_STATIC_LOSTDATA = 50;

        /// <summary>
        /// 底层回调接收到的数据pos
        /// </summary>
        internal static long m_lCallBackPos;

        /// <summary>
        /// 消息ID
        /// </summary>
        public int m_nMessageID;

        /// <summary>
        /// 通道风格
        /// </summary>
        public int m_nChannelStyle;
        /// <summary>
        /// 通道组ID
        /// </summary>
        public int m_nChannelGroupID;
        /// <summary>
        /// 通道组对应的IP
        /// </summary>
        public string m_strMachineIP;
        /// <summary>
        /// 仪器ID
        /// </summary>
        public int m_nMachineID;
        /// <summary>
        /// 通道ID
        /// </summary>
        public int m_nMacChnID;

        /// <summary>
        /// 触发块索引
        /// </summary>
        public int m_nTrigBlockIndex;

        /// <summary>
        /// 数据位置
        /// </summary>
        protected long m_lPos;
        /// <summary>
        /// 每通道的数据量
        /// </summary>
        protected int m_nPerChnCount;

        /// <summary>
        /// 总的字节数
        /// </summary>
        public int m_nTotalBytesCount;
        /// <summary>
        /// 数据
        /// </summary>
        public byte[] m_SampleData;
        /// <summary>
        /// 无数据组ID
        /// </summary>
        public List<int> m_lstNoDataGroupID;
        /// <summary>
        /// 事后重新计算使用
        /// </summary>
        public List<Group> m_TimeGroup;
        /// <summary>
        /// 数据索引
        /// </summary>
        public Dictionary<int, int> m_dicBufferIndex;

        /// <summary>
        /// 是否停止采集
        /// </summary>
        protected bool m_bStopSample;

        public bool IsStopSample
        {
            get { return m_bStopSample; }
            set { m_bStopSample = value; }
        }

        protected bool m_bSetSaveCount = false;
        protected int m_nSaveChnCount = 0;

        /// <summary>
        /// 存储的数据
        /// </summary>
        public IntPtr m_PtrData;

        public SampleData()
        {
            m_PtrData = IntPtr.Zero;
            m_lstNoDataGroupID = new List<int>();
            m_strMachineIP = "";
        }

        public SampleData Clone()
        {
            SampleData data = (SampleData)this.MemberwiseClone();

            return data;
        }

        public long StartPos
        {
            set { m_lPos = value; }
            get { return m_lPos; }
        }

        /// <summary>
        /// 设置每通道存储的数据量
        /// </summary>
        public int PerChnCount
        {
            set { m_nPerChnCount = value; }
            get { return m_nPerChnCount; }
        }

        public long GetStartPos(int nOffset = 0)
        {
            return m_lPos + nOffset;
        }

        /// <summary>
        /// 获取通道数据量
        /// </summary>
        /// <param name="nOffset"></param>
        /// <returns></returns>
        public int GetPerChnCount(int nOffset = 0)
        {
            return m_nPerChnCount - nOffset;
        }

        public void SetSaveChnCount(int nSaveCount)
        {
            m_nSaveChnCount = nSaveCount;
            m_bSetSaveCount = true;
        }

        /// <summary>
        /// 获取需要存储的数据量
        /// </summary>
        /// <returns></returns>
        public int SaveChnCount
        {
            get
            {
                if (m_bSetSaveCount)
                {
                    if (m_nPerChnCount < m_nSaveChnCount || m_nSaveChnCount < 0)
                        return m_nPerChnCount;

                    return m_nSaveChnCount;
                }

                return m_nPerChnCount;
            }
        }
    }
}
