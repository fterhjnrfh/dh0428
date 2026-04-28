using DHHandle;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Example_Demo
{
    /// <summary>
    /// IOView.xaml 的交互逻辑
    /// </summary>
    public partial class IOView : BaseView
    {
        Dictionary<HardChannel, List<WPFIOChannel>> m_DictIO = new Dictionary<HardChannel, List<WPFIOChannel>>();

        public IOView()
        {
            InitializeComponent();
            DefaultMeasureType = (int)MEASURE_TYPE.MEASURE_TYPE_IO;
            datagrid.ItemsSource = ShowChannels;
        }

        public override void InitUI(MachineMonitor hardWare, List<HardChannel> childHardChannel)
        {
            base.InitUI(hardWare, childHardChannel);

            RefreshChannel();

            m_HardWare.EventIODataChanged -= M_HardWare_EventIODataChanged;
            m_HardWare.EventIODataChanged += M_HardWare_EventIODataChanged;
        }

        private void M_HardWare_EventIODataChanged(HardChannel obj)
        {
            if (m_DictIO.ContainsKey(obj))
            {
                foreach (var item in m_DictIO[obj])
                {
                    item.FireDataChanged();
                }
            }
        }

        public override void RefreshChannel()
        {
            ShowChannels.Clear();
            m_DictIO.Clear();
            foreach (var item in GetListHardChannel())
            {
                m_DictIO[item] = new List<WPFIOChannel>();
                foreach (var iochn in item.IOChannels)
                {
                    WPFIOChannel chn = new WPFIOChannel();
                    chn.SetParam(m_HardWare, item, iochn);
                    ShowChannels.Add(chn);
                    m_DictIO[item].Add(chn);
                }
            }
        }

        private void TextBlock_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            WPFIOChannel wpfIo = (sender as TextBlock).DataContext as WPFIOChannel;
            if (wpfIo.m_IOChannel.IOStatus == IOChannel.IO_INPUT)
                return;

            var tempDatas = wpfIo.m_HardChannel.IOData();
            tempDatas[wpfIo.SignalIndex] = (byte)(tempDatas[wpfIo.SignalIndex] == 0 ? 1 : 0);
            string str = string.Empty;
            for (int i = tempDatas.Length - 1; i >= 0; i--)
                str += tempDatas[i];
            m_HardWare.WriteIOChnOutputValue(wpfIo.m_HardChannel, Convert.ToInt32(str, 2));
        }
    }
}
