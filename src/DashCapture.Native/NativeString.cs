using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DashCapture.Native;

public static class NativeString
{
    public static string FromAnsiBuffer(IntPtr pointer, int byteCount)
    {
        if (pointer == IntPtr.Zero || byteCount <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[byteCount];
        Marshal.Copy(pointer, bytes, 0, byteCount);
        int terminator = Array.IndexOf(bytes, (byte)0);
        int length = terminator >= 0 ? terminator : bytes.Length;
        return Encoding.Default.GetString(bytes, 0, length).Trim();
    }
}
