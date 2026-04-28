using DHHandle;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Example_Demo
{
    /// <summary>
    /// StrainView.xaml 的交互逻辑
    /// </summary>
    public partial class StrainView : BaseView
    {
        public StrainView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_STRAINMETER;
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
                WPFStrainChannel chn = new WPFStrainChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbMeterage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFStrainChannel chn = cmb.DataContext as WPFStrainChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.Meterage == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_SHOWTYPE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void cmbBridgeType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFStrainChannel chn = cmb.DataContext as WPFStrainChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.BridgeType == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGETYPE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }

        private void StrainGauge_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StrainGauge_LostFocus(sender, null);
            }
        }

        private void StrainGauge_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFStrainChannel chn = tb.DataContext as WPFStrainChannel;
            if (chn == null)
                return;

            if (chn.StrainGauge == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_GAUGE, tb.Text);
            chn.RefreshParam();
        }

        private void StrainLead_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StrainLead_LostFocus(sender, null);
            }
        }

        private void StrainLead_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFStrainChannel chn = tb.DataContext as WPFStrainChannel;
            if (chn == null)
                return;

            if (chn.StrainLead == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_LEAD, tb.Text);
            chn.RefreshParam();
        }

        private void StrainSenseCoef_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StrainSenseCoef_LostFocus(sender, null);
            }
        }

        private void StrainSenseCoef_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFStrainChannel chn = tb.DataContext as WPFStrainChannel;
            if (chn == null)
                return;

            if (chn.StrainSenseCoef == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_SENSECOEF, tb.Text);
            chn.RefreshParam();
        }

        private void StrainPosion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StrainPosion_LostFocus(sender, null);
            }
        }

        private void StrainPosion_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFStrainChannel chn = tb.DataContext as WPFStrainChannel;
            if (chn == null)
                return;

            if (chn.StrainPosion == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_POSION, tb.Text);
            chn.RefreshParam();
        }

        private void StrainElasticity_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StrainElasticity_LostFocus(sender, null);
            }
        }

        private void StrainElasticity_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            WPFStrainChannel chn = tb.DataContext as WPFStrainChannel;
            if (chn == null)
                return;

            if (chn.StrainElasticity == tb.Text)
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_ELASTICITY, tb.Text);
            chn.RefreshParam();
        }

        private void cmbBridgeVoltage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFStrainChannel chn = cmb.DataContext as WPFStrainChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.BridgeType == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_STRAIN_BRIDGEVOLTAGE, cmb.SelectedItem.ToString());
            chn.RefreshParam();
        }
    }
}
