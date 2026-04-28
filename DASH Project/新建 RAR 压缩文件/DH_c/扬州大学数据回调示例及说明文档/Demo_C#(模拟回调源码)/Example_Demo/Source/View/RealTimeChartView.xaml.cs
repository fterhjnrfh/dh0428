using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DHHandle;
using Example_Demo.Source.Model;
using LiveCharts;
using LiveCharts.Wpf;

namespace Example_Demo
{
    /// <summary>
    /// RealTimeChartView.xaml 的交互逻辑
    /// </summary>
    public partial class RealTimeChartView : UserControl
    {
        // 定时器，用于更新图表
        private DispatcherTimer _timer;

        // 是否需要更新图表
        private bool _chartNeedsUpdate = false;

        // 控件是否已完全初始化
        private bool _isInitialized = false;

        RealTimeChartViewModel RealTimeChartViewModel { get; set; }

        public RealTimeChartView()
        {
            try
            {
                InitializeComponent();

                RealTimeChartViewModel = new RealTimeChartViewModel();
                // 初始化属性
                InitChartProperties();

                // 添加图表缩放事件
                if (MainChart != null)
                {
                    MainChart.MouseWheel += MainChart_MouseWheel;
                }

                // 初始化定时器
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(10) // 10ms更新一次图表
                };
                _timer.Tick += Timer_Tick;

                // 创建图表更新定时器，降低更新频率
                var chartUpdateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100) // 100ms更新一次图表UI
                };
                chartUpdateTimer.Tick += ChartUpdateTimer_Tick;
                chartUpdateTimer.Start();

                // 控件卸载时释放资源
                this.Unloaded += RealTimeChartView_Unloaded;

                // 界面加载完成后再次确认数据上下文已正确设置
                this.Loaded += RealTimeChartView_Loaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RealTimeChartView初始化异常: {ex}");
                MessageBox.Show($"实时曲线视图初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化实时曲线
        /// </summary>
        public void Initialize(MachineMonitor machineMonitor)
        {
            try
            {
                RealTimeChartViewModel.Initialize(machineMonitor);
                DataContext = RealTimeChartViewModel;
                Debug.WriteLine("实时曲线已初始化");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化实时曲线异常: {ex}");
            }
        }

        public void Refresh()
        {
            RealTimeChartViewModel.RefreshChannel();
        }

        private void RealTimeChartView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                InitChartProperties();

                // 标记控件已完全初始化
                _isInitialized = true;

                Debug.WriteLine("RealTimeChartView已加载完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RealTimeChartView加载事件异常: {ex}");
            }
        }

        /// <summary>
        /// 初始化图表相关属性
        /// </summary>
        private void InitChartProperties()
        {
            try
            {
                // 初始化图表
                if (RealTimeChartViewModel.SeriesCollection == null)
                {
                    RealTimeChartViewModel.SeriesCollection = new SeriesCollection();

                    // 设置X轴标签
                    RealTimeChartViewModel.Labels = new string[RealTimeChartViewModel.MaxDataPoints];
                    for (int i = 0; i < RealTimeChartViewModel.Labels.Length; i++)
                    {
                        RealTimeChartViewModel.Labels[i] = i.ToString();
                    }

                    // 设置Y轴格式化
                    RealTimeChartViewModel.YFormatter = value => value.ToString("0.00");

                    // 禁用动画，提高实时数据显示性能
                    if (MainChart != null)
                    {
                        MainChart.DisableAnimations = true;

                        // 优化图表性能设置
                        OptimizeChartPerformance();
                    }
                }

                // 清空图表数据
                if (RealTimeChartViewModel.SeriesCollection.Count > 0 && RealTimeChartViewModel.SeriesCollection[0].Values != null)
                {
                    RealTimeChartViewModel.SeriesCollection[0].Values.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化图表属性异常: {ex}");
            }
        }

        /// <summary>
        /// 控件卸载时释放资源
        /// </summary>
        private void RealTimeChartView_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RealTimeChartView卸载异常: {ex}");
            }
        }

        /// <summary>
        /// 优化图表性能设置
        /// </summary>
        private void OptimizeChartPerformance()
        {
            try
            {
                // 禁用工具提示和图例
                MainChart.DataTooltip = null;

                // 设置硬件加速
                RenderOptions.SetBitmapScalingMode(MainChart, BitmapScalingMode.LowQuality);
                RenderOptions.SetEdgeMode(MainChart, EdgeMode.Aliased);

                // 减少抗锯齿计算
                MainChart.UseLayoutRounding = true;

                // 优化内存使用
                MainChart.AnimationsSpeed = TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"优化图表性能设置异常: {ex}");
            }
        }

        /// <summary>
        /// 图表更新定时器Tick事件
        /// </summary>
        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 只有在需要更新且处于自动滚动模式时才更新图表
                if (_chartNeedsUpdate && RealTimeChartViewModel.IsAutoScrolling && MainChart != null)
                {
                    MainChart.Update(false, false); // 轻量级更新，不重置轴
                    _chartNeedsUpdate = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图表更新定时器异常: {ex}");
            }
        }

        /// <summary>
        /// 图表鼠标滚轮事件处理
        /// </summary>
        private void MainChart_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // 如果用户正在使用滚轮缩放，自动取消"自动缩放"选项
                if (chkAutoScale != null && chkAutoScale.IsChecked == true)
                {
                    RealTimeChartViewModel.IsUserZooming = true;
                    chkAutoScale.IsChecked = false;
                    RealTimeChartViewModel.IsUserZooming = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"图表鼠标滚轮事件异常: {ex}");
            }
        }

        /// <summary>
        /// 创建新的Series对象
        /// </summary>
        private LineSeries CreateSeries(string title)
        {
            return new LineSeries
            {
                Title = title,
                Values = new ChartValues<double> { },
                PointGeometry = null,
                LineSmoothness = 0,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Colors.LightGreen)
            };
        }

        /// <summary>
        /// 开始监控按钮点击事件
        /// </summary>
        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        /// <summary>
        /// 停止监控按钮点击事件
        /// </summary>
        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }

        /// <summary>
        /// 通道选择变更事件
        /// </summary>
        private void cmbChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (RealTimeChartViewModel.SelectHardChannel != null)
                {
                    var _currentChannelId = RealTimeChartViewModel.SelectHardChannel;

                    // 更新图表标题 - 创建新的Series替换现有Series
                    if (RealTimeChartViewModel.SeriesCollection.Count > 0)
                    {
                        // 创建新的Series
                        var newSeries = CreateSeries($"通道{_currentChannelId.m_strChannelName}");

                        // 替换Series
                        RealTimeChartViewModel.SeriesCollection.Clear();
                        RealTimeChartViewModel.SeriesCollection.Add(newSeries);
                    }
                    else
                    {
                        // 如果没有Series则添加一个
                        RealTimeChartViewModel.SeriesCollection.Add(CreateSeries($"通道{_currentChannelId}"));
                    }

                    // 清空图表数据
                    if (RealTimeChartViewModel.SeriesCollection.Count > 0 && RealTimeChartViewModel.SeriesCollection[0].Values != null)
                    {
                        RealTimeChartViewModel.SeriesCollection[0].Values.Clear();
                    }

                    // 重置图表视图
                    if (chkAutoScale != null && chkAutoScale.IsChecked == true)
                    {
                        ResetChartView();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通道选择变更事件异常: {ex}");
            }
        }

        /// <summary>
        /// 自动缩放开启事件
        /// </summary>
        private void ChkAutoScale_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果不是用户缩放导致的状态变化
                if (!RealTimeChartViewModel.IsUserZooming)
                {
                    RealTimeChartViewModel.IsAutoScrolling = true;

                    // 重置图表视图
                    ResetChartView();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自动缩放开启事件异常: {ex}");
            }
        }

        /// <summary>
        /// 自动缩放关闭事件
        /// </summary>
        private void ChkAutoScale_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                RealTimeChartViewModel.IsAutoScrolling = false;

                // 允许X轴缩放
                if (MainChart != null)
                {
                    MainChart.Zoom = ZoomingOptions.X;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自动缩放关闭事件异常: {ex}");
            }
        }

        /// <summary>
        /// 重置图表视图
        /// </summary>
        private void ResetChartView()
        {
            try
            {
                if (MainChart == null) return;

                // 禁用缩放
                MainChart.Zoom = ZoomingOptions.None;

                // 重置轴
                if (MainChart.AxisX != null && MainChart.AxisX.Count > 0)
                {
                    MainChart.AxisX[0].MinValue = double.NaN;
                    MainChart.AxisX[0].MaxValue = double.NaN;
                }

                if (MainChart.AxisY != null && MainChart.AxisY.Count > 0)
                {
                    MainChart.AxisY[0].MinValue = double.NaN;
                    MainChart.AxisY[0].MaxValue = double.NaN;
                }

                // 刷新图表
                MainChart.Update(true, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重置图表视图异常: {ex}");
            }
        }

        /// <summary>
        /// 定时器Tick事件，更新图表
        /// </summary>
        private void Timer_Tick(object sender, EventArgs e)
        {
            UpdateChart();
        }

        /// <summary>
        /// 开始监控
        /// </summary>
        private void StartMonitoring()
        {
            try
            {
                RealTimeChartViewModel.StartMonitoring();
                // 确保SeriesCollection已初始化
                InitChartProperties();

                // 启动定时器
                _timer.Start();

                // 如果自动缩放被勾选，重置图表视图
                if (chkAutoScale != null && chkAutoScale.IsChecked == true)
                {
                    ResetChartView();
                }

                Debug.WriteLine("开始监控");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"开始监控异常: {ex}");
                MessageBox.Show($"启动监控失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        private void StopMonitoring()
        {
            try
            {
                // 停止定时器
                _timer?.Stop();

                // 停止采样
                RealTimeChartViewModel.StopMonitoring();

                Debug.WriteLine("停止监控");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止监控异常: {ex}");
                MessageBox.Show($"停止监控失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新图表
        /// </summary>
        private void UpdateChart()
        {
            try
            {
                // 确保控件已初始化
                if (!_isInitialized) return;

                // 确保SeriesCollection已初始化
                if (RealTimeChartViewModel.SeriesCollection == null || RealTimeChartViewModel.SeriesCollection.Count == 0)
                {
                    return;
                }

                int processedCount = 0;
                const int maxProcessPerUpdate = 10; // 每次更新最多处理10个数据点
                bool hasNewData = false;
                while (RealTimeChartViewModel.GetData(out SignalData signalData) && processedCount < maxProcessPerUpdate)
                {
                    var serial = RealTimeChartViewModel.SeriesCollection[0].Values;
                    //Trace.WriteLine(string.Join(" ", signalData.Data));
                    foreach (var item in signalData.Data)
                    {
                        serial.Add((double)item);
                        // 限制最大数据点数
                        if (serial.Count >= RealTimeChartViewModel.MaxDataPoints)
                        {
                            serial.RemoveAt(0);
                        }
                    }
                    RealTimeChartViewModel.DataCount = signalData.Position + signalData.Data.Length;
                    RealTimeChartViewModel.CurrentData = signalData.Data.LastOrDefault();

                    hasNewData = true;
                    processedCount++;
                }

                // 如果有新数据且处于自动滚动模式，标记需要更新图表
                if (hasNewData && RealTimeChartViewModel.IsAutoScrolling)
                {
                    _chartNeedsUpdate = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新图表异常: {ex}");
            }
        }
    }
}