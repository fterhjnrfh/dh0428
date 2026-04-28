using DHHandle;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// CANView.xaml 的交互逻辑
    /// </summary>
    public partial class CANView : BaseView
    {
        public CANView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_CAN;
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
                WPFCANChannel chn = new WPFCANChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbBaudrateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFCANChannel chn = cmb.DataContext as WPFCANChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_CAN_BAUDRATE, cmb.SelectedItem.ToString());
        }

        private void cmbBaudrate2List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFCANChannel chn = cmb.DataContext as WPFCANChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_CAN_BAUDRATE2, cmb.SelectedItem.ToString());
        }

        private void cmbCanType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFCANChannel chn = cmb.DataContext as WPFCANChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_CAN_TYPE, cmb.SelectedItem.ToString());
        }

        private void cmbDataLen_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFCANChannel chn = cmb.DataContext as WPFCANChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_CAN_DATA_LEN, cmb.SelectedItem.ToString());
        }

        private void CheckBox_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            CheckBox cmb = sender as CheckBox;
            WPFCANChannel chn = cmb.DataContext as WPFCANChannel;
            if (chn == null)
                return;

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_CAN_BRS, cmb.IsChecked.Value ? "1" : "0");
        }
    }
}
