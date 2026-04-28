using DHHandle;


namespace Example_Demo
{
    /// <summary>
    /// 热电偶测温
    /// </summary>
    class WPFThermocoupleChannel : WPFChannel
    {
        string _CoolTemperature;
        /// <summary>
        /// 冷锻温度
        /// </summary>
        public string CoolTemperature
        {
            get { return _CoolTemperature; }
            set
            {
                if (_CoolTemperature != value)
                {
                    _CoolTemperature = value;
                    OnPropertyChanged("CoolTemperature");
                }
            }
        }

        public override void RefreshParam()
        {
            base.RefreshParam();

            Meterage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_THERMO_TYPE);
            MeterageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_THERMO_TYPE);

            CoolTemperature = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_THERMO_COOLTEMPERATURE);
        }

    }
}
