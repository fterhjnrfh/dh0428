using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace DHHandle
{
    /// <summary>
    /// 数据处理
    /// </summary>
    public partial class MachineMonitor
    {
        public void GetDataThread()
        {
            return;
            int maxDataCount = m_lstDevice.Select(F => (int)(F.m_lstHardChannel.Count * F.CurrentSamplefreq)).Max();
            int nBufferSize = (int)Math.Max(1024 * 1024, maxDataCount);
            nBufferSize *= 4;
            IntPtr pBuffer = Marshal.AllocHGlobal(sizeof(float) * nBufferSize);
            IntPtr pBufferPosAddr = Marshal.AllocHGlobal(sizeof(float) * nBufferSize);
            long nTotalDataPos = 0L;
            long lReceiveCount = 0L;
            long lChnCount = 0L;
            int nValueSize;
            int nReturnValue;
            List<Tuple<HardChannel, long, Can_Data>> canDatas = new List<Tuple<HardChannel, long, Can_Data>>();
            while (m_bThread)
            {
                try
                {
                    switch (m_GetDataType)
                    {
                        case GetDataTypeEnum.SingleMachine:
                            {
                                foreach (Device device in m_lstDevice)
                                {
                                    if (!device.ReadData)
                                        continue;

                                    if (device.m_bOutDataDevice) //外部数据源通道
                                    {
                                        HardWare_StandardC.GetOutDataData(device.m_nDeviceID, nBufferSize, pBuffer, out nTotalDataPos, out lReceiveCount, out lChnCount);
                                        if (lReceiveCount > 0)
                                        {
                                            float[] pValue = new float[lReceiveCount * lChnCount];
                                            Marshal.Copy(pBuffer, pValue, 0, (int)(lReceiveCount * lChnCount));
                                            DealSingleMachineSerialData(device, pValue, lReceiveCount, lChnCount, nTotalDataPos);
                                        }
                                    }
                                    else //常规数采通道
                                    {
                                        HardWare_StandardC.GetOneMacChnData_New(device.m_nDeviceID, out lReceiveCount, out lChnCount, out nTotalDataPos, nBufferSize, pBuffer);
                                        if (lReceiveCount > 0)
                                        {
                                            if (m_bFirst)
                                            {
                                                ResetDataIndex();
                                            }
                                            float[] pValue = new float[lReceiveCount * lChnCount];
                                            Marshal.Copy(pBuffer, pValue, 0, (int)(lReceiveCount * lChnCount));
                                            DealSingleMachineSerialData(device, pValue, lReceiveCount, lChnCount, nTotalDataPos);
                                        }

                                        //单台仪器不同通道不同采样频率获取接口
                                        //HardWare_StandardC.GetOneMacChnData_CommonRate(device.m_nDeviceID, out lReceiveCount, out lChnCount, out nTotalDataPos, nBufferSize, pBuffer);
                                        //if (lReceiveCount > 0)
                                        //{
                                        //    if (m_bFirst)
                                        //    {
                                        //        ResetDataIndex();
                                        //    }                                            
                                        //    DealDiffSampleFreqSingleMachineSerialData(device, pBuffer, lReceiveCount, lChnCount, nTotalDataPos);
                                        //}
                                    }
                                }
                            }
                            break;
                        case GetDataTypeEnum.MultiMachine:
                            {
                                HardWare_StandardC.GetAllMacChnData(nBufferSize, pBuffer, out nTotalDataPos, out lReceiveCount, out lChnCount);
                                if (lReceiveCount > 0)
                                {
                                    if (m_bFirst)
                                    {
                                        ResetDataIndex();
                                    }
                                    float[] pValue = new float[lReceiveCount * lChnCount];
                                    Marshal.Copy(pBuffer, pValue, 0, (int)(lReceiveCount * lChnCount));
                                    DealMultiMachineSerialData(pValue, lReceiveCount, lChnCount, nTotalDataPos);
                                }
                            }
                            break;
                        case GetDataTypeEnum.TeamMachine:
                            {
                                foreach (var item in m_DictTeamId)
                                {
                                    HardWare_StandardC.GetOneMacTeamChnData_New(item.Item1, item.Item2, pBuffer, nBufferSize, out nTotalDataPos, out lReceiveCount, out lChnCount, out nReturnValue);
                                    if (lReceiveCount <= 0)
                                        continue;

                                    float[] pValue = new float[lReceiveCount * lChnCount];
                                    //这边转化数据，需要用（每个通道的数据量 * 通道数 ）得到所有通道数据
                                    Marshal.Copy(pBuffer, pValue, 0, (int)(lReceiveCount * lChnCount));
                                    DealTeamMachineSerialData(item.Item1, item.Item2, pValue, lReceiveCount, lChnCount, nTotalDataPos);
                                }
                            }
                            break;
                        default:
                            break;
                    }

                    canDatas.Clear();
                    foreach (Device device in m_lstDevice)
                    {
                        foreach (var channel in device.m_lstHardChannel)
                        {
                            if (channel.IsIOChannel)
                            {
                                foreach (var item in channel.IOChannels)
                                {
                                    HardWare_StandardC.DA_GetIOChnData(device.m_nDeviceID, channel.m_nChannelID, item.Index, nBufferSize, pBuffer, out lReceiveCount);
                                    if (lReceiveCount > 0)
                                    {
                                        item.Data = Marshal.ReadInt32(pBuffer, ((int)lReceiveCount - 1) * sizeof(int));
                                        byte[] bytes = new byte[lReceiveCount * 4];
                                        Marshal.Copy(pBuffer, bytes, 0, (int)lReceiveCount);
                                    }
                                }
                                if (EventIODataChanged != null)
                                    EventIODataChanged(channel);
                            }
                            else if (channel.IsStaticStatChn) //485数据都是从GetRS485ChnData_New获取数据
                            {
                                HardWare_StandardC.GetRS485ChnData_New(device.m_nDeviceID, channel.m_nChannelID, nBufferSize, pBuffer, out lReceiveCount, out lChnCount);
                                if (lReceiveCount > 0)
                                {
                                    DealStatData(pBuffer, channel, lReceiveCount, (int)lChnCount);
                                }
                            }
                            else if (channel.IsCANChannel) //获取CAN通道数据
                            {
                                if (HardWare_StandardC.GetExtraCANChnData(device.m_nDeviceID, channel.m_nChannelID, nBufferSize, pBuffer, out nValueSize) == 1)
                                {
                                    DealCANData(canDatas, channel, pBuffer, nValueSize);
                                }
                            }
                        }

                        //获取计数器数据
                        if (device.m_bExistPulseChn)
                        {
                            HardWare_StandardC.GetOneMacPluseData(device.m_nDeviceID, out lReceiveCount, out lChnCount, nBufferSize, pBuffer, nBufferSize, pBufferPosAddr);
                            if (lReceiveCount > 0)
                                DealPulseData(device, pBuffer, pBufferPosAddr, lReceiveCount, lChnCount);
                        }
                    }
                    if (canDatas.Count > 0)
                    {
                        EventCANDataChanged?.Invoke(canDatas);
                    }

                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    Utility.WriteLog(ex.Message + ex.StackTrace);
                }
            }
            Marshal.FreeHGlobal(pBuffer);
        }

        private void ResetDataIndex()
        {
            foreach (Device device in m_lstDevice)
            {
                foreach (HardChannel channel in device.m_lstHardChannel)
                {
                    if (device.m_bOutDataDevice)
                    {
                        channel.m_nDataIndex = channel.m_nChannelID;
                    }
                    else
                    {
                        channel.ResetDataIndex(m_GetDataType);
                    }
                }
            }
            m_bFirst = false;
        }

        public void FireMultiEvent(Dictionary<HardChannel, float[]> keyValues, long pos)
        {
            if (MultiChnEventDataChanged != null)
            {
                MultiChnEventDataChanged(keyValues, pos);
            }
        }

        private void DealSingleMachineSerialData(Device device, float[] pValue, long nReceiveCount, long nChnCount, long nTotalDataPos)
        {
            m_DictChnData.Clear();
            foreach (HardChannel hardChannel in device.m_lstHardChannel)
            {
                if (hardChannel.IsTimeChn())
                {
                    float[] pfltData = new float[nReceiveCount];
                    Array.Copy(pValue, hardChannel.m_nDataIndex * nReceiveCount, pfltData, 0, nReceiveCount);
                    m_DictChnData[hardChannel] = pfltData;
                }
            }
            FireMultiEvent(m_DictChnData, nTotalDataPos);
        }

        /// <summary>
        /// 获取不同通道不同采样频率数据
        /// </summary>
        /// <param name="device"></param>
        /// <param name="pBuffer">数据格式  chnid datacount 数据；chnid datacount 数据；</param>
        /// <param name="nReceiveCount"></param>
        /// <param name="nChnCount"></param>
        /// <param name="nTotalDataPos"></param>
        private void DealDiffSampleFreqSingleMachineSerialData(Device device, IntPtr pBuffer, long nReceiveCount, long nChnCount, long nTotalDataPos)
        {
            m_DictChnData.Clear();
            int nOffset = 0;
            for (int i = 0; i < nChnCount; i++)
            {
                int nChnid = Marshal.ReadInt32(pBuffer, nOffset);
                nOffset += sizeof(int);

                int dataCount = Marshal.ReadInt32(pBuffer, nOffset);
                nOffset += sizeof(int);

                HardChannel hardChannel = device.m_lstHardChannel.Find(F => F.m_nChannelID == nChnid);
                if (hardChannel != null)
                {
                    float[] pValue = new float[dataCount];
                    Marshal.Copy(pBuffer + nOffset, pValue, 0, dataCount);
                    m_DictChnData[hardChannel] = pValue;
                }
                nOffset += dataCount * sizeof(float);
            }
            FireMultiEvent(m_DictChnData, nTotalDataPos);
        }

        private void DealTeamMachineSerialData(int deviceId, int teamid, float[] pValue, long nReceiveCount, long nChnCount, long nTotalDataPos)
        {
            m_DictChnData.Clear();
            foreach (HardChannel hardChannel in m_lstHardChannel.FindAll(F => F.m_nDeviceID == deviceId && F.m_nTeamId == teamid))
            {
                int index = hardChannel.m_nTeamIndex;
                float[] pfltData = new float[nReceiveCount];
                for (int k = 0; k < nReceiveCount; k++)
                {
                    pfltData[k] = pValue[index * nReceiveCount + k];
                }
                m_DictChnData[hardChannel] = pfltData;
            }
            FireMultiEvent(m_DictChnData, nTotalDataPos);
        }

        private void DealMultiMachineSerialData(float[] pValue, long nReceiveCount, long nChnCount, long nTotalDataPos)
        {
            m_DictChnData.Clear();
            foreach (var device in m_lstDevice)
            {
                foreach (HardChannel hardChannel in device.m_lstHardChannel)
                {
                    if (hardChannel.IsTimeChn())
                    {
                        float[] pfltData = new float[nReceiveCount];
                        Array.Copy(pValue, hardChannel.m_nDataIndex * nReceiveCount, pfltData, 0, nReceiveCount);
                        m_DictChnData[hardChannel] = pfltData;
                    }
                }
            }

            FireMultiEvent(m_DictChnData, nTotalDataPos);
        }

        private void DealStatData(IntPtr pBuffer, HardChannel channel, long nReceiveCount, int nChnCount)
        {
            //单个RS422/振弦 HardChannel 能拆分多个Chn出来，接口返回的nChnCount即为选择的m_HardChannel拆分出的通道数量
            //比如振弦，单个m_HardChannel拆分出3个Chn，pBuffer按照 pos(8个字节)+chn1数据+chn2数据+chn3数据+pos(8个字节)+chn1数据+chn2数据+chn3数据
            float[] pfltData = new float[nReceiveCount];
            float[] pValue = new float[nReceiveCount * nChnCount];
            long[] allPos = new long[nReceiveCount]; //该数组为获得数据的所有位置，
            //这边转化数据，需要用（每个通道的数据量 * 通道数 ）得到所有通道数据
            int nOffSet = 0;
            int length = sizeof(long) + sizeof(float) * nChnCount;
            byte[] dataByte = new byte[length];
            for (int i = 0; i < nReceiveCount; i++)
            {
                ReadBytesFromPointer(pBuffer, nOffSet, dataByte, 0, length);
                nOffSet += length;

                allPos[i] = BitConverter.ToInt64(dataByte, 0);
                for (int j = 0; j < nChnCount; j++)
                {
                    pValue[i * nChnCount + j] = BitConverter.ToSingle(dataByte, sizeof(long) + j * sizeof(float));
                }
            }
            //pValue即为该m_HardChannel 所有拆分通道数据，chn1数据+chn2数据+chn3+chn1数据+chn2数据+chn3数据
            if (EventStaticStatDataChanged != null)
            {
                EventStaticStatDataChanged(channel, allPos, nChnCount, pValue);
            }
        }

        private void DealPulseData(Device device, IntPtr pBuffer, IntPtr pBufferPosAddr, long nReceiveCount, long nChnCount)
        {
            long[] pPosValue = new long[nReceiveCount];
            Marshal.Copy(pBufferPosAddr, pPosValue, 0, (int)nReceiveCount);

            int nChnIndex = 0;
            int nOffset = 0;
            foreach (HardChannel hardChannel in device.m_lstHardChannel)
            {
                if (hardChannel.IsPulseChannel)
                {
                    PulsData[] pulsDatas = new PulsData[nReceiveCount];
                    for (int i = 0; i < nReceiveCount; i++)
                    {
                        pulsDatas[i] = (PulsData)Marshal.PtrToStructure(pBuffer + nOffset, typeof(PulsData));
                        nOffset += sizeof(int) * 3;
                    }
                    nChnIndex++;
                    if (EventPulseDataChanged != null)
                    {
                        EventPulseDataChanged(hardChannel, pPosValue, pulsDatas);
                    }
                }
            }
        }

        private void DealCANData(List<Tuple<HardChannel, long, Can_Data>> canDatas, HardChannel channel, IntPtr pBuffer, int nBufferSize)
        {
            int nOffset = 0;
            while (nOffset < nBufferSize)
            {
                long llPos = Marshal.ReadInt64(pBuffer, nOffset);
                nOffset += sizeof(long);

                Can_Data can_Data = new Can_Data();
                can_Data.nHeadSignature = Marshal.ReadInt32(pBuffer, nOffset); // 标识 0x9C
                nOffset += sizeof(int);

                can_Data.nType = Marshal.ReadInt32(pBuffer, nOffset);   // 0 -- 标准帧， 1 - 扩展帧
                nOffset += sizeof(int);

                can_Data.nStruct = Marshal.ReadInt32(pBuffer, nOffset); // 帧结构， 0 -- 数据帧，1 -- 远程帧
                nOffset += sizeof(int);

                can_Data.cID = new byte[4];// 标识
                Marshal.Copy(pBuffer + nOffset, can_Data.cID, 0, 4);
                nOffset += 4;

                can_Data.nDataCount = Marshal.ReadInt32(pBuffer, nOffset);// // 数据长度
                nOffset += sizeof(int);

                can_Data.pData = new byte[can_Data.nDataCount];
                Marshal.Copy(pBuffer + nOffset, can_Data.pData, 0, can_Data.nDataCount);
                nOffset += can_Data.nDataCount;

                canDatas.Add(new Tuple<HardChannel, long, Can_Data>(channel, llPos, can_Data));
            }
        }

        public bool GetGPSData(int deviceid, out GpsData gpsData, out float time)
        {
            time = 0;
            gpsData = new GpsData();
            int nReadCount = 0;
            for (int i = 0; i < 6; i++)
            {
                if (HardWare_StandardC.GetSampleStatValue(deviceid, (int)CHANNEL_STYLE.GPS_CHANNEL_STYLE, i, out time, out float value) == 1)
                {
                    switch ((GPSEnum)i)
                    {
                        case GPSEnum.GPS_TYPE_LONG:
                            gpsData.m_fltLong = value;
                            break;
                        case GPSEnum.GPS_TYPE_LAT:
                            gpsData.m_fltLat = value;
                            break;
                        case GPSEnum.BrsGPS_TYPE_VISCOUNT:
                            gpsData.m_nVisCount = (int)value;
                            break;
                        case GPSEnum.BrsGPS_TYPE_TRACKCOUNT:
                            gpsData.m_nTraceCount = (int)value;
                            break;
                        case GPSEnum.BrsGPS_TYPE_SPEED:
                            gpsData.m_fltSpeed = value;
                            break;
                        case GPSEnum.BrsGPS_TYPE_HEIGHT:
                            gpsData.m_fltHeight = value;
                            break;
                        default:
                            break;
                    }
                    nReadCount++;
                }
            }
            return nReadCount > 0;
        }

        private static void ReadBytesFromPointer(IntPtr source, int sourceOffset, byte[] destination, int desOffset, int length)
        {
            Marshal.Copy(source + sourceOffset, destination, desOffset, length);
        }
    }
}
