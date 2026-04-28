using DHHandle;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// ShowDataView.xaml 的交互逻辑
    /// </summary>
    public partial class ShowDataView : UserControl
    {
        ShowDataModel m_ShowDataModel;

        public ShowDataView()
        {
            InitializeComponent();
        }

        public void SetParam(MachineMonitor _machineMonitor)
        {
            m_ShowDataModel?.Dispose();
            m_ShowDataModel = new ShowDataModel(_machineMonitor);
            this.DataContext = m_ShowDataModel;
        }

        private void btnClear_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_ShowDataModel.Data = "";
            m_ShowDataModel.StatData = "";
        }

        private void btnStart_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_ShowDataModel.ShowData = true;
        }

        private void btnStop_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_ShowDataModel.ShowData = false;
        }

        private void btnGetGPSData_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_ShowDataModel.GetGpsData();
        }
    }
}
