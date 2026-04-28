using DHHandle;
using System.Collections.ObjectModel;
using System.Windows;

namespace Example_Demo.Source.View
{
    /// <summary>
    /// OutDataSetWindow.xaml 的交互逻辑
    /// </summary>
    public partial class OutDataSetWindow : Window
    {
        MachineMonitor m_MachineMonitor;
        ObservableCollection<OutDataSourceInfo> SourceInfos = new ObservableCollection<OutDataSourceInfo>();

        public OutDataSetWindow()
        {
            InitializeComponent();
        }

        public void SetParam(MachineMonitor machineMonitor)
        {
            m_MachineMonitor = machineMonitor;
            itemsOutDataSource.ItemsSource = SourceInfos;
            InitOutdataInfo();
        }

        private void InitOutdataInfo()
        {
            SourceInfos.Clear();
            foreach (var item in m_MachineMonitor.GetOutDataSourceType())
            {
                OutDataSourceInfo outDataSourceInfo = new OutDataSourceInfo();
                outDataSourceInfo.OutDataType = item;
                int bUsed, port;
                string ip;
                if (m_MachineMonitor.GetOneTypeOutDataSourceStatus(item, out bUsed, out ip, out port))
                {
                    outDataSourceInfo.UseOutDataSource = bUsed == 1;
                    outDataSourceInfo.StrIP = ip;
                    outDataSourceInfo.PortID = port;
                }
                SourceInfos.Add(outDataSourceInfo);
            }
        }

        private void btnSetup_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in SourceInfos)
            {
                m_MachineMonitor.SetOneTypeOutDataSourceStatus(item.UseOutDataSource, item.OutDataType, item.StrIP + "," + item.PortID);
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class OutDataSourceInfo
    {
        public int OutDataType { get; set; }
        public bool UseOutDataSource { get; set; }
        public string StrIP { get; set; }
        public int PortID { get; set; }
    }
}
