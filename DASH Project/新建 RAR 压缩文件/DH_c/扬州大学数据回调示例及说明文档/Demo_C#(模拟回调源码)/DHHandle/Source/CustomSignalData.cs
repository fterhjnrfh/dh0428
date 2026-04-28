using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace DHHandle
{
    // 计数器数据结构
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class PulsData
    {
        /// <summary>
        /// 旋转方向，0表示正向，1表示逆向
        /// </summary>
        public float nCircleDirection;
        /// <summary>
        /// 位置计数器（速度）
        /// </summary>
        public float nPosCounter;
        /// <summary>
        /// 脉冲计数器（累加）
        /// </summary>
        public float nPulseCounter;

        public PulsData()
        {
            nCircleDirection = 0;
            nPulseCounter = 0;
            nPosCounter = 0;
        }
    }

    /// <summary>
    /// CAN数据结构
    /// </summary>
    public struct Can_Data
    {
        public int nHeadSignature; // 标识 0x9C
        public int nType;          // 0 -- 标准帧， 1 - 扩展帧
        public int nStruct;            // 帧结构， 0 -- 数据帧，1 -- 远程帧
        public byte[] cID;            // 标识
        public int nDataCount;     // 数据长度
        /// <summary>
        /// 如果是常规can，则nDataCount固定为8，，如果是canFD，则pData大小是nDataCount
        /// </summary>
        public byte[] pData;

        public override string ToString()
        {
            string str = $"标识:{nHeadSignature} Type:{nType} 帧结构:{nStruct} cId:{cID[0].ToString("X2")} {cID[1].ToString("X2")} {cID[2].ToString("X2")} {cID[3].ToString("X2")} nDataCount:{nDataCount}" + Environment.NewLine;
            str += string.Join(" ", pData.Select(x => x.ToString("X2")));
            return str;
        }
    }

    public struct GpsData
    {
        /// <summary>
        /// GPS经度
        /// </summary>
        public float m_fltLong;
        /// <summary>
        /// GPS纬度
        /// </summary>
        public float m_fltLat;
        /// <summary>
        /// 可见卫星数
        /// </summary>
        public int m_nVisCount;
        /// <summary>
        /// 追踪卫星数
        /// </summary>
        public int m_nTraceCount;
        /// <summary>
        /// GPS速度
        /// </summary>
        public float m_fltSpeed;
        /// <summary>
        /// GPS高度
        /// </summary>
        public float m_fltHeight;

        public override string ToString()
        {
            return $"经度:{m_fltLong} 纬度:{m_fltLat} 可见卫星数:{m_nVisCount} 追踪卫星数:{m_nTraceCount} 速度:{m_fltSpeed} 高度:{m_fltHeight}";
        }
    }
}
