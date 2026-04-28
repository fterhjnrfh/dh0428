using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml;

namespace DHHandle
{
    public class Channel_Key
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string m_strIP = "";
        public int m_nChannelGroupID;
        public int m_nChannelStyle;
        public int m_nChannelID;
        public int m_nCellID;

        public static string ConvertToXML(List<HardChannel> lstChnKey)
        {
            if (lstChnKey == null)
                return "";

            XmlDocument _Doc = new XmlDocument();
            XmlElement Root = _Doc.CreateElement("Root");
            _Doc.AppendChild(Root);
            for (int i = 0; i < lstChnKey.Count; i++)
            {
                XmlElement node = Root.OwnerDocument.CreateElement("ChannelKey");
                Save(lstChnKey[i], node);
                Root.AppendChild(node);
            }
            return Root.OuterXml;
        }

        public static void Save(HardChannel hardChannel, XmlElement node)
        {
            AddChild(node, "GroupChannelID", hardChannel.m_nDeviceID);
            AddChild(node, "ChannelStyle", hardChannel.m_nChannelStyle);
            AddChild(node, "ChannelID", hardChannel.m_nChannelID);
            AddChild(node, "MachineIP", hardChannel.m_strDeviceIP);
        }

        static void AddChild(XmlElement node, string name, object value)
        {
            XmlElement xe = node.OwnerDocument.CreateElement(name);
            xe.InnerText = value == null ? "" : value.ToString();
            node.AppendChild(xe);
        }
    }

    public class Device
    {
        public string m_strDeviceIP;

        public int m_nDeviceID;

        public int m_nInterfaceType;

        public List<HardChannel> m_lstHardChannel;

        /// <summary>
        /// 是否存在计数器通道
        /// </summary>
        public bool m_bExistPulseChn;
        /// <summary>
        /// 是否外部数据源通道
        /// </summary>
        public bool m_bOutDataDevice;

        public int DeviceID { get { return m_nDeviceID; } }

        public string DeviceInfo { get { return string.Format("{0}号机({1})", m_nDeviceID + 1, m_strDeviceIP); } }
        public bool ReadData { get; set; }
        public List<float> SampleFreqs { get; set; }
        public float CurrentSamplefreq { get; set; }

        public long m_lReceiveCount;

        public Device(int deviceId, string strIP)
        {
            m_nDeviceID = deviceId;
            m_strDeviceIP = strIP;
            m_lstHardChannel = new List<HardChannel>();
            m_bOutDataDevice = false;
            ReadData = true;
        }

        public void PrepareSample()
        {
            m_lReceiveCount = 0;
            m_bExistPulseChn = m_lstHardChannel.Exists(F => F.IsPulseChannel);
        }
    }

    public class HardChannel
    {
        const int BufferSize = 100;
        public string m_strDeviceIP;

        public int m_nDeviceID;

        public bool m_bExistControl;

        public int m_nChannelID;

        public int m_nControlID;

        public int m_nCollectID;

        public int m_nChannelStyle;

        public string m_strChannelName { get; set; }

        public string m_strChannelDetail;

        /// <summary>
        /// //在线标志，由硬件检测，用户不可设置
        /// </summary>
        public int m_bOnlineFlag;

        public string m_strMeasureType;
        public int m_nMeasureType;

        public int m_nMeterageType;

        public string m_strUnit;

        public float m_fltSampleFreq;
        /// <summary>
        /// 计数器测量类型
        /// </summary>
        public string m_strCounterSensor;

        public int m_nSplitCount;

        public int m_nDataIndex;

        //仪器分组id
        public int m_nTeamId;
        //仪器分组index
        public int m_nTeamIndex;
        /// <summary>
        /// 量程
        /// </summary>
        public string m_strFullValue;
        public List<string> m_lstFullValue;
        /// <summary>
        /// 桥压
        /// </summary>
        public string m_strBridgeVoltage;
        public List<string> m_lstBridgeVoltage;

        public List<IOChannel> IOChannels = new List<IOChannel>();

        public event EventHandler<DataChangedEventArgs> EventDataChanged;

        public HardChannel(int DeviceID, string strIP, int nMacChnId, int bOnLine)
        {
            m_nDeviceID = DeviceID;
            m_strDeviceIP = strIP;
            m_nChannelID = nMacChnId;
            m_bOnlineFlag = bOnLine;
            m_strChannelName = string.Format("AI{0}-{1}", DeviceID + 1, nMacChnId + 1);
        }

        public void ChangeChannelName(string paramname)
        {
            if (string.IsNullOrEmpty(paramname))
                return;
            m_strChannelName = paramname;
        }

        public void ResetDataIndex(GetDataTypeEnum datatype)
        {
            switch (datatype)
            {
                case GetDataTypeEnum.SingleMachine:
                    m_nDataIndex = HardWare_StandardC.GetOneMacDataIndex(m_nDeviceID, m_nChannelID);
                    break;
                case GetDataTypeEnum.MultiMachine:
                    m_nDataIndex = HardWare_StandardC.GetAllMacDataIndex(m_nDeviceID, m_nChannelID);
                    break;
                case GetDataTypeEnum.TeamMachine:
                    break;
                default:
                    break;
            }
        }

        public bool IsSameHardChannel(HardChannel chn)
        {
            bool bSame = false;
            if (m_strDeviceIP == chn.m_strDeviceIP && m_nDeviceID == chn.m_nDeviceID && m_nChannelID == chn.m_nChannelID && m_nChannelStyle == chn.m_nChannelStyle)
            {
                bSame = true;
            }
            return bSame;
        }

        public string GetHardChannelID()
        {
            string strText = "";
            strText = ((!m_bExistControl) ? (strText + (m_nDeviceID + 1) + "-") : ((m_nControlID + 1).ToString("00") + "-" + (m_nCollectID + 1).ToString("00") + "-"));
            if (m_nChannelID + 1 <= 9)
            {
                strText += "0";
            }
            return strText + (m_nChannelID + 1);
        }

        /// <summary>
        /// 通过GetOneMacChnData_New或GetAllMacChnData接口拿到数据的通道
        /// </summary>
        /// <returns></returns>
        public bool IsTimeChn()
        {
            if (IsStaticStatChn || IsPulseChannel || IsIOChannel || m_bOnlineFlag == 0)
                return false;

            return true;
        }

        /// <summary>
        /// 通过GetRS485ChnData_New接口拿到数据的通道
        /// </summary>
        /// <returns></returns>
        public bool IsStaticStatChn
        {
            get
            {
                return IsRS422Chn() || IsVibStrainChn;
            }
        }

        /// <summary>
        /// RS422通道
        /// </summary>
        /// <returns></returns>
        bool IsRS422Chn()
        {
            bool b422Chn = false;
            switch (m_nMeasureType)
            {
                case (int)MEASURE_TYPE.MEASURE_TYPE_TEMP_597XW:
                case (int)MEASURE_TYPE.MEASURE_TYPE_RS422:
                case (int)MEASURE_TYPE.MEASURE_TYPE_DOUBLE_RS422:
                case (int)MEASURE_TYPE.MEASURE_TYPE_PWM:
                case (int)MEASURE_TYPE.MEASURE_TYPE_RS485_COMMON_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_TEMP_JUFU_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_TEMP_SANMU_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_POWER_ADG_1000_51_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_POWER_CHROMA_62000H_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_1540HPROS_COOL_WATER_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_SALT_FOG_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_TEMP_SHANGJIAODA_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_POWER_PBZ40_10_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_15PROS_COOL_WATER_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_TEMP_U8555P_CONTROL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_DOUBLE_RS422_INERTIAL:
                case (int)MEASURE_TYPE.MEASURE_TYPE_DOUBLE_RS422_WIND:
                case (int)MEASURE_TYPE.MEASURE_TYPE_5859A:
                case (int)MEASURE_TYPE.MEASURE_TYPE_RS422_FBG:
                case (int)MEASURE_TYPE.MEASURE_TYPE_RS422_EX:
                    {
                        b422Chn = true;
                    }
                    break;
            }

            return b422Chn;
        }

        /// <summary>
        /// 振弦通道
        /// </summary>
        /// <returns></returns>
        public bool IsVibStrainChn
        {
            get
            {
                return m_nMeasureType == (int)MEASURE_TYPE.MEASURE_TYPE_VIB_WIRE || m_nMeasureType == (int)MEASURE_TYPE.MEASURE_TYPE_VIB_WIRE_PRESS;
            }
        }

        /// <summary>
        /// 通过GetOneMacPluseData接口拿到数据的通道
        /// </summary>
        /// <returns></returns>
        public bool IsPulseChannel
        {
            get
            {
                return m_nMeasureType == (int)MEASURE_TYPE.MEASURE_PULSE_COUNTER && m_strCounterSensor == "编码器";
            }
        }

        public bool IsIOChannel
        {
            get
            {
                return m_nMeasureType == (int)MEASURE_TYPE.MEASURE_TYPE_IO;
            }
        }

        public bool IsCANChannel
        {
            get
            {
                return m_nMeasureType == (int)MEASURE_TYPE.MEASURE_TYPE_CAN;
            }
        }

        public void GetIOStatus()
        {
            IntPtr objptr = Marshal.AllocHGlobal(BufferSize);
            try
            {
                int nReturnSize;
                int nIOChnCount;
                if (HardWare_StandardC.DA_GetIOChnStatus(m_nDeviceID, m_nChannelID, BufferSize, objptr, out nReturnSize, out nIOChnCount) == 0)
                    return;

                string sText = Marshal.PtrToStringAnsi(objptr, nReturnSize - 1);
                string[] strings = sText.Split(',');
                if (nIOChnCount != strings.Length)
                    return;

                for (int i = 0; i < nIOChnCount; i++)
                {
                    IOChannel iOChannel = new IOChannel();
                    iOChannel.Index = i;
                    iOChannel.IOStatus = int.Parse(strings[i]);
                    IOChannels.Add(iOChannel);
                }
            }
            catch (Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            finally
            {
                Marshal.FreeHGlobal(objptr);
            }
        }

        public byte[] IOData()
        {
            List<byte> bytes = new List<byte>();
            foreach (var item in IOChannels)
            {
                if (item.IOStatus == IOChannel.IO_OUTPUT)
                {
                    bytes.Add((byte)item.Data);
                }
            }
            return bytes.ToArray();
        }

        public void FireDataEvent(DataChangedEventArgs args)
        {
            try
            {
                EventDataChanged?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                Utility.WriteLog(ex.ToString());
            }
        }
    }

    public class IOChannel
    {
        public const int IO_OUTPUT = 0;
        public const int IO_INPUT = 1;

        public int Index;
        /// <summary>
        /// DO/DI状态 0：DO 1：DI
        /// </summary>
        public int IOStatus;

        public int Data;
    }
}
