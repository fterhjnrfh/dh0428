using DHHandle;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Linq;

namespace Example_Demo
{
    public class ShowDataModel : INotifyPropertyChanged
    {
        const int MaxShowLength = 10000;
        private MachineMonitor m_MachineMonitor;

        bool _ShowData = true;
        public bool ShowData
        {
            get
            {
                return _ShowData;
            }
            set
            {
                _ShowData = value;
                OnPropertyChanged("ShowData");
            }
        }

        string _Data = "";
        public string Data
        {
            get
            {
                return _Data;
            }
            set
            {
                if (ShowData)
                {
                    if (_Data.Length > MaxShowLength)
                    {
                        _Data = "";
                    }
                    else
                    {
                        _Data = value;
                    }
                    OnPropertyChanged("Data");
                }
            }
        }

        string _StatData = "";
        public string StatData
        {
            get
            {
                return _StatData;
            }
            set
            {
                if (ShowData)
                {
                    _StatData = value;
                    OnPropertyChanged("StatData");
                }
            }
        }

        string _GpsData = "";
        public string GpsData
        {
            get
            {
                return _GpsData;
            }
            set
            {
                if (ShowData)
                {
                    if (_GpsData.Length > MaxShowLength)
                    {
                        _GpsData = "";
                    }
                    else
                    {
                        _GpsData = value;
                    }
                    OnPropertyChanged("GpsData");
                }
            }
        }

        public List<Device> Devices
        {
            get
            {
                return m_MachineMonitor.Devices;
            }
        }

        public Device SelectDevice { get; set; }

        public ShowDataModel(MachineMonitor _machineMonitor)
        {
            m_MachineMonitor = _machineMonitor;
            SelectDevice = Devices.FirstOrDefault();
            UnRegisterDataEvent();
            RegisterDataEvent();
        }

        void RegisterDataEvent()
        {
            m_MachineMonitor.MultiChnEventDataChanged += MachineMonitor_MultiChnEventDataChanged;
            m_MachineMonitor.EventStaticStatDataChanged += MachineMonitor_EventStaticStatDataChanged;
            m_MachineMonitor.EventPulseDataChanged += MachineMonitor_EventPulseDataChanged;
            m_MachineMonitor.EventCANDataChanged += MachineMonitor_EventCANDataChanged;
        }

        void UnRegisterDataEvent()
        {
            m_MachineMonitor.MultiChnEventDataChanged -= MachineMonitor_MultiChnEventDataChanged;
            m_MachineMonitor.EventStaticStatDataChanged -= MachineMonitor_EventStaticStatDataChanged;
            m_MachineMonitor.EventPulseDataChanged -= MachineMonitor_EventPulseDataChanged;
            m_MachineMonitor.EventCANDataChanged -= MachineMonitor_EventCANDataChanged;
        }

        /// <summary>
        /// 示例，数据写在安装目录\logs文件夹内d
        /// 仅写入字典Value 第一个点数据到文件
        /// </summary>
        /// <param name="dictValue"></param>
        /// <param name="pos"></param>
        private void MachineMonitor_MultiChnEventDataChanged(Dictionary<HardChannel, float[]> dictValue, long pos)
        {
            string info = "模拟通道数据 pos:" + pos + Environment.NewLine;
            foreach (var key in dictValue.Keys)
            {
                info += key.m_strChannelName + ":" + dictValue[key][0] + " ";
            }
            Data = info + Environment.NewLine + Data + Environment.NewLine;
        }

        /// <summary>
        /// 振弦或者485通道数据
        /// </summary>
        /// <param name="hardChannel"></param>
        /// <param name="allPos"></param>
        /// <param name="splitCount"></param>
        /// <param name="data"></param>
        private void MachineMonitor_EventStaticStatDataChanged(HardChannel hardChannel, long[] allPos, int splitCount, float[] data)
        {
            string info = "静态数据 pos:" + allPos[0] + Environment.NewLine;
            info += hardChannel.m_strChannelName;
            for (int i = 0; i < splitCount; i++)
            {
                info += data[i] + " ";
            }
            StatData = info + Environment.NewLine + StatData;
        }

        /// <summary>
        /// 计数器数据
        /// </summary>
        /// <param name="hardChannel"></param>
        /// <param name="data"></param>
        private void MachineMonitor_EventPulseDataChanged(HardChannel hardChannel, long[] pos, PulsData[] data)
        {
            string info = string.Format("计数器 {0} pos:{1} 旋转方向:{2}  位置计数器:{3}  脉冲计数器:{4}", hardChannel.m_strChannelName,
                pos[0], data[0].nCircleDirection, data[0].nPosCounter, data[0].nPulseCounter);
            StatData = info + Environment.NewLine + StatData + Environment.NewLine;
        }

        private void MachineMonitor_EventCANDataChanged(List<Tuple<HardChannel, long, Can_Data>> obj)
        {
            string info = "";
            foreach (var item in obj)
            {
                info += $"{item.Item1.m_strChannelName}==>pos:{item.Item2}" + Environment.NewLine;
                info += item.Item3.ToString();
                info += Environment.NewLine;
            }
            StatData = info + Environment.NewLine;
        }

        public void GetGpsData()
        {
            if (SelectDevice == null)
            {
                GpsData = "请选择仪器!";
                return;
            }
            if (m_MachineMonitor.GetGPSData(SelectDevice.DeviceID, out GpsData gpsData, out float time))
            {
                GpsData = $"time:{time}  {gpsData.ToString()}";
            }
            else
            {
                GpsData = "获取失败!";
            }
        }

        public void Dispose()
        {
            UnRegisterDataEvent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var temp = PropertyChanged;
            if (temp != null)
            {
                temp(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
