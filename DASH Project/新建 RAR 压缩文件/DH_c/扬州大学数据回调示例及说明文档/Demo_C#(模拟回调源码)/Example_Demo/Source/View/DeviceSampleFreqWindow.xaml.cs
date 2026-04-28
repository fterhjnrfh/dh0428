using DHHandle;
using System.Windows;
using System.Windows.Controls;

namespace Example_Demo.Source.View
{
    /// <summary>
    /// DeviceSampleFreqWindow.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceSampleFreqWindow : Window
    {
        MachineMonitor m_MachineMonitor;
        public DeviceSampleFreqWindow()
        {
            InitializeComponent();
        }

        public void SetParam(MachineMonitor machineMonitor)
        {
            m_MachineMonitor = machineMonitor;
            itemsDevice.ItemsSource = m_MachineMonitor.Devices;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Device device = (sender as ComboBox).DataContext as Device;
            if (device == null)
                return;

            m_MachineMonitor.SetMacSampleFreq(device);
        }
    }
}
