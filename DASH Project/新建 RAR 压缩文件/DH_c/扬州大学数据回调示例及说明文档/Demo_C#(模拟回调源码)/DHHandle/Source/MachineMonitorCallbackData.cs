using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DHHandle
{
    public class _IDHTestHardWareEvents_SampleDataChange64Event
    {
        public int nBlockIndex;
        public int nBufferCount;
        public int nChannelID;
        public int nChannelStyle;
        public int nDataCountPerChannel;
        public int nGroupID;
        public int nMachineID;
        public int nMessageType;
        public long nTotalDataCount;
        public long sampleTime;
        public string strNoDataGroupID;
        public long varSampleData;

        public _IDHTestHardWareEvents_SampleDataChange64Event(long lSampleTime, string noDataGroupID, int messageType, int groupID, int channelStyle, int channelID, int machineID, long totalDataCount, int dataCountPerChannel, int bufferCount, int blockIndex, long sampleData)
        {
            sampleTime = lSampleTime;
            strNoDataGroupID = noDataGroupID;
            nMessageType = messageType;
            nGroupID = groupID;
            nChannelStyle = channelStyle;
            nChannelID = channelID;
            nMachineID = machineID;
            nTotalDataCount = totalDataCount;
            nDataCountPerChannel = dataCountPerChannel;
            nBufferCount = bufferCount;
            nBlockIndex = blockIndex;
            varSampleData = sampleData;
        }
    }

    public partial class MachineMonitor
    {
        private HardWare_StandardC.SampleDataChangeEventHandle m_SampleDataHandler;

        /// <summary>
        /// 数据处理
        /// </summary>
        Sensor m_OnlineSensor;

        private void DealSampleData(long sampleTime, int groupIdSize, IntPtr groupInfo, int nMessageType, int nGroupID, int nChannelStyle, int nChannelID, int nMachineID, long nTotalDataCount, int nDataCountPerChannel, int nBufferCount, int nBlockIndex, long varSampleData)
        {
            string strNoDataGroupID = "";
            if (groupIdSize > 0) strNoDataGroupID = GetIntPtrToString(groupInfo, groupIdSize);

            _IDHTestHardWareEvents_SampleDataChange64Event sampleDataEvent
               = new _IDHTestHardWareEvents_SampleDataChange64Event(sampleTime, strNoDataGroupID, nMessageType, nGroupID,
               nChannelStyle, nChannelID, nMachineID, nTotalDataCount, nDataCountPerChannel, nBufferCount, nBlockIndex, varSampleData);

            ProcessSampleData(sampleDataEvent);
        }

        public void ProcessSampleData(_IDHTestHardWareEvents_SampleDataChange64Event e)
        {
            try
            {
                SampleData data = InitEventInfo(e);
                if ((data.StartPos + data.PerChnCount) > SampleData.m_lCallBackPos)
                    SampleData.m_lCallBackPos = data.StartPos + data.PerChnCount;
                // 处理数据
                DealSampleData(e, data);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
        }

        private string GetIntPtrToString(IntPtr intPtr, int size)
        {
            if (size == 0)
                return "";

            byte[] data = new byte[size];
            Marshal.Copy(intPtr, data, 0, size);
            return Encoding.Default.GetString(data, 0, size);
        }

        protected SampleData InitEventInfo(_IDHTestHardWareEvents_SampleDataChange64Event e)
        {
            SampleData data = new SampleData();

            data.m_nTrigBlockIndex = e.nBlockIndex;
            data.m_nMessageID = e.nMessageType;
            data.m_nChannelGroupID = e.nGroupID;
            data.m_nChannelStyle = e.nChannelStyle;//该字段亦被用于底层抽点个数(5902N定制功能)
            data.StartPos = e.nTotalDataCount;
            data.PerChnCount = e.nDataCountPerChannel;
            data.m_nTotalBytesCount = e.nBufferCount;
            data.m_nMacChnID = e.nChannelID;
            data.m_nMachineID = e.nMachineID;

            #region 获取数据附带信息
            switch (e.nMessageType)
            {
                case SampleData.SAMPLE_ANALOG_MULTICHN_DATA:
                case SampleData.SAMPLE_ANALOG_DATA:
                case SampleData.SAMPLE_ANALOG_MULTIFREQCHN_DATA:
                    {
                        if (e.strNoDataGroupID != null && e.strNoDataGroupID != "")
                        {
                            string[] lstData = e.strNoDataGroupID.Split('|');
                            if (lstData != null)
                            {
                                for (int i = 0; i < lstData.Length; i++)
                                {
                                    if (int.TryParse(lstData[i], out int nID))
                                    {
                                        data.m_lstNoDataGroupID.Add(nID);
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
            #endregion

            return data;
        }

        private void DealSampleData(_IDHTestHardWareEvents_SampleDataChange64Event e, SampleData data)
        {
            switch (data.m_nMessageID)
            {
                case SampleData.SAMPLE_ANALOG_DATA:      // 时间序列数据
                case SampleData.SAMPLE_SINGLEGROUP_ANALOGDATA:
                default:
                    {
                        data.m_SampleData = new byte[data.m_nTotalBytesCount];

                        IntPtr ptr = (IntPtr)e.varSampleData;
                        ProcessStoreData.ReadBytesFromPointer(ptr, 0, data.m_SampleData, 0, data.m_nTotalBytesCount);

                        m_OnlineSensor.AddAnalysisData(data);
                    }
                    break;
            }
        }

        public void ReleaseBuffer(List<SampleData> lstMachineData)
        {
            for (int i = 0; i < lstMachineData.Count; i++)
            {
                if (lstMachineData[i].m_PtrData != IntPtr.Zero)
                {
                    HardWare_StandardC.DA_ReleaseBuffer(lstMachineData[i].m_PtrData.ToInt64());
                }
            }
        }

        public void ReleaseBuffer(SampleData sampledata)
        {
            if (sampledata.m_PtrData != IntPtr.Zero)
            {
                HardWare_StandardC.DA_ReleaseBuffer(sampledata.m_PtrData.ToInt64());
            }
        }
    }
}
