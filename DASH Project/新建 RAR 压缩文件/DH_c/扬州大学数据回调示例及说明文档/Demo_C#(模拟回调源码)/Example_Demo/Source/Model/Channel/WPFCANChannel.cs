using DHHandle;
using System.Collections.Generic;

namespace Example_Demo
{
    /// <summary>
    /// CAN
    /// </summary>
    class WPFCANChannel : WPFChannel
    {
        string _Baudrate = "";
        /// <summary>
        /// 波特率
        /// </summary>
        public string Baudrate
        {
            get { return _Baudrate; }
            set
            {
                if (_Baudrate != value)
                {
                    _Baudrate = value;
                    OnPropertyChanged("Baudrate");
                }
            }
        }

        List<string> _BaudrateList;
        /// <summary>
        /// 波特率
        /// </summary>
        public List<string> BaudrateList
        {
            get { return _BaudrateList; }
            set
            {
                if (_BaudrateList != value)
                {
                    _BaudrateList = value;
                    OnPropertyChanged("BaudrateList");
                }
            }
        }

        string _Baudrate2 = "";
        /// <summary>
        /// CANFD波特率
        /// </summary>
        public string Baudrate2
        {
            get { return _Baudrate2; }
            set
            {
                if (_Baudrate2 != value)
                {
                    _Baudrate2 = value;
                    OnPropertyChanged("Baudrate2");
                }
            }
        }

        List<string> _Baudrate2List;
        /// <summary>
        /// CANFD波特率
        /// </summary>
        public List<string> Baudrate2List
        {
            get { return _Baudrate2List; }
            set
            {
                if (_Baudrate2List != value)
                {
                    _Baudrate2List = value;
                    OnPropertyChanged("Baudrate2List");
                }
            }
        }

        string _CanType = "";
        /// <summary>
        /// Can类型 0 是常规can，1是canFD
        /// </summary>
        public string CanType
        {
            get { return _CanType; }
            set
            {
                if (_CanType != value)
                {
                    _CanType = value;
                    OnPropertyChanged("CanType");
                }
            }
        }

        List<string> _CanTypeList;
        /// <summary>
        /// Can类型 0 是常规can，1是canFD
        /// </summary>
        public List<string> CanTypeList
        {
            get { return _CanTypeList; }
            set
            {
                if (_CanTypeList != value)
                {
                    _CanTypeList = value;
                    OnPropertyChanged("CanTypeList");
                }
            }
        }

        int _DataLen;
        /// <summary>
        /// CANFD数据长度
        /// </summary>
        public int DataLen
        {
            get { return _DataLen; }
            set
            {
                if (_DataLen != value)
                {
                    _DataLen = value;
                    OnPropertyChanged("DataLen");
                }
            }
        }

        public List<int> DataLenList
        {
            get
            {
                return new List<int>() { 8, 12, 16, 20, 24, 32, 48, 64 };
            }
        }

        bool _Brs;
        /// <summary>
        /// BRS
        /// </summary>
        public bool Brs
        {
            get { return _Brs; }
            set
            {
                if (_Brs != value)
                {
                    _Brs = value;
                    OnPropertyChanged("Brs");
                }
            }
        }

        public override void RefreshParam()
        {
            //测量类型
            MeasureType = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_MEASURETYPE);

            Baudrate = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CAN_BAUDRATE);
            BaudrateList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CAN_BAUDRATE);

            Baudrate2 = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CAN_BAUDRATE2);
            Baudrate2List = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CAN_BAUDRATE2);

            CanType = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CAN_TYPE);
            CanTypeList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CAN_TYPE);

            if (int.TryParse(m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CAN_DATA_LEN), out int datalen))
            {
                DataLen = datalen;
            }
            if (int.TryParse(m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CAN_BRS), out int _brs))
            {
                Brs = _brs == 1;
            }
        }
    }
}
