using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace DashCapture.Core.Memory;

public sealed class NativeSlabPool : IDisposable
{
    private readonly ConcurrentQueue<IntPtr> _free = new();
    private readonly int _slabSizeBytes;
    private int _disposed;

    public NativeSlabPool(int slabSizeBytes, int slabCount)
    {
        if (slabSizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(slabSizeBytes));
        if (slabCount <= 0) throw new ArgumentOutOfRangeException(nameof(slabCount));

        _slabSizeBytes = slabSizeBytes;
        for (int i = 0; i < slabCount; i++)
        {
            _free.Enqueue(Marshal.AllocHGlobal(_slabSizeBytes));
        }
    }

    public RentedNativeBuffer Rent(int minimumBytes)
    {
        ThrowIfDisposed();
        if (minimumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(minimumBytes));

        if (minimumBytes <= _slabSizeBytes && _free.TryDequeue(out IntPtr pooled))
        {
            return new RentedNativeBuffer(this, pooled, _slabSizeBytes, pooled: true);
        }

        return new RentedNativeBuffer(this, Marshal.AllocHGlobal(minimumBytes), minimumBytes, pooled: false);
    }

    internal void Return(RentedNativeBuffer buffer)
    {
        if (buffer.Pointer == IntPtr.Zero)
        {
            return;
        }

        if (_disposed != 0 || !buffer.Pooled)
        {
            Marshal.FreeHGlobal(buffer.Pointer);
            return;
        }

        _free.Enqueue(buffer.Pointer);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (_free.TryDequeue(out IntPtr pointer))
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed != 0) throw new ObjectDisposedException(nameof(NativeSlabPool));
    }
}

public readonly struct RentedNativeBuffer
{
    private readonly NativeSlabPool _owner;

    internal RentedNativeBuffer(NativeSlabPool owner, IntPtr pointer, int capacity, bool pooled)
    {
        _owner = owner;
        Pointer = pointer;
        Capacity = capacity;
        Pooled = pooled;
    }

    public IntPtr Pointer { get; }
    public int Capacity { get; }
    internal bool Pooled { get; }

    public void Return() => _owner.Return(this);
}
