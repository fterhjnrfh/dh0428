using DHHandle;
using System.Collections.Generic;

namespace Example_Demo
{
    /// <summary>
    /// 桥式传感器
    /// </summary>
    class WPFBridgeChannel : WPFChannel
    {
        string _BridgeMode;
        /// <summary>
        /// 供桥
        /// </summary>
        public string BridgeMode
        {
            get { return _BridgeMode; }
            set
            {
                if (_BridgeMode != value)
                {
                    _BridgeMode = value;
                    OnPropertyChanged("BridgeMode");
                }
            }
        }

        List<string> _BridgeModeList;
        /// <summary>
        /// 供桥
        /// </summary>
        public List<string> BridgeModeList
        {
            get { return _BridgeModeList; }
            set
            {
                if (_BridgeModeList != value)
                {
                    _BridgeModeList = value;
                    OnPropertyChanged("BridgeModeList");
                }
            }
        }

        string _BridgeVoltage;
        /// <summary>
        /// 桥压
        /// </summary>
        public string BridgeVoltage
        {
            get { return _BridgeVoltage; }
            set
            {
                if (_BridgeVoltage != value)
                {
                    _BridgeVoltage = value;
                    OnPropertyChanged("BridgeVoltage");
                }
            }
        }

        List<string> _BridgeVoltageList;
        /// <summary>
        /// 供桥
        /// </summary>
        public List<string> BridgeVoltageList
        {
            get { return _BridgeVoltageList; }
            set
            {
                if (_BridgeVoltageList != value)
                {
                    _BridgeVoltageList = value;
                    OnPropertyChanged("BridgeVoltageList");
                }
            }
        }

        public override void RefreshParam()
        {
            base.RefreshParam();

            BridgeMode = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_BRIDGE_MODE);
            BridgeModeList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_BRIDGE_MODE);

            BridgeVoltage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE);
            BridgeVoltageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE);
        }

    }
}
