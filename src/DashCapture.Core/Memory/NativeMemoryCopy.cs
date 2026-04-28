using System;

namespace DashCapture.Core.Memory;

public static class NativeMemoryCopy
{
    public static unsafe void Copy(IntPtr source, IntPtr destination, int byteCount)
    {
        if (source == IntPtr.Zero) throw new ArgumentNullException(nameof(source));
        if (destination == IntPtr.Zero) throw new ArgumentNullException(nameof(destination));
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));

        Buffer.MemoryCopy(source.ToPointer(), destination.ToPointer(), byteCount, byteCount);
    }
}
