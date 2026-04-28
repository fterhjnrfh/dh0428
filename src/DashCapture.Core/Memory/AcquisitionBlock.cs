using System;
using System.Threading;
using DashCapture.Core.Models;

namespace DashCapture.Core.Memory;

public sealed class AcquisitionBlock
{
    private readonly RentedNativeBuffer _buffer;
    private int _referenceCount;

    public AcquisitionBlock(RentedNativeBuffer buffer, SdkSampleData header, int channelCount)
    {
        _buffer = buffer;
        Header = header;
        Length = header.BufferCount;
        ChannelCount = channelCount;
        CreatedAt = DateTimeOffset.UtcNow;
        _referenceCount = 1;
    }

    public SdkSampleData Header { get; }
    public IntPtr DataPointer => _buffer.Pointer;
    public int Length { get; }
    public int ChannelCount { get; }
    public DateTimeOffset CreatedAt { get; }

    public void Retain()
    {
        int current;
        do
        {
            current = Volatile.Read(ref _referenceCount);
            if (current <= 0) throw new ObjectDisposedException(nameof(AcquisitionBlock));
        }
        while (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) != current);
    }

    public void Release()
    {
        int remaining = Interlocked.Decrement(ref _referenceCount);
        if (remaining == 0)
        {
            _buffer.Return();
        }
        else if (remaining < 0)
        {
            throw new ObjectDisposedException(nameof(AcquisitionBlock));
        }
    }
}
