using DHHandle;
using System.Collections.Generic;


namespace Example_Demo
{
    /// <summary>
    /// 应变应力
    /// </summary>
    class WPFStrainChannel : WPFChannel
    {
        string _BridgeType;
        /// <summary>
        /// 桥路方式
        /// </summary>
        public string BridgeType
        {
            get { return _BridgeType; }
            set
            {
                if (_BridgeType != value)
                {
                    _BridgeType = value;
                    OnPropertyChanged("BridgeType");
                }
            }
        }

        List<string> _BridgeTypeList;
        /// <summary>
        /// 桥路方式列表
        /// </summary>
        public List<string> BridgeTypeList
        {
            get { return _BridgeTypeList; }
            set
            {
                if (_BridgeTypeList != value)
                {
                    _BridgeTypeList = value;
                    OnPropertyChanged("BridgeTypeList");
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
        /// 桥压列表
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

        string _StrainGauge;
        /// <summary>
        /// 应变计阻力值
        /// </summary>
        public string StrainGauge
        {
            get { return _StrainGauge; }
            set
            {
                if (_StrainGauge != value)
                {
                    _StrainGauge = value;
                    OnPropertyChanged("StrainGauge");
                }
            }
        }

        string _StrainLead;
        /// <summary>
        /// 导线电阻
        /// </summary>
        public string StrainLead
        {
            get { return _StrainLead; }
            set
            {
                if (_StrainLead != value)
                {
                    _StrainLead = value;
                    OnPropertyChanged("StrainLead");
                }
            }
        }

        string _StrainSenseCoef;
        /// <summary>
        /// 灵敏度系数
        /// </summary>
        public string StrainSenseCoef
        {
            get { return _StrainSenseCoef; }
            set
            {
                if (_StrainSenseCoef != value)
                {
                    _StrainSenseCoef = value;
                    OnPropertyChanged("StrainSenseCoef");
                }
            }
        }

        string _StrainPosion;
        /// <summary>
        /// 泊松比
        /// </summary>
        public string StrainPosion
        {
            get { return _StrainPosion; }
            set
            {
                if (_StrainPosion != value)
                {
                    _StrainPosion = value;
                    OnPropertyChanged("StrainPosion");
                }
            }
        }

        string _StrainElasticity;
        /// <summary>
        /// 弹性模量
        /// </summary>
        public string StrainElasticity
        {
            get { return _StrainElasticity; }
            set
            {
                if (_StrainElasticity != value)
                {
                    _StrainElasticity = value;
                    OnPropertyChanged("StrainElasticity");
                }
            }
        }

        public override void RefreshParam()
        {
            base.RefreshParam();

            //应变应力类型
            Meterage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_SHOWTYPE);
            MeterageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_STRAIN_SHOWTYPE);

            //桥路方式
            BridgeType = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGETYPE);
            BridgeTypeList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGETYPE);

            //桥压
            BridgeVoltage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE);
            BridgeVoltageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE);

            //应变计阻力值
            StrainGauge = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_GAUGE);
            //导线电阻
            StrainLead = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_LEAD);
            //灵敏度系数
            StrainSenseCoef = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_SENSECOEF);
            //泊松比
            StrainPosion = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_POSION);
            //弹性模量
            StrainElasticity = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_STRAIN_ELASTICITY);
        }

    }
}
