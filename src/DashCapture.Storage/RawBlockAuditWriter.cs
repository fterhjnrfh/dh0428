using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DashCapture.Core.Memory;

namespace DashCapture.Storage;

internal sealed class RawBlockAuditWriter : IDisposable
{
    private readonly StreamWriter _writer;

    public RawBlockAuditWriter(string tdmsPath)
    {
        CurrentPath = Path.ChangeExtension(tdmsPath, ".raw.csv");
        _writer = new StreamWriter(CurrentPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 64 * 1024);
        _writer.WriteLine("utc,block_index,message_type,group_id,machine_id,total_data_count,data_count_per_channel,buffer_count,channel_count,layout,crc32,group_info");
    }

    public string CurrentPath { get; }

    public void Write(AcquisitionBlock block)
    {
        uint crc = Crc32.Compute(block.DataPointer, block.Length);
        _writer.Write(block.CreatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.BlockIndex.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.MessageType.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.GroupId.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.MachineId.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.TotalDataCount.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.DataCountPerChannel.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.BufferCount.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.ChannelCount.ToString(CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.Write(block.Header.Layout.ToString());
        _writer.Write(',');
        _writer.Write(crc.ToString("X8", CultureInfo.InvariantCulture));
        _writer.Write(',');
        _writer.WriteLine(Escape(block.Header.GroupInfo));
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return 0;
        }

        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc = Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return ~crc;
    }

    public static uint Compute(ReadOnlySpan<float> data)
    {
        return Compute(MemoryMarshal.AsBytes(data));
    }

    public static unsafe uint Compute(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return 0;
        }

        uint crc = 0xFFFFFFFF;
        byte* data = (byte*)pointer.ToPointer();
        for (int i = 0; i < length; i++)
        {
            crc = Table[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
