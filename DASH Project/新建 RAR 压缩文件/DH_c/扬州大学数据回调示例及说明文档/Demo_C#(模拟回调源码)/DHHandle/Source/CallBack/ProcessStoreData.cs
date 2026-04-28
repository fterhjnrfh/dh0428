using System;

namespace DHHandle
{
    /// <summary>
    /// 源数数处理
    /// </summary>
    public class ProcessStoreData
    {
        /// <summary>
        /// short 类型
        /// </summary>
        const int DATA_TYPE_SHORT = 0;
        /// <summary>
        /// float类型
        /// </summary>
        const int DATA_TYPE_FLOAT = 1;
        /// <summary>
        /// int类型
        /// </summary>
        const int DATA_TYPE_INT = 2;
        /// <summary>
        /// 24位int类型
        /// </summary>
        const int DATA_TYPE_24BIT = 3;
        /// <summary>
        ///  1位
        /// </summary>
        const int DATA_TYPE_CHAR = 4;
        /// <summary>
        /// double类型
        /// </summary>
        const int DATA_TYPE_DOUBLE = 5;

        /// <summary>
        /// 从基地址某偏移处填充给定大小的字节数组
        /// </summary>
        /// <param name="source">基地址</param>
        /// <param name="desOffset">偏移量</param>
        /// <param name="destination">目标数组</param>
        public static void ReadBytesFromPointer(IntPtr source, int sourceOffset, byte[] destination, int desOffset, int length)
        {
            unsafe
            {
                byte* pSource = (byte*)source.ToPointer();
                pSource += sourceOffset;

                int nIndex = 0;
                int nCount = desOffset + length;
                for (int i = desOffset; i < nCount; i++)
                {
                    destination[i] = pSource[nIndex++];
                }
            }
        }

        public static unsafe float[] PickDataToFloat(int perChnCount, IntPtr ptr, byte[] source, int srcOffset, int oneDataAllchnBytes, int chnBytesOffset)
        {
            if (perChnCount <= 0)
            {
                return new float[0];
            }

            float[] des = null;
            if (ptr != IntPtr.Zero)
            {
                byte* pData = (byte*)ptr.ToPointer();
                pData += srcOffset + chnBytesOffset;

                des = PickDataToFloat(oneDataAllchnBytes, perChnCount, pData);
            }
            else
            {
                fixed (byte* pTmp = source)
                {
                    byte* pData = pTmp + srcOffset + chnBytesOffset;

                    des = PickDataToFloat(oneDataAllchnBytes, perChnCount, pData);
                }
            }
            return des;
        }

        protected static unsafe float[] PickDataToFloat(int oneDataAllchnBytes, int perChnCount, byte* pData)
        {
            float[] des = new float[perChnCount];
            //Demo
            //for (int j = 0; j < perChnCount; j++)
            //{
            //    des[j] = ((float*)pData)[0];
            //    pData += oneDataAllchnBytes;
            //}

            //千兆 5921
            //for (int j = 0; j < perChnCount; j++)
            //{
            //    des[j] = ((int*)pData)[0];
            //    pData += oneDataAllchnBytes;
            //}

            //4G Analog
            for (int j = 0; j < perChnCount; j++)
            {
                des[j] = ((float*)pData)[0];
                pData += oneDataAllchnBytes;
            }
            return des;
        }
    }
}
