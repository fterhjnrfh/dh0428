using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DHHandle
{
    public class StringInfoHelper
    {
        public static StringBuilder GetStringBuilder(int capacity = 1024)
        {
            StringBuilder sb = new StringBuilder();
            sb.Capacity = capacity;
            return sb;
        }

        public static StringBuilder GetStringBuilder(string xml)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(xml);
            return sb;
        }

        public static string GetStringFromInptr(IntPtr point, int length)
        {
            if (length == 0)
            {
                return "";
            }
            byte[] data = new byte[length];
            Marshal.Copy(point, data, 0, length);
            string str = GetStringFromBytes(data, length);
            data = null;
            return str;
        }

        public static string GetStringFromBytes(byte[] data, int length)
        {
            return Encoding.Default.GetString(data, 0, length);
        }

        public static IntPtr GetIntPtrFromString(string a, out int length)
        {
            if (string.IsNullOrEmpty(a))
            {
                a = "";
            }
            byte[] data = Encoding.UTF8.GetBytes(a);
            length = data.Length;
            IntPtr point = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, point, data.Length);
            data = null;
            return point;
        }

        public static IntPtr GetIntPtr(int capacity = 1024)
        {
            return Marshal.AllocHGlobal(capacity);
        }

        public static List<string> ParseString(string strSelectValue)
        {
            List<string> lstSelectValue = new List<string>();
            if (!string.IsNullOrEmpty(strSelectValue))
            {
                string[] lstSelect = strSelectValue.Split('|');
                if (lstSelect != null)
                {
                    for (int i = 0; i < lstSelect.Length; i++)
                    {
                        lstSelectValue.Add(lstSelect[i]);
                    }
                }
            }
            return lstSelectValue;
        }

        public static int GetStringLength(string str)
        {
            return Encoding.Default.GetByteCount(str);
        }
    }
}
