using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace DHHandle
{
    /// <summary>
    /// 待处理数据所需信息
    /// </summary>
    public struct DataProcessInfo
    {
        /// <summary>
        /// 当前数据块索引
        /// </summary>
        public int instantIndex;
        /// <summary>
        /// 待处理数据位置
        /// </summary>
        public long dataPos;
        /// <summary>
        /// 待处理数据量
        /// </summary>
        public int perChnCount;
        /// <summary>
        /// 待处理数据buffer
        /// </summary>
        public byte[] buffer;
        /// <summary>
        /// 待处理数据指针（底层申请的数据指针用于减少数据拷贝）
        /// </summary>
        public IntPtr ptr;

        /// <summary>
        /// 是否为单通道
        /// </summary>
        public bool bSingleChn;
        /// <summary>
        /// 是否需要处理抽点采样比
        /// </summary>
        public bool bNeedProcessSampleRate;

        /// <summary>
        /// 上发数据的显示抽点比例
        /// </summary>
        public int sampleShowRate;

        /// <summary>
        /// 仪器字节偏移
        /// </summary>
        public int srcMacOffsetBytes;

        /// <summary>
        /// 一个数据所有通道字数数
        /// </summary>
        public int oneDataAllBytes;
        /// <summary>
        /// 字节偏移
        /// </summary>
        public int chnOffsetBytes;
    }

    public class GroupProcessQueue
    {
        /// <summary>
        /// 可以处理的组
        /// </summary>
        public List<int> m_lstGroupID = new List<int>();
        /// <summary>
        /// 外部数据源组
        /// </summary>
        public List<int> m_lstOutGroupID = new List<int>();
        /// <summary>
        /// 保存需要处理的数据列表
        /// </summary>
        public SourceDataQueue m_AnalysisDataQueue = new SourceDataQueue();
    }

    public class SourceDataQueue
    {
        /// <summary>
        /// 自旋锁
        /// </summary>
        protected object m_Lock = new object();

        protected List<SampleData> m_aRealTimeData;
        /// <summary>
        /// 总的条数
        /// </summary>
        protected int m_nTotalCount;
        /// <summary>
        /// SampleData时间大于可处理位置+m_dStoreOverTime,则需要存储
        /// </summary>
        protected const int m_dStoreOverTime = 10;

        public SourceDataQueue()
        {
            m_aRealTimeData = new List<SampleData>();
        }

        public List<SampleData> DelInvalidData(List<Channel_Key> lstKey)
        {
            List<SampleData> lst = new List<SampleData>();
            lock (m_Lock)
            {
                while (true)
                {
                    bool bRemove = false;

                    for (int j = 0; j < m_aRealTimeData.Count; j++)
                    {
                        for (int i = 0; i < lstKey.Count; i++)
                        {
                            if (lstKey[i].m_nChannelGroupID == m_aRealTimeData[j].m_nChannelGroupID)
                            {
                                lst.Add(m_aRealTimeData[j]);
                                m_aRealTimeData.RemoveAt(j);
                                m_nTotalCount--;
                                bRemove = true;
                                break;
                            }
                        }

                        if (bRemove)
                            break;
                    }

                    if (!bRemove)
                        break;
                }
            }

            return lst;
        }

        /// <summary>
        /// 是否存在数据
        /// </summary>
        /// <returns></returns>
        public int GetExistDataCount()
        {
            return m_nTotalCount;
        }

        public void AddData(SampleData data)
        {
            lock (m_Lock)
            {
                m_aRealTimeData.Add(data);
                m_nTotalCount++;
            }
        }

        public void AddRangeData(List<SampleData> lstSampleData)
        {
            lock (m_Lock)
            {
                m_aRealTimeData.AddRange(lstSampleData);
                m_nTotalCount += lstSampleData.Count;
            }
        }

        public void Clear()
        {
            lock (m_Lock)
            {
                m_aRealTimeData.Clear();
                m_nTotalCount = 0;
            }
        }

        public List<SampleData> GetAllData()
        {
            List<SampleData> lstData = null;
            lock (m_Lock)
            {
                lstData = m_aRealTimeData.GetRange(0, m_aRealTimeData.Count);
                m_aRealTimeData.Clear();
                m_nTotalCount = 0;
            }

            return lstData;
        }
    }

    /// <summary>
    /// 仪器数据处理模块，是采集模块与分析算法模块的边界类
    /// </summary>
    public class Sensor
    {
        /// <summary>
        /// 非采样状态需要处理的数据队列
        /// </summary>
        public SourceDataQueue m_DataQueue = new SourceDataQueue();

        /// <summary>
        /// 是否启动采集状态
        /// </summary>
        public bool m_bSample;
        /// <summary>
        /// 是否正在处理数据
        /// </summary>
        protected bool m_bProcess;

        /// <summary>
        /// 信号缓存大小
        /// </summary>
        protected int m_nMaxBufferCount;

        protected int m_nSerialBufferCount;

        /// <summary>
        /// 线程锁对象
        /// </summary>
        protected int m_LockValue = 0;

        /// <summary>
        /// 保存需要处理的数据列表
        /// </summary>
        protected SourceDataQueue m_AnalysisDataQueue = new SourceDataQueue();

        /// <summary>
        /// 数据处理分析定时器
        /// </summary>
        protected System.Timers.Timer m_Timer;

        protected List<Task> m_lstTasks = new List<Task>();

        /// <summary>
        /// 是否超过最大内存使用量
        /// </summary>
        public bool m_bMaxMemory = false;
        public long m_maxMemory = 2147483648;

        /// <summary>
        /// 是否曾经超过最大缓存
        /// </summary>
        public bool m_IsEverOverMaxMemory = false;

        public MachineMonitor m_MainController;

        //此处应该从通道读取
        int m_nDataByteSize = 4;

        /// <summary>
        /// 添加待分析的数据
        /// </summary>
        /// <param name="data"></param>
        public bool AddAnalysisData(SampleData data)
        {
            m_AnalysisDataQueue.AddData(data);

            return true;
        }

        /// <summary>
        /// 启动处理
        /// </summary>
        /// <param name="bCheckMaxBufferCount">是否检查数据队列数据量</param>
        /// <param name="bNeedOutGroup">是否需要外部数据源数据</param>
        public void Start()
        {
            try
            {
                PrepareDataQueue();

                // Sensor, 分析使用
                m_Timer = new System.Timers.Timer(10);
                m_Timer.Elapsed += new ElapsedEventHandler(DealMachineData);
                m_Timer.Start();
            }
            catch (Exception ex)
            {
                Utility.WriteLog(ex);
                m_bSample = false;
            }
        }

        private void PrepareDataQueue()
        {
            m_DataQueue.Clear();

            List<SampleData> lstData = m_AnalysisDataQueue.GetAllData();
            m_MainController.ReleaseBuffer(lstData);
            m_AnalysisDataQueue.Clear();
        }

        public void Stop()
        {
            if (m_Timer != null)
                m_Timer.Enabled = false;

            int nSleepCount = 0;
            while (m_bProcess)
            {
                nSleepCount++;
                Thread.Sleep(100);

                if (nSleepCount > 50)
                    break;
            }
        }

        /// <summary>
        /// 提取显示与处理的数据
        /// </summary>
        public void PickShowAndProcessData(List<SampleData> lstData, bool bUseMoreMemory)
        {
            if (lstData.Count == 0)
                return;

            try
            {
                for (int i = 0; i < lstData.Count; i++)
                {
                    SampleData fData = lstData[i];

                    if (!m_bSample)
                    {
                        if (fData.m_PtrData != IntPtr.Zero)
                            m_MainController.ReleaseBuffer(fData);
                        continue;
                    }

                    switch (fData.m_nMessageID)
                    {
                        case SampleData.SAMPLE_SINGLEGROUP_ANALOGDATA:
                        case SampleData.SAMPLE_ANALOG_DATA:
                            PickTeamData(fData, out List<DataChangedEventArgs> lstevent);
                            foreach (var item in lstevent)
                            {
                                item.DataChangeSignal.FireDataEvent(item);
                            }
                            break;
                    }

                    if (fData.m_PtrData != IntPtr.Zero)
                        m_MainController.ReleaseBuffer(fData);
                }
            }
            catch (Exception ex)
            {
                Trace.Write("PickShowAndProcessData " + ex.Message);
            }
        }

        protected void DealMachineData(object sender, ElapsedEventArgs es)
        {
            m_bSample = true;
            if (m_bSample && Interlocked.CompareExchange(ref m_LockValue, 1, 0) == 0)
            {
                try
                {
                    m_bProcess = true;
                    AnalysisData(m_AnalysisDataQueue);
                    m_bProcess = false;
                }
                catch (Exception e)
                {
                    m_bProcess = false;
                    Trace.Write("DealMachineData " + e.Message + e.StackTrace);
                }

                Interlocked.CompareExchange(ref m_LockValue, 0, 1);
            }
        }

        protected void AnalysisData(object obj)
        {
            SourceDataQueue analysisqueue = (SourceDataQueue)obj;
            List<SampleData> lstData = analysisqueue.GetAllData();
            PickShowAndProcessData(lstData, m_bMaxMemory);
        }

        public bool PickTeamData(SampleData fData, out List<DataChangedEventArgs> lstevent)
        {
            lstevent = null;
            if (!m_MainController.m_DictDevice.TryGetValue(fData.m_nChannelGroupID, out Device device))
                return false;

            // fData.m_nMachineID 代表第几组
            lstevent = PickGroupData(device, fData.m_nTrigBlockIndex, fData.StartPos, fData.PerChnCount, fData.m_SampleData, fData.m_PtrData);
            return true;
        }

        protected List<DataChangedEventArgs> PickGroupData(Device device, int nInstantIndex, long nDataPos, int nPerChnCount, byte[] buffer, IntPtr ptr)
        {
            List<DataChangedEventArgs> lstevent = new List<DataChangedEventArgs>();

            DataProcessInfo info;
            info.instantIndex = nInstantIndex;
            info.buffer = buffer;
            info.ptr = ptr;
            info.srcMacOffsetBytes = 0;
            //
            info.oneDataAllBytes = device.m_lstHardChannel.Count * m_nDataByteSize;
            info.chnOffsetBytes = 0;

            info.dataPos = nDataPos;
            info.perChnCount = nPerChnCount;
            info.bSingleChn = false;
            info.sampleShowRate = 0;
            info.bNeedProcessSampleRate = true;

            List<List<DataChangedEventArgs>> lstEvent = new List<List<DataChangedEventArgs>>();
            List<Task> lsttask = new List<Task>();

            double minustime = Math.Abs((SampleData.m_lCallBackPos / device.CurrentSamplefreq - nDataPos / device.CurrentSamplefreq));
            //此处打印底层推送pos与已处理数据pos,判断是否来得及处理数据
            if (minustime > 5)
            {
                WriteLog($"底层回调事件推送pos:{SampleData.m_lCallBackPos}  时间:{SampleData.m_lCallBackPos / device.CurrentSamplefreq}  " +
                    $"处理底层数据pos:{nDataPos} 时间:{nDataPos / device.CurrentSamplefreq}  " +
                    $"时间差:{minustime}");
            }
            //此处判断每台仪器接收数据量是否连续
            if (device.m_lReceiveCount != nDataPos)
            {
                WriteLog($"仪器数据不连续 机号:{device.DeviceID}  处理pos:{device.m_lReceiveCount}  SampleDataPos:{nDataPos}");
            }
            info.dataPos = nDataPos;
            info.perChnCount = nPerChnCount;
            info.srcMacOffsetBytes = 0;
            PickMultiChnData(device, ref info, lstEvent, lsttask);

            device.m_lReceiveCount += nPerChnCount;

            if (lsttask.Count > 0)
            {
                if (!Task.WaitAll(lsttask.ToArray(), 10000))
                {
                    Utility.WriteLog("PickMultiChnData WaitAll TimeOut ");
                }
            }
            for (int i = 0; i < lstEvent.Count; i++)
                lstevent.AddRange(lstEvent[i]);

            return lstevent;
        }

        private void PickMultiChnData(Device device, ref DataProcessInfo info, List<List<DataChangedEventArgs>> lstEvent, List<Task> lsttask)
        {
            long nDataPos = info.dataPos;
            int nPerChnCount = info.perChnCount;
            int srcMacOffsetBytes = info.srcMacOffsetBytes;

            foreach (HardChannel algparam in device.m_lstHardChannel)
            {
                info.dataPos = nDataPos;
                info.perChnCount = nPerChnCount;
                info.srcMacOffsetBytes = srcMacOffsetBytes;

                info.chnOffsetBytes = algparam.m_nChannelID * m_nDataByteSize;

                if (info.perChnCount > 0)
                {
                    List<DataChangedEventArgs> lst = Process(algparam, info);
                    lstEvent.Add(lst);
                }
            }
        }

        public List<DataChangedEventArgs> Process(HardChannel hardchannel, DataProcessInfo info)
        {
            // 不需要指定数据
            NewProcessData(ref info, out List<UpdateData> lstData);
            return CreateDataEvent(hardchannel, lstData);
        }

        /// <summary>
        /// 创建数据事件
        /// </summary>
        /// <param name="dataPos">位置</param>
        /// <param name="perChnCount">数据量</param>
        /// <param name="lstvalidbuffer"></param>
        /// <returns></returns>
        protected List<DataChangedEventArgs> CreateDataEvent(HardChannel m_TimeSignal, int instantIndex, long dataPos, int perChnCount, float[] lstvalidbuffer, bool bNeedPhase = true)
        {
            List<DataChangedEventArgs> lstEvent = new List<DataChangedEventArgs>();

            DataChangedEventArgs dataevent = new DataChangedEventArgs(m_TimeSignal);
            lstEvent.Add(dataevent);

            if (lstvalidbuffer != null && lstvalidbuffer.Length != 0)
            {
                UpdateData data = new UpdateData(lstvalidbuffer);
                data.InstantIndex = instantIndex;
                data.Pos = dataPos;
                data.PerChnCount = perChnCount;

                dataevent.UpdateDatas = new List<UpdateData> { data };
            }
            return lstEvent;
        }

        protected List<DataChangedEventArgs> CreateDataEvent(HardChannel hardchannel, List<UpdateData> lstData)
        {
            List<DataChangedEventArgs> lstEvent = new List<DataChangedEventArgs>();

            for (int i = 0; i < lstData.Count; i++)
            {
                List<DataChangedEventArgs> tmp = CreateDataEvent(hardchannel, lstData[i].InstantIndex, lstData[i].Pos, lstData[i].PerChnCount, lstData[i].Data, i == 0);
                lstEvent.AddRange(tmp);
            }

            return lstEvent;
        }

        public bool NewProcessData(ref DataProcessInfo info, out List<UpdateData> lstData)
        {
            lstData = new List<UpdateData>();

            float[] lstvalidbuffer = ProcessStoreData.PickDataToFloat(info.perChnCount, info.ptr, info.buffer, info.srcMacOffsetBytes, info.oneDataAllBytes, info.chnOffsetBytes);

            UpdateData data = new UpdateData(lstvalidbuffer);
            data.InstantIndex = info.instantIndex;
            data.Pos = info.dataPos;
            data.PerChnCount = info.perChnCount;
            lstData.Add(data);

            ProcessData(info.perChnCount, lstvalidbuffer, 0, lstvalidbuffer, 0);
            return true;
        }

        public void ProcessData(int nDataCount, float[] lstSourceData, int srcOffset, float[] lstDesData, int desOffset = 0)
        {
            //该参数都需要从通道获取
            //Demo仪器系数
            //double m_fltADtoEUCoef = 0.3051850947599719;
            //千兆 5921系数
            //double m_fltADtoEUCoef = 0.00119209303761637660;
            //4G Analog
            double m_fltADtoEUCoef = 0.0030518509475997192;

            double elasticitycoief = 1;
            double m_fltIntegralUnitCoief = 1;
            double fltCoief = m_fltADtoEUCoef * elasticitycoief * m_fltIntegralUnitCoief;
            for (int i = 0; i < nDataCount; i++)
            {
                lstDesData[desOffset++] = (float)(lstSourceData[srcOffset++] * fltCoief);
            }
        }

        internal void WriteLog(string str)
        {
            Utility.WriteLog(str);
            Trace.WriteLine(str);
        }
    }
}
