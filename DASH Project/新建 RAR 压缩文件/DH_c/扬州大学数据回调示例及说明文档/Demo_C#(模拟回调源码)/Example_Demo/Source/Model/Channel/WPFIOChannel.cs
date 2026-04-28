using DHHandle;

namespace Example_Demo
{
    /// <summary>
    /// IO通道
    /// </summary>
    class WPFIOChannel : WPFChannel
    {
        public int Data
        {
            get { return m_IOChannel.Data; }
        }

        public string IOStatus
        {
            get { return m_IOChannel.IOStatus == IOChannel.IO_INPUT ? "Input" : "Output"; }
        }

        internal IOChannel m_IOChannel;

        public void SetParam(MachineMonitor _IHardWareCtrl, HardChannel hard, IOChannel iOChannel)
        {
            #region 基本信息
            m_HardWare = _IHardWareCtrl;
            m_HardChannel = hard;
            m_IOChannel = iOChannel;

            ID = ChannelName = string.Format("{0}{1}-{2}({3})", m_IOChannel.IOStatus == IOChannel.IO_OUTPUT ? "DO" : "DI", hard.m_nDeviceID + 1, hard.m_nChannelID + 1, m_IOChannel.Index + 1);
            #endregion
        }

        public int SignalIndex
        {
            get
            {
                return m_IOChannel.Index;
            }
        }

        public void FireDataChanged()
        {
            OnPropertyChanged("Data");
        }
    }
}
