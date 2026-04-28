using DHHandle;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace Example_Demo
{
    public class BaseView : UserControl
    {
        public MachineMonitor m_HardWare;
        public List<HardChannel> m_AllHardChannel;
        public ObservableCollection<WPFChannel> ShowChannels = new ObservableCollection<WPFChannel>();
        public int DefaultMeasureType = -1;
        /// <summary>
        /// 已经重写界面的测量类型通道
        /// </summary>
        public static List<int> HaveViewMeasureTypes = new List<int>() { (int)MEASURE_TYPE.MEASURE_TYPE_SENSOR_BT, (int)MEASURE_TYPE.MEASURE_PULSE_COUNTER, (int)MEASURE_TYPE.MEASURE_TYPE_IO,
            (int)MEASURE_TYPE.MEASURE_TYPE_TEMPERATURE_PT,(int)MEASURE_TYPE.MEASURE_TYPE_STRAINMETER,(int)MEASURE_TYPE.MEASURE_TYPE_CAN,
            (int)MEASURE_TYPE.MEASURE_TYPE_TEMPERATURE_THERMO,(int)MEASURE_TYPE.MEASURE_TYPE_INTERNAL_DA};

        public virtual void InitUI(MachineMonitor hardWare, List<HardChannel> childHardChannel)
        {
            m_HardWare = hardWare;
            m_AllHardChannel = childHardChannel;
        }

        public virtual void RefreshChannel()
        {

        }

        /// <summary>
        /// 获取子页面的通道信息
        /// </summary>
        /// <param name="measureType"></param>
        /// <returns></returns>
        public List<HardChannel> GetListHardChannel()
        {
            return m_AllHardChannel.FindAll(F => F.m_nMeasureType == DefaultMeasureType);
        }

        public void cmbUseFlag_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.UseFlag == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_USE, cmb.SelectedItem.ToString());
            chn.m_HardChannel.m_nMeasureType = m_HardWare.GetChannelMeasureType(chn.m_HardChannel);
            RefreshChannel();
        }

        public void cmbMeasureType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.MeasureType == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_MEASURETYPE, cmb.SelectedItem.ToString());
            chn.m_HardChannel.m_nMeasureType = m_HardWare.GetChannelMeasureType(chn.m_HardChannel);
            RefreshChannel();
        }

        public void cmbFullRand_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.FullRand == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_FULLVALUE, cmb.SelectedItem.ToString());
            chn.FullRand = m_HardWare.GetMacChnCurrentParam(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_FULLVALUE);
        }

        public void cmbSampleRate_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.SampleRate == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SAMPLE, cmb.SelectedItem.ToString());
            chn.SampleRate = m_HardWare.GetMacChnCurrentParam(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_SAMPLE);
            RefreshChannel();
        }

        public void cmbUpFreq_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.UpFreq == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_UPFREQ, cmb.SelectedItem.ToString());
            chn.UpFreq = m_HardWare.GetMacChnCurrentParam(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_UPFREQ);
        }

        public void cmbInputMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox cmb = sender as ComboBox;
            WPFChannel chn = cmb.DataContext as WPFChannel;
            if (chn == null || cmb.SelectedItem == null)
                return;

            if (chn.InputMode == cmb.SelectedItem.ToString())
            {
                return;
            }

            m_HardWare.ModifyParamAndSendCode(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_INPUTMODE, cmb.SelectedItem.ToString());
            chn.InputMode = m_HardWare.GetMacChnCurrentParam(chn.m_HardChannel, ParamShowDefine.SHOW_CHANNEL_INPUTMODE);
        }
    }
}
