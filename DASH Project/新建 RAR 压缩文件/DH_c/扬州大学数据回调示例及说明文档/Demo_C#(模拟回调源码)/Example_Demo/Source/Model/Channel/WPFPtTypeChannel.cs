using DHHandle;

namespace Example_Demo
{
    /// <summary>
    /// 铂电阻测温
    /// </summary>
    class WPFPtTypeChannel : WPFChannel
    {

        public override void RefreshParam()
        {
            base.RefreshParam();

            Meterage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_PT_TYPE);
            MeterageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_PT_TYPE);
        }

    }
}
