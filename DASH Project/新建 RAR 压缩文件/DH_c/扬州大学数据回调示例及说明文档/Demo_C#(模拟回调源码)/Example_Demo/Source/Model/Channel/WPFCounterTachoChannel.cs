using DHHandle;

namespace Example_Demo
{
    /// <summary>
    /// 脉冲计数器测量
    /// </summary>
    class WPFCounterTachoChannel : WPFChannel
    {
        public override void RefreshParam()
        {
            Meterage = m_HardWare.GetMacChnCurrentParam(m_HardChannel, ParamShowDefine.SHOW_COUNTER_SENSOR_TYPE_EX);
            MeterageList = m_HardWare.GetParamSelectValue(m_HardChannel, ParamShowDefine.SHOW_COUNTER_SENSOR_TYPE_EX);
        }
    }
}
