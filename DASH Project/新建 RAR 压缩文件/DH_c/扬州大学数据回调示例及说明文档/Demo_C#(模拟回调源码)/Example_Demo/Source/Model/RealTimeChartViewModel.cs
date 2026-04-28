using DHHandle;
using Example_Demo.Source.Model;
using LiveCharts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Example_Demo
{
    internal class RealTimeChartViewModel : INotifyPropertyChanged
    {
        // 图表相关属性
        public SeriesCollection SeriesCollection { get; set; }
        public string[] Labels { get; set; }
        public Func<double, string> YFormatter { get; set; }

        // 设备监控相关
        private MachineMonitor _machineMonitor { get; set; }

        // 数据队列 - 线程安全的并发队列
        private ConcurrentQueue<SignalData> _signalDatas = new ConcurrentQueue<SignalData>();

        // 当前选中的通道
        private HardChannel _SelectHardChannel;
        public HardChannel SelectHardChannel
        {
            get => _SelectHardChannel;
            set
            {
                if (_SelectHardChannel != null)
                    _SelectHardChannel.EventDataChanged -= SelectHardChannel_EventDataChanged;
                SetProperty(ref _SelectHardChannel, value, "SelectHardChannel");
                if (SelectHardChannel != null)
                    SelectHardChannel.EventDataChanged += SelectHardChannel_EventDataChanged;
            }
        }


        private bool _IsMonitoring = false;
        public bool IsMonitoring
        {
            get => _IsMonitoring;
            set => SetProperty(ref _IsMonitoring, value, "IsMonitoring");
        }

        public List<HardChannel> TimeChannels { get => _machineMonitor?.AllTimeChannels; }

        // 最大数据点数
        public int MaxDataPoints { get; set; } = 1000;

        // 是否正在自动滚动
        private bool _IsAutoScrolling = true;
        public bool IsAutoScrolling
        {
            get => _IsAutoScrolling;
            set => SetProperty(ref _IsAutoScrolling, value, "IsAutoScrolling");
        }

        // 是否用户正在手动缩放
        public bool IsUserZooming { get; set; } = false;

        private long _DataCount = 0;
        public long DataCount
        {
            get => _DataCount;
            set => SetProperty(ref _DataCount, value, "DataCount");
        }

        private float _CurrentData = 0;
        public float CurrentData
        {
            get => _CurrentData;
            set => SetProperty(ref _CurrentData, value, "CurrentData");
        }

        public void Initialize(MachineMonitor machineMonitor)
        {
            _machineMonitor = machineMonitor;
            RefreshChannel();
        }

        internal void RefreshChannel()
        {
            OnPropertyChanged("TimeChannels");
            SelectHardChannel = TimeChannels.FirstOrDefault();
        }

        internal void StartMonitoring()
        {
            return;
            // 清空数据队列
            while (_signalDatas.TryDequeue(out _)) { }

            MaxDataPoints = (int)_machineMonitor.m_CurSampleFreq * 10;
            StopMonitoring();
            _machineMonitor.MultiChnEventDataChanged += MachineMonitor_MultiChnEventDataChanged;
            _machineMonitor.EventStaticStatDataChanged += MachineMonitor_EventStaticStatDataChanged;
            _machineMonitor.EventPulseDataChanged += MachineMonitor_EventPulseDataChanged;
            IsMonitoring = true;
        }

        internal void StopMonitoring()
        {
            if (_machineMonitor != null)
            {
                _machineMonitor.MultiChnEventDataChanged -= MachineMonitor_MultiChnEventDataChanged;
                _machineMonitor.EventStaticStatDataChanged -= MachineMonitor_EventStaticStatDataChanged;
                _machineMonitor.EventPulseDataChanged -= MachineMonitor_EventPulseDataChanged;
            }
            IsMonitoring = false;
        }

        /// <summary>
        /// 多通道数据事件处理
        /// </summary>
        private void MachineMonitor_MultiChnEventDataChanged(Dictionary<HardChannel, float[]> dictValue, long pos)
        {
            try
            {
                foreach (var key in dictValue.Keys)
                {
                    if (key == SelectHardChannel)
                    {
                        // 将数据存入线程安全队列
                        _signalDatas.Enqueue(new SignalData
                        {
                            HardChannel = key,
                            Position = pos,
                            Data = dictValue[key]
                        });
                        //if (key.m_nChannelID == 0)
                        //{
                        //    Trace.WriteLine($"pos:{pos}  max:{dictValue[key].Max()}  min:{dictValue[key].Min()}");
                        //}
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"多通道数据事件处理异常: {ex}");
            }
        }

        private void SelectHardChannel_EventDataChanged(object sender, DataChangedEventArgs e)
        {
            try
            {
                foreach (var item in e.UpdateDatas)
                {
                    // 将数据存入线程安全队列
                    _signalDatas.Enqueue(new SignalData
                    {
                        HardChannel = e.DataChangeSignal,
                        Position = item.Pos,
                        Data = item.Data
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"多通道数据事件处理异常: {ex}");
            }
        }

        /// <summary>
        /// 静态统计数据事件处理
        /// </summary>
        private void MachineMonitor_EventStaticStatDataChanged(HardChannel arg1, long[] arg2, int arg3, float[] arg4)
        {
            // 可以根据需要处理静态统计数据
        }

        /// <summary>
        /// 脉冲数据事件处理
        /// </summary>
        private void MachineMonitor_EventPulseDataChanged(HardChannel hardChannel, long[] poss, PulsData[] pulsDatas)
        {
            // 可以根据需要处理脉冲数据
        }

        /// <summary>
        /// 只处理当前选中通道的数据
        /// </summary>
        /// <param name="signalData"></param>
        /// <returns></returns>
        internal bool GetData(out SignalData signalData)
        {
            if (_signalDatas.TryDequeue(out signalData))
            {
                return signalData.HardChannel == SelectHardChannel;
            }
            return false;
        }

        #region INotifyPropertyChanged Members
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual bool SetProperty<T>(ref T storage, T value, string propertyName)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
