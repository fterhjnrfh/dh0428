using DHHandle;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// PtTypeView.xaml 的交互逻辑
    /// </summary>
    public partial class PtTypeView : BaseView
    {
        public PtTypeView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_TEMPERATURE_PT;
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
                WPFPtTypeChannel chn = new WPFPtTypeChannel();
                chn.SetParam(m_HardWare, item);
                ShowChannels.Add(chn);
            }
        }

        private void cmbMeterage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFPtTypeChannel chn = cmb.DataContext as WPFPtTypeChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.Meterage == cmb.SelectedItem.ToString())
            {
                return;
            }

            chn.ModifyParameter(m_HardWare, ParamShowDefine.SHOW_PT_TYPE, cmb.SelectedItem.ToString());
        }
    }
}
