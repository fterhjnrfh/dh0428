using System.Windows;
using Microsoft.Win32;

namespace AutoStartVirtualInstrument
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        MainWindowModel windowModel = new MainWindowModel();
        public MainWindow()
        {
            InitializeComponent();
            DataContext = windowModel;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            windowModel.StartExe();
        }

        private void Button_Config_Click(object sender, RoutedEventArgs e)
        {
            windowModel.Config();
        }

        private void Button_ConfigDevice_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            // 设置文件筛选器
            openFileDialog.Filter = "DeviceInfo (*.ini)|*.ini";
            // 设置对话框标题
            openFileDialog.Title = "选择DeviceInfo.ini";
            // 显示对话框
            if (openFileDialog.ShowDialog().Value)
            {
                windowModel.ConfigDevice(openFileDialog.FileName);
            }
        }
    }
}
