using DHHandle;
using System.Collections.Generic;

namespace Example_Demo
{
    /// <summary>
    /// BridgeView.xaml 的交互逻辑
    /// </summary>
    public partial class OtherView : BaseView
    {
        public OtherView()
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
            foreach (var item in m_AllHardChannel.FindAll(x => !HaveViewMeasureTypes.Contains(x.m_nMeasureType)))
            {
                WPFChannel chn = new WPFChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }
    }
}
