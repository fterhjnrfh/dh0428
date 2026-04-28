using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DHHandle
{
    /// <summary>
    /// 查找仪器通道及采样频率
    /// </summary>
    public partial class MachineMonitor
    {
        /// <summary>
        /// 获取真实仪器
        /// </summary>
        private void FindMachine()
        {
            int mac_online_count = HardWare_StandardC.GetAllMacOnlineCount();
            Utility.WriteLog("GetAllMacOnlineCount  " + mac_online_count);
            for (int i = 0; i < mac_online_count; i++)
            {
                Device device = CreateDevice(i);
                if (device != null)
                {
                    m_lstDevice.Add(device);
                    m_DictDevice[device.DeviceID] = device;
                }
            }
            if (m_lstDevice.Count == 0)
            {
                Utility.WriteLog("未查找到仪器");
            }
        }

        private Device CreateDevice(int macIndex)
        {
            IntPtr strMacIp = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
            int res = HardWare_StandardC.GetMacInfoFromIndex(macIndex, out int deviceId, strMacIp, HardWare_StandardC.StandardCapacity, out int nUseBuffer);
            string macIp = StringInfoHelper.GetStringFromInptr(strMacIp, nUseBuffer);
            switch (res)
            {
                case -1:
                    Utility.WriteLog("CreateDevice    未初始化  " + macIndex);
                    return null;
                case -2:
                    Utility.WriteLog("CreateDevice  序号超出总数   " + macIndex);
                    return null;
                case -3:
                    Utility.WriteLog("CreateDevice  内存不足  " + macIndex);
                    return null;
                default:
                    {
                        byte macStauts = HardWare_StandardC.GetMacLinkStatus(deviceId, macIp);
                        Utility.WriteLog("macId  " + deviceId + "  macIp " + macIp + "   macStauts" + macStauts);
                        Device device = new Device(deviceId, macIp);
                        device.SampleFreqs = GetMacAllSampleFreq(deviceId);
                        device.CurrentSamplefreq = GetMacSampleFreq(deviceId);

                        int channel_count = HardWare_StandardC.GetMacCurrentChnCount(device.m_nDeviceID, device.m_strDeviceIP);
                        for (int i = 0; i < channel_count; i++)
                        {
                            int errorcode = HardWare_StandardC.GetChannelIDFromAllChannelIndex(device.m_nDeviceID, device.m_strDeviceIP, i, out int nMacChnId, out int bOnLine);
                            if (errorcode < 0)
                            {
                                Utility.WriteLog("GetMacChnIdFromMacIndex  " + i + "  未查找到通道  errorcode:" + errorcode);
                                continue;
                            }
                            CreateHardChannel(device, nMacChnId, bOnLine);
                        }
                        return device;
                    }
            }
        }

        private void CreateHardChannel(Device device, int nMacChnId, int bOnLine)
        {
            HardChannel hardChannel = new HardChannel(device.m_nDeviceID, device.m_strDeviceIP, nMacChnId, bOnLine);
            hardChannel.m_nMeasureType = GetChannelMeasureType(hardChannel);
            hardChannel.m_strCounterSensor = GetMacChnCurrentParam(hardChannel, ParamShowDefine.SHOW_COUNTER_SENSOR_TYPE_EX);
            hardChannel.ChangeChannelName(GetMacChnCurrentParam(hardChannel, ParamShowDefine.SHOW_CHANNEL_NAME));
            HardWare_StandardC.GetChannelTeamID(device.m_nDeviceID, device.m_strDeviceIP, nMacChnId, out hardChannel.m_nTeamId);
            HardWare_StandardC.GetDataIndexInTeam(device.m_nDeviceID, device.m_strDeviceIP, nMacChnId, hardChannel.m_nTeamId, out hardChannel.m_nTeamIndex, out int errorcode);
            if (hardChannel.IsIOChannel)
            {
                hardChannel.GetIOStatus();
            }

            //if (hardChannel.m_bOnlineFlag == 1)
            {
                m_lstHardChannel.Add(hardChannel);
                device.m_lstHardChannel.Add(hardChannel);
                if (!m_DictTeamId.Exists(F => F.Item1 == device.m_nDeviceID && F.Item2 == hardChannel.m_nTeamId))
                {
                    m_DictTeamId.Add(new Tuple<int, int>(device.m_nDeviceID, hardChannel.m_nTeamId));
                }
            }
        }

        #region 采样频率
        public void InitSampleFreq()
        {
            m_lstSampleFreq.Clear();

            IntPtr pFreqList = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
            string strParamValue = "";
            try
            {
                int size;
                if (HardWare_StandardC.GetMacSampleFreqList(pFreqList, HardWare_StandardC.StandardCapacity, out size))
                {
                    strParamValue = StringInfoHelper.GetStringFromInptr(pFreqList, size);
                }
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            Marshal.FreeHGlobal(pFreqList);

            foreach (var item in StringInfoHelper.ParseString(strParamValue))
            {
                if (!string.IsNullOrEmpty(item))
                {
                    m_lstSampleFreq.Add(float.Parse(item));
                }
            }
            m_CurSampleFreq = HardWare_StandardC.GetMacCurrentSampleFreq();
        }

        public void SetSampleFreq(float samplefreq)
        {
            HardWare_StandardC.SetMacSampleFreq(samplefreq);
            foreach (var item in Devices)
            {
                item.CurrentSamplefreq = samplefreq;
            }
        }

        public List<float> GetMacAllSampleFreq(int deviceid)
        {
            var sampleFreqs = new List<float>();

            IntPtr pFreqList = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
            string strParamValue = "";
            try
            {
                int size;
                if (HardWare_StandardC.GetMacSampleFreqListEx(deviceid, pFreqList, HardWare_StandardC.StandardCapacity, out size))
                {
                    strParamValue = StringInfoHelper.GetStringFromInptr(pFreqList, size);
                }
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            Marshal.FreeHGlobal(pFreqList);

            foreach (var item in StringInfoHelper.ParseString(strParamValue))
            {
                if (!string.IsNullOrEmpty(item))
                {
                    sampleFreqs.Add(float.Parse(item));
                }
            }
            return sampleFreqs;
        }

        public void SetMacSampleFreq(Device device)
        {
            HardWare_StandardC.SetMacSampleFreqEx(device.DeviceID, device.CurrentSamplefreq);
            device.CurrentSamplefreq = GetMacSampleFreq(device.DeviceID);
        }

        public float GetMacSampleFreq(int deviceid)
        {
            return HardWare_StandardC.GetMacCurrentSampleFreqEx(deviceid);
        }
        #endregion
    }
}
