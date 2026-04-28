using DHHandle;
using Example_Demo.Source.View;
using System;
using System.Windows;

namespace Example_Demo
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 说明：
    /// 可直接添加DHHandle类库到解决方案
    /// 启动的Exe程序所在文件夹需要包含"64位DLL和Config文件夹"
    /// -------------------------------------------------------------------------------------
    /// DHDAS软件，在DHDAS设置好所有参数，启动采样后，界面波形为所需波形后
    /// 设置参数简便方法1:可将"DHDAS软件目录\Config"文件夹和"COM控件\config"文件夹内serial文件 ，复制到运行程序目录，重新运行程序后，界面参数即为DHDAS软件所设置参数
    /// 设置参数简便方法2:在DHDAS【测量】-【参数管理】界面，点击【保存硬件参数】，保存文件并命名"AllGroupChannel.xml",放入"运行目录\Param"文件夹
    /// -------------------------------------------------------------------------------------
    /// 获取数据，参照RegisterDataEvent事件即可，
    /// 常规信号需要注册MultiChnEventDataChanged
    /// 振弦或者485需要注册EventStaticStatDataChanged
    /// </summary>
    public partial class MainWindow : Window
    {
        private MachineMonitor machineMonitor;

        public MainWindow()
        {
            InitializeComponent();
            InitMachineMonitor();
            base.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            machineMonitor.QuitControl();
        }

        private void InitMachineMonitor()
        {
            machineMonitor = new MachineMonitor();
            machineMonitor.Init();
            realTimeChartView.Initialize(machineMonitor);
            InitChnView();
        }

        private void btnStartSample_Click(object sender, RoutedEventArgs e)
        {
            GetDataTypeEnum type = GetDataTypeEnum.SingleMachine;
            if (rbMultiMachine.IsChecked.Value)
            {
                type = GetDataTypeEnum.MultiMachine;
            }
            else if (rbTeamMachine.IsChecked.Value)
            {
                type = GetDataTypeEnum.TeamMachine;
            }
            //由于IO通道数据显示在IOView上，因此启动采样前刷新下IO通道界面
            ioView.RefreshChannel();

            machineMonitor.StartSample(type, int.Parse(tbDataCountEveryTime.Text));
            SetButtonStatus(true);
        }

        private void btnStopSample_Click(object sender, RoutedEventArgs e)
        {
            machineMonitor.StopSample();
            SetButtonStatus(false);
        }

        public void SetButtonStatus(bool isSampling)
        {
            btnStartSample.IsEnabled = btnBalance.IsEnabled = btnClearZero.IsEnabled = cmbSampleFreq.IsEnabled = !isSampling;
            btnStopSample.IsEnabled = isSampling;
        }

        private void btnBalance_Click(object sender, RoutedEventArgs e)
        {
            machineMonitor.Balance();
        }

        private void btnClearZero_Click(object sender, RoutedEventArgs e)
        {
            machineMonitor.ClearZero();
        }

        private void btnRefreshChnView_Click(object sender, RoutedEventArgs e)
        {
            InitChnView();
        }

        private void btnReconnect_Click(object sender, RoutedEventArgs e)
        {
            machineMonitor.ReConnectAllMac();
            InitChnView();
        }

        private void cmbSampleFreq_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbSampleFreq == null)
                return;

            float samplefreq = float.Parse(cmbSampleFreq.SelectedItem.ToString());
            machineMonitor.SetSampleFreq(samplefreq);
            InitSampleFreq();
        }

        private bool IsSampling()
        {
            return machineMonitor.m_bThread;
        }

        #region 初始化通道界面&&波形界面
        void InitChnView()
        {
            InitSampleFreq();
            strainView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            voltageView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            bridgeView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            ptTypeView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            thermocoupleView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            counterTachoView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            ioView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            outdataView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            canView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            otherView.InitUI(machineMonitor, machineMonitor.AllTimeChannels);
            showDataView.SetParam(machineMonitor);
            realTimeChartView.Refresh();
        }

        void InitSampleFreq()
        {
            machineMonitor.InitSampleFreq();
            cmbSampleFreq.ItemsSource = machineMonitor.m_lstSampleFreq;
            cmbSampleFreq.SelectedItem = machineMonitor.m_CurSampleFreq;
        }

        #endregion

        /// <summary>
        /// 不同采样频率设置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnDiffSampleFreqSet_Click(object sender, RoutedEventArgs e)
        {
            DeviceSampleFreqWindow window = new DeviceSampleFreqWindow();
            window.SetParam(machineMonitor);
            window.ShowDialog();
        }

        /// <summary>
        /// 外部数据源设置
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnOutDataSet_Click(object sender, RoutedEventArgs e)
        {
            OutDataSetWindow window = new OutDataSetWindow();
            window.SetParam(machineMonitor);
            window.ShowDialog();
            machineMonitor.FindOutData();
            outdataView.RefreshChannel();
        }

        private void btnChangeIP_Click(object sender, RoutedEventArgs e)
        {
            machineMonitor.ChangeIP();
        }

        private void btnExportBalanceZero_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.SaveFileDialog() { Filter = "清零文件(*.xml)|*.xml" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var res = machineMonitor.GetAllChannelBalanceAndZeroValue(dlg.FileName);
                MessageBox.Show(res ? "导出成功!" : "导出失败!");
            }
        }

        private void btnImportBalanceZero_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.OpenFileDialog() { Filter = "清零文件(*.xml)|*.xml" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var res = machineMonitor.SetAllChannelBalanceAndZeroValue(dlg.FileName);
                MessageBox.Show(res ? "导入成功!" : "导入成功!");
            }
        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog folder = new System.Windows.Forms.FolderBrowserDialog();
            if (folder.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var res = machineMonitor.SaveMacParameter(folder.SelectedPath);
                MessageBox.Show(res ? "导出成功!" : "导出失败!");
            }
        }

        private void btnImport_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog folder = new System.Windows.Forms.FolderBrowserDialog();
            if (folder.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var res = machineMonitor.LoadMacParameter(folder.SelectedPath);
                if (res)
                    btnRefreshChnView_Click(null, null);
                MessageBox.Show(res ? "导入成功!" : "导入成功!");
            }
        }
    }
}
