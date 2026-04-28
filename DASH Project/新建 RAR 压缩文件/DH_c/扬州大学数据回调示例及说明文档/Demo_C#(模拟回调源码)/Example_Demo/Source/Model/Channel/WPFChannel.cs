using DHHandle;
using System.Collections.Generic;
using System.ComponentModel;

namespace Example_Demo
{
    public partial class WPFChannel : INotifyPropertyChanged
    {
        public MachineMonitor m_HardWare { get; set; }
        public HardChannel m_HardChannel { get; set; }

        bool _isEnabled;
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }
        public string ID { get; set; }

        string _channelName;
        public string ChannelName
        {
            get { return _channelName; }
            set
            {
                if (_channelName != value)
                {
                    _channelName = value;
                    OnPropertyChanged("ChannelName");
                }
            }
        }

        #region 通用
        string _UseFlag;
        /// <summary>
        /// 通道开关
        /// </summary>
        public string UseFlag
        {
            get { return _UseFlag; }
            set
            {
                if (_UseFlag != value)
                {
                    _UseFlag = value;
                    OnPropertyChanged("UseFlag");
                }
            }
        }

        List<string> _UseFlagList;
        public List<string> UseFlagList
        {
            get { return _UseFlagList; }
            set
            {
                if (_UseFlagList != value)
                {
                    _UseFlagList = value;
                    OnPropertyChanged("UseFlagList");
                }
            }
        }

        string _fullRand;
        public string FullRand
        {
            get { return _fullRand; }
            set
            {
                if (_fullRand != value)
                {
                    _fullRand = value;
                    OnPropertyChanged("FullRand");
                }
            }
        }

        List<string> _fullRandList;
        public List<string> FullRandList
        {
            get { return _fullRandList; }
            set
            {
                if (_fullRandList != value)
                {
                    _fullRandList = value;
                    OnPropertyChanged("FullRandList");
                }
            }
        }

        string _inputMode;
        /// <summary>
        /// 输入方式DC等
        /// </summary>
        public string InputMode
        {
            get
            {
                return _inputMode;
            }
            set
            {
                if (_inputMode != value)
                {
                    _inputMode = value;
                    OnPropertyChanged("InputMode");
                }
            }
        }

        List<string> _InputModeList;
        public List<string> InputModeList
        {
            get
            {
                return _InputModeList;
            }
            set
            {
                if (_InputModeList != value)
                {
                    _InputModeList = value;
                    OnPropertyChanged("InputModeList");
                }
            }
        }

        string _upFreq;
        /// <summary>
        /// 上限制频率
        /// </summary>
        public string UpFreq
        {
            get { return _upFreq; }
            set
            {
                if (_upFreq != value)
                {
                    _upFreq = value;
                    OnPropertyChanged("UpFreq");
                    OnPropertyChanged("InputModeStr");
                }
            }
        }

        List<string> _upFreqList;
        /// <summary>
        /// 上限制频率列表
        /// </summary>
        public List<string> UpFreqList
        {
            get { return _upFreqList; }
            set
            {
                if (_upFreqList != value)
                {
                    _upFreqList = value;
                    OnPropertyChanged("UpFreqList");
                }
            }
        }

        /// <summary>
        /// 测量量
        /// </summary>
        string _meterage;
        public string Meterage
        {
            get { return _meterage; }
            set
            {
                if (_meterage != value)
                {
                    _meterage = value;
                    OnPropertyChanged("Meterage");
                }
            }
        }

        string _measureType;
        /// <summary>
        /// 字符串形式的测量类型
        /// </summary>
        public string MeasureType
        {
            get { return _measureType; }
            set
            {
                if (_measureType != value)
                {
                    _measureType = value;
                    OnPropertyChanged("MeasureType");
                }
            }
        }

        string _SampleRate;
        /// <summary>
        /// 字符串形式的采样频率
        /// </summary>
        public string SampleRate
        {
            get { return _SampleRate; }
            set
            {
                if (_SampleRate != value)
                {
                    _SampleRate = value;
                    OnPropertyChanged("SampleRate");
                }
            }
        }

        List<string> _measureTypeList;
        /// <summary>
        /// 测量类型列表
        /// </summary>
        public List<string> MeasureTypeList
        {
            get { return _measureTypeList; }
            set
            {
                _measureTypeList = value;
                OnPropertyChanged("MeasureTypeList");
            }
        }

        List<string> _meterageList;
        /// <summary>
        /// 测量量列表
        /// </summary>
        public List<string> MeterageList
        {
            get { return _meterageList; }
            set
            {
                _meterageList = value;
                OnPropertyChanged("MeterageList");
            }
        }

        List<string> _SampleRateList;
        /// <summary>
        /// 测量量列表
        /// </summary>
        public List<string> SampleRateList
        {
            get { return _SampleRateList; }
            set
            {
                _SampleRateList = value;
                OnPropertyChanged("SampleRateList");
            }
        }
        #endregion      

        public virtual void RefreshParam()
        {
            //开关
            UseFlag = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_USE);
            UseFlagList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_USE);
            m_HardChannel.m_bOnlineFlag = UseFlag == "ON" ? 1 : 0;

            //测量类型
            MeasureType = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_MEASURETYPE);
            MeasureTypeList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_MEASURETYPE);

            //满度量程
            FullRandList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_FULLVALUE);
            FullRand = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_FULLVALUE);

            //上限频率
            UpFreq = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_UPFREQ);
            UpFreqList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_UPFREQ);

            //输入方式
            InputMode = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_INPUTMODE);
            InputModeList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_INPUTMODE);

            //单通道采样频率
            SampleRate = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SAMPLE);
            SampleRateList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SAMPLE);
        }

        public void SetParam(MachineMonitor _IHardWareCtrl, HardChannel hard)
        {
            #region 基本信息
            m_HardWare = _IHardWareCtrl;
            m_HardChannel = hard;
            ID = ChannelName = hard.m_strChannelName;
            #endregion

            RefreshParam();
        }

        public void ModifyParameter(MachineMonitor m_HardWare, int paramShowID, string str)
        {
            m_HardWare.ModifyParamAndSendCode(m_HardChannel, paramShowID, str);
            RefreshParam();
        }

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }

        #endregion
    }

}
