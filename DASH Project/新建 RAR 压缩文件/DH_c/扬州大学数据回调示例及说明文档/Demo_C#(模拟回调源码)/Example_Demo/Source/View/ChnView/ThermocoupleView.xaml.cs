using DHHandle;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Example_Demo
{
    /// <summary>
    /// ThermocoupleView.xaml 的交互逻辑
    /// </summary>
    public partial class ThermocoupleView : BaseView
    {
        public ThermocoupleView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_TEMPERATURE_THERMO;
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
                WPFThermocoupleChannel chn = new WPFThermocoupleChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbMeterage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFThermocoupleChannel chn = cmb.DataContext as WPFThermocoupleChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.Meterage == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_THERMO_TYPE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void CoolTemperature_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CoolTemperature_LostFocus(sender, null);
            }
        }

        private void CoolTemperature_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFThermocoupleChannel chn = tb.DataContext as WPFThermocoupleChannel;
            if (chn == null)
                return;

            if (chn.CoolTemperature == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_THERMO_COOLTEMPERATURE, tb.Text);
            chn.RefreshParam();
        }
    }
}
