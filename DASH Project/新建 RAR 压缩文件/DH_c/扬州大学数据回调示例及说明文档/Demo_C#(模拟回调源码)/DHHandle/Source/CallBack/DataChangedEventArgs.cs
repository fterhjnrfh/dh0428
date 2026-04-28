using System;
using System.Collections.Generic;

namespace DHHandle
{
    public class UpdateData
    {
        /// <summary>
        /// 瞬态索引
        /// </summary>
        public int InstantIndex;
        /// <summary>
        /// 数据位置
        /// </summary>
        public long Pos;
        /// <summary>
        /// 数据量
        /// </summary>
        public int PerChnCount;
        /// <summary>
        /// 数据
        /// </summary>
        public float[] Data;
        /// <summary>
        /// 双精度数据
        /// </summary>
        public double[] DData;

        /// <summary>
        /// 已处理数据量
        /// </summary>
        public int processCount;

        public UpdateData(float[] data, double[] dData = null)
        {
            Data = data;
            DData = dData;
        }
    }

    /// <summary>
    /// 信号信息改变事件
    /// </summary>
    public class SignalEventArgs : EventArgs
    {
        /// <summary>
        /// 信息发生改变的信号
        /// </summary>
        public HardChannel DataChangeSignal { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="signal">信息发生改变的信号</param>
        public SignalEventArgs(HardChannel signal)
        {
            DataChangeSignal = signal;
        }
    }

    /// <summary>
	/// 数据发生改变事件
    /// </summary>
    public class DataChangedEventArgs : SignalEventArgs
    {
        /// <summary>
        /// 更新的数据
        /// </summary>
        public List<UpdateData> UpdateDatas { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="signal">数据发生改变的信号</param>
        public DataChangedEventArgs(HardChannel signal) : base(signal)
        {

        }
    }
}
