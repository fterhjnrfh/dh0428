using DHHandle;
using System.Collections.Generic;

namespace Example_Demo
{
    /// <summary>
    /// OutDataView.xaml 的交互逻辑
    /// </summary>
    public partial class OutDataView : BaseView
    {
        public OutDataView()
        {
            InitializeComponent();
            datagrid.ItemsSource = ShowChannels;
        }

        public override void InitUI(MachineMonitor hardWare, List<HardChannel> childHardChannel)
        {
            base.InitUI(hardWare, childHardChannel);

            RefreshChannel();
        }

        public override void RefreshChannel()
        {
            ShowChannels.Clear();

            foreach (var item in m_HardWare.Devices)
            {
                if (item.m_bOutDataDevice)
                {
                    foreach (var channel in item.m_lstHardChannel)
                    {
                        WPFChannel chn = new WPFChannel();
                        chn.SetParam(m_HardWare, channel);
                        ShowChannels.Add(chn);
                    }
                }
            }
        }
    }
}
