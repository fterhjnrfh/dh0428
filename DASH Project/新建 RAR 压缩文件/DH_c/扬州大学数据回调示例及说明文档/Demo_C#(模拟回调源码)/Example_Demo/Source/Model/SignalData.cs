using DHHandle;

namespace Example_Demo.Source.Model
{
    /// <summary>
    /// 用于存储从设备获取的通道数据
    /// </summary>
    public class SignalData
    {
        /// <summary>
        /// 硬件通道信息
        /// </summary>
        public HardChannel HardChannel { get; set; }

        /// <summary>
        /// 数据位置/时间戳
        /// </summary>
        public long Position { get; set; }

        /// <summary>
        /// 通道数据
        /// </summary>
        public float[] Data { get; set; }
    }
}