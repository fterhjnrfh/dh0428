using System;
using System.Collections.Generic;
using System.Threading;

namespace DHHandle
{
    public partial class MachineMonitor
    {
        private List<Device> m_lstDevice = new List<Device>();

        /// <summary>
        /// 所有时域信号通道
        /// </summary>
        public List<HardChannel> AllTimeChannels
        {
            get
            {
                List<HardChannel> hardChannels = new List<HardChannel>();
                foreach (var item in m_lstDevice)
                {
                    if (item.m_bOutDataDevice)
                        continue;
                    hardChannels.AddRange(item.m_lstHardChannel);
                }
                return hardChannels;
            }
        }

        public List<Device> Devices
        {
            get
            {
                return m_lstDevice;
            }
        }

        public Dictionary<int, Device> m_DictDevice = new Dictionary<int, Device>();

        public bool m_bThread;
        private bool m_bFirst = true;
        Thread m_DataThread;

        Dictionary<HardChannel, float[]> m_DictChnData = new Dictionary<HardChannel, float[]>();
        /// <summary>
        /// 多个通道数据
        /// </summary>
        public event Action<Dictionary<HardChannel, float[]>, long> MultiChnEventDataChanged;
        /// <summary>
        /// RS485或者振弦通道
        /// </summary>
        public event Action<HardChannel, long[], int, float[]> EventStaticStatDataChanged;
        /// <summary>
        /// 计数器通道值
        /// </summary>
        public event Action<HardChannel, long[], PulsData[]> EventPulseDataChanged;
        /// <summary>
        /// IO通道
        /// </summary>
        public event Action<HardChannel> EventIODataChanged;
        /// <summary>
        /// CAN通道值
        /// </summary>
        public event Action<List<Tuple<HardChannel, long, Can_Data>>> EventCANDataChanged;

        public List<float> m_lstSampleFreq = new List<float>();
        public float m_CurSampleFreq;
        /// <summary>
        /// 是否单台仪器获取数据
        /// </summary>
        private GetDataTypeEnum m_GetDataType;

        /// <summary>
        /// key:仪器号；value：仪器下的所有teamid
        /// </summary>
        public List<Tuple<int, int>> m_DictTeamId = new List<Tuple<int, int>>();

        /// <summary>
        /// 所有通道
        /// </summary>
        public List<HardChannel> m_lstHardChannel = new List<HardChannel>();

        public void Init()
        {
            int res = HardWare_StandardC.InitMacControl(Utility.CfgDir + "\\");
            Utility.WriteLog("InitMacControl" + res);

            m_SampleDataHandler = new HardWare_StandardC.SampleDataChangeEventHandle(DealSampleData);
            HardWare_StandardC.SetDataChangeCallBackFun(m_SampleDataHandler);

            m_OnlineSensor = new Sensor();
            m_OnlineSensor.m_MainController = this;

            ReConnectAllMac();
        }

        public void ReConnectAllMac()
        {
            m_lstDevice.Clear();
            m_DictDevice.Clear();
            bool bFindMachine = HardWare_StandardC.RefindAndConnecMac();
            Utility.WriteLog("RefindAndConnecMac " + bFindMachine);
            if (!bFindMachine)
            {
                return;
            }

            LoadParamFile();
            FindMachine();
            FindOutData();
            InitSampleFreq();
        }

        /// <summary>
        /// 启动采样
        /// </summary>
        /// <param name="SingleGetData">是否单台仪器获取数据</param>
        public void StartSample(GetDataTypeEnum datatype, int nDataCount)
        {
            m_OnlineSensor.Start();

            m_GetDataType = datatype;
            m_bFirst = true;
            HardWare_StandardC.SetGetDataCountEveryTime(nDataCount);
            PrepareSample();
            HardWare_StandardC.StartMacSample();
            Utility.WriteLog("StartSample");

            SampleData.m_lCallBackPos = 0;
            m_DataThread = new Thread(GetDataThread);
            m_bThread = true;
            m_DataThread.IsBackground = true;
            m_DataThread.Start();
        }

        public void StopSample()
        {
            m_OnlineSensor.Stop();
            HardWare_StandardC.StopMacSample();
            Utility.WriteLog("StopSample");
            m_bThread = false;
            m_DataThread?.Interrupt();
        }

        public void QuitControl()
        {
            StopSample();
            m_lstDevice.Clear();
            HardWare_StandardC.QuitMacControl();
            Utility.WriteLog("QuitMacControl");
        }

        public void Balance()
        {
            HardWare_StandardC.AllChannelBalance();
        }

        public void ClearZero()
        {
            HardWare_StandardC.AllChannelClearZeroEx(0);
        }

        /// <summary>
        /// 单通道平衡清零
        /// </summary>
        /// <param name="hardChannels"></param>
        /// <param name="bGnd"></param>
        /// <returns></returns>
        public bool SingleChannelClearZero(List<HardChannel> hardChannels, bool bGnd)
        {
            string str = Channel_Key.ConvertToXML(m_lstHardChannel);
            return HardWare_StandardC.ChannelClearZeroEx(str.Length, str, bGnd ? 1 : 0) == 1;
        }

        public void PrepareSample()
        {
            switch (m_GetDataType)
            {
                case GetDataTypeEnum.SingleMachine:
                    HardWare_StandardC.ChangeGetDataStatus(true);
                    break;
                case GetDataTypeEnum.MultiMachine:
                    HardWare_StandardC.ChangeGetDataStatus(false);
                    break;
                case GetDataTypeEnum.TeamMachine:
                    break;
                default:
                    break;
            }
            foreach (Device device in m_lstDevice)
            {
                device.PrepareSample();
            }
        }

        public void ChangeIP()
        {
            HardWare_StandardC.SetUpInterface();
        }

        /// <summary>
        /// 导出平衡文件
        /// </summary>
        /// <param name="paramDir"></param>
        /// <returns></returns>
        public bool GetAllChannelBalanceAndZeroValue(string paramDir)
        {
            return HardWare_StandardC.GetAllChannelBalanceAndZeroValue(paramDir) == 1;
        }

        /// <summary>
        /// 导入平衡文件
        /// </summary>
        /// <param name="paramDir"></param>
        /// <returns></returns>
        public bool SetAllChannelBalanceAndZeroValue(string paramDir)
        {
            return HardWare_StandardC.SetAllChannelBalanceAndZeroValue(paramDir) == 1;
        }
    }
}
