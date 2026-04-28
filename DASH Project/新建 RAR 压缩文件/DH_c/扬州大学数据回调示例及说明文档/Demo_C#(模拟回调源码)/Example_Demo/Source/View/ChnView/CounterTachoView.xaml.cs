using DHHandle;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// CounterTachoView.xaml 的交互逻辑
    /// </summary>
    public partial class CounterTachoView : BaseView
    {
        public CounterTachoView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_PULSE_COUNTER;
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
                WPFCounterTachoChannel chn = new WPFCounterTachoChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbMeterage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFCounterTachoChannel chn = cmb.DataContext as WPFCounterTachoChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.Meterage == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_COUNTER_SENSOR_TYPE_EX, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void btnCounterReset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_HardWare.m_bThread)
            {
                MessageBox.Show("请停止采样！");
                return;
            }

            WPFCounterTachoChannel chn = datagrid.SelectedItem as WPFCounterTachoChannel;
            if (chn == null)
            {
                MessageBox.Show("请选择计数器通道！");
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_PULSE_RESET_SET, "ON");
            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_PULSE_RESET_SET, "OFF");
            MessageBox.Show("复位成功");
        }
    }
}
