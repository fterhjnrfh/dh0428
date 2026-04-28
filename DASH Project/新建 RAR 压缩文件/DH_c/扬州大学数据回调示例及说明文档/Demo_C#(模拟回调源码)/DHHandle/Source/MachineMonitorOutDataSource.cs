using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DHHandle
{
    public partial class MachineMonitor
    {
        public List<int> GetOutDataSourceType()
        {
            List<int> ints = new List<int>();
            IntPtr intPtr = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
            int nUseBuffer;
            int res = HardWare_StandardC.GetOutDataSourceType(HardWare_StandardC.StandardCapacity, intPtr, out nUseBuffer);
            if (res < 0)
            {
                Utility.WriteLog("GetOutDataSourceType获取失败");
            }
            string datainfo = StringInfoHelper.GetStringFromInptr(intPtr, nUseBuffer);
            foreach (var item in datainfo.Split(','))
            {
                if (string.IsNullOrEmpty(item))
                    continue;

                string[] strings = item.Split(':');
                if (strings.Length == 0)
                    continue;

                int val;
                if (int.TryParse(strings[0], out val))
                {
                    ints.Add(val);
                }
            }
            return ints;
        }

        /// <summary>
        /// 获取多个外部数据源中某一个状态
        /// </summary>
        public bool GetOneTypeOutDataSourceStatus(int nOutDataType, out int nUseOutDataSource, out string strIP, out int nPort)
        {
            nUseOutDataSource = 0;
            strIP = "";
            nPort = 0;
            try
            {
                IntPtr intPtr = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
                int nUseBuffer;
                int res = HardWare_StandardC.GetOneTypeOutDataSourceStatus(out nUseOutDataSource, nOutDataType, HardWare_StandardC.StandardCapacity, intPtr, out nUseBuffer);
                string datainfo = StringInfoHelper.GetStringFromInptr(intPtr, nUseBuffer);
                if (res < 0 || string.IsNullOrEmpty(datainfo))
                {
                    Utility.WriteLog("GetOneTypeOutDataSourceStatus失败");
                    return false;
                }
                strIP = datainfo.Split(',')[0];
                nPort = int.Parse(datainfo.Split(',')[1]);
            }
            catch (Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 设置多个外部数据源某一个状态
        /// </summary>
        /// <param name="bUsed"></param>
        /// <param name="nOutDataType"></param>
        /// <param name="pInfo"></param>
        public void SetOneTypeOutDataSourceStatus(bool bUsed, int nOutDataType, string pInfo)
        {
            int res = HardWare_StandardC.SetOneTypeOutDataSourceStatus(bUsed ? 1 : 0, nOutDataType, pInfo);
            if (res < 0)
            {
                Utility.WriteLog("SetOneTypeOutDataSourceStatus失败");
            }
        }

        /// <summary>
        /// 获取外部数据源
        /// </summary>
        public void FindOutData()
        {
            m_lstDevice.RemoveAll(F => F.m_bOutDataDevice);
            List<int> outtypes = GetOutDataSourceType();

            for (int nIndex = 0; nIndex < outtypes.Count; nIndex++)
            {
                int deviceId;
                string strMacIp = "";
                int nChnCount;
                int nOutType = outtypes[nIndex];

                IntPtr strParamPtr = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);
                int nUseBuffer;
                HardWare_StandardC.GetOutDataSourceMacIDFromIndex(nIndex, out deviceId, strMacIp, out nChnCount, out nOutType);
                if (nChnCount == 0)
                    continue;

                Device device = new Device(deviceId, strMacIp);
                device.m_bOutDataDevice = true;
                for (int i = 0; i < nChnCount; i++)
                {
                    int errorcode = HardWare_StandardC.GetOutDataSourceChannelFromIndex(device.m_nDeviceID, i, 0, strParamPtr, out nUseBuffer); //读取外部数据源通道名称
                    if (errorcode < 0)
                    {
                        Utility.WriteLog("GetOutDataSourceChannelFromIndex  " + i + "  未查找到通道  errorcode:" + errorcode);
                        continue;
                    }
                    string name = StringInfoHelper.GetStringFromInptr(strParamPtr, nUseBuffer);
                    HardChannel hardChannel = new HardChannel(device.m_nDeviceID, device.m_strDeviceIP, i, 1);
                    hardChannel.m_nMeasureType = GetChannelMeasureType(hardChannel);
                    hardChannel.ChangeChannelName(name);

                    m_lstHardChannel.Add(hardChannel);
                    device.m_lstHardChannel.Add(hardChannel);
                }
                if (device.m_lstHardChannel.Count > 0)
                    m_lstDevice.Add(device);
            }
        }
    }
}
