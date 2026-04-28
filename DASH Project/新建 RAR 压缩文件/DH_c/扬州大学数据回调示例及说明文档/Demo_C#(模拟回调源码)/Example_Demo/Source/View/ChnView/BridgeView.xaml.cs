using DHHandle;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// BridgeView.xaml 的交互逻辑
    /// </summary>
    public partial class BridgeView : BaseView
    {
        public BridgeView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_SENSOR_BT;
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
            foreach (var item in GetListHardChannel())
            {
                WPFBridgeChannel chn = new WPFBridgeChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbBridgeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFBridgeChannel chn = cmb.DataContext as WPFBridgeChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.BridgeMode == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_BRIDGE_MODE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void cmbBridgeVoltage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFBridgeChannel chn = cmb.DataContext as WPFBridgeChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.BridgeVoltage == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }
    }
}
