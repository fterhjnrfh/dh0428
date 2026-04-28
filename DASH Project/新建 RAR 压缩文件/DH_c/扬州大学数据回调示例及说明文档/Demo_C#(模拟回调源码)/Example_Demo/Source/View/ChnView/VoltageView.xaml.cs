using DHHandle;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Example_Demo
{
    /// <summary>
    /// VoltageView.xaml 的交互逻辑
    /// </summary>
    public partial class VoltageView : BaseView
    {
        public VoltageView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_INTERNAL_DA;
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
                WPFVoltageChannel chn = new WPFVoltageChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbVoltFullValue_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFVoltageChannel chn = cmb.DataContext as WPFVoltageChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.VoltFullValue == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_VOLT_FULLVALUE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void TransFactor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TransFactor_LostFocus(sender, null);
            }
        }

        private void TransFactor_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFVoltageChannel chn = tb.DataContext as WPFVoltageChannel;
            if (chn == null)
                return;

            if (chn.TransFactor == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SENSECOEF, tb.Text);
            chn.RefreshParam();
        }
    }
}
