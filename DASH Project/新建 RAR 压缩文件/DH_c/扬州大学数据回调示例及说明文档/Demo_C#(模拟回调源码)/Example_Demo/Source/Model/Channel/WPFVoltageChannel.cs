using DHHandle;
using System.Collections.Generic;


namespace Example_Demo
{
    /// <summary>
    /// 电压测量
    /// </summary>
    class WPFVoltageChannel : WPFChannel
    {
        string transFactor;
        /// <summary>
        /// 传感器灵敏度
        /// </summary>
        public string TransFactor
        {
            get { return transFactor; }
            set
            {
                if (transFactor != value)
                {
                    transFactor = value;
                    OnPropertyChanged("TransFactor");
                }
            }
        }

        string _VoltFullValue;
        /// <summary>
        /// 电压量程范围
        /// </summary>
        public string VoltFullValue
        {
            get { return _VoltFullValue; }
            set
            {
                if (_VoltFullValue != value)
                {
                    _VoltFullValue = value;
                    OnPropertyChanged("VoltFullValue");
                }
            }
        }

        List<string> _VoltFullValueList;
        /// <summary>
        /// 电压量程范围
        /// </summary>
        public List<string> VoltFullValueList
        {
            get { return _VoltFullValueList; }
            set
            {
                if (_VoltFullValueList != value)
                {
                    _VoltFullValueList = value;
                    OnPropertyChanged("VoltFullValueList");
                }
            }
        }


        public override void RefreshParam()
        {
            base.RefreshParam();

            TransFactor = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SENSECOEF);
            VoltFullValue = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_VOLT_FULLVALUE);
            VoltFullValueList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_CHANNEL_VOLT_FULLVALUE);
        }

    }
}
