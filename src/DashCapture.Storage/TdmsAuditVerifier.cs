using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DashCapture.Core.Models;
using DashCapture.Native;

namespace DashCapture.Storage;

public static class TdmsAuditVerifier
{
    public static TdmsAuditVerificationResult Verify(string tdmsPath, string? auditCsvPath, string tdmRuntimeDir)
    {
        if (string.IsNullOrWhiteSpace(tdmsPath))
        {
            throw new ArgumentException("TDMS path is required.", nameof(tdmsPath));
        }

        tdmsPath = Path.GetFullPath(tdmsPath);
        auditCsvPath = string.IsNullOrWhiteSpace(auditCsvPath)
            ? Path.ChangeExtension(tdmsPath, ".raw.csv")
            : Path.GetFullPath(auditCsvPath);

        if (!File.Exists(tdmsPath))
        {
            throw new FileNotFoundException("TDMS file was not found.", tdmsPath);
        }

        if (!File.Exists(auditCsvPath))
        {
            throw new FileNotFoundException("Raw audit CSV was not found.", auditCsvPath);
        }

        NativeBootstrap.AddSearchDirectory(tdmRuntimeDir);

        List<RawBlockAuditRecord> records = ReadAudit(auditCsvPath);
        var firstPositionByGroup = records
            .GroupBy(r => r.GroupId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.TotalDataCount));

        IntPtr file = IntPtr.Zero;
        var mismatches = new List<TdmsAuditMismatch>();
        int checkedBlocks = 0;
        long checkedBytes = 0;

        TdmNative.ThrowIfError(
            TdmNative.DDC_OpenFileEx(tdmsPath, TdmNative.TdmsFileType, readOnly: 1, out file),
            "DDC_OpenFileEx");

        try
        {
            Dictionary<int, TdmsGroupReader> groups = ReadGroups(file);
            foreach (RawBlockAuditRecord record in records)
            {
                bool globalRecord = record.MessageType == DashSampleMessageType.AnalogMultiChannelData ||
                                    record.GroupId < 0 ||
                                    record.MachineId < 0;

                if (globalRecord)
                {
                    if (!firstPositionByGroup.TryGetValue(record.GroupId, out long globalFirstPosition))
                    {
                        globalFirstPosition = 0;
                    }

                    long globalLocalStart = record.TotalDataCount - globalFirstPosition;
                    if (globalLocalStart < 0)
                    {
                        mismatches.Add(new TdmsAuditMismatch(
                            record.BlockIndex,
                            record.GroupId,
                            record.TotalDataCount,
                            record.Crc32,
                            string.Empty,
                            "Invalid local TDMS offset."));
                        continue;
                    }

                    uint globalActual = ReconstructGlobalBlockCrc(groups.Values, (ulong)globalLocalStart, record.DataCountPerChannel, record.Layout);
                    string globalActualText = globalActual.ToString("X8", CultureInfo.InvariantCulture);
                    checkedBlocks++;
                    checkedBytes += record.BufferCount;

                    if (!string.Equals(globalActualText, record.Crc32, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add(new TdmsAuditMismatch(
                            record.BlockIndex,
                            record.GroupId,
                            record.TotalDataCount,
                            record.Crc32,
                            globalActualText,
                            "Reconstructed global TDMS block CRC does not match SDK raw block CRC."));
                    }

                    continue;
                }

                if (!groups.TryGetValue(record.GroupId, out TdmsGroupReader? group))
                {
                    mismatches.Add(new TdmsAuditMismatch(
                        record.BlockIndex,
                        record.GroupId,
                        record.TotalDataCount,
                        record.Crc32,
                        string.Empty,
                        "TDMS group is missing."));
                    continue;
                }

                if (!firstPositionByGroup.TryGetValue(record.GroupId, out long firstPosition))
                {
                    firstPosition = 0;
                }

                long localStart = record.TotalDataCount - firstPosition;
                if (localStart < 0)
                {
                    mismatches.Add(new TdmsAuditMismatch(
                        record.BlockIndex,
                        record.GroupId,
                        record.TotalDataCount,
                        record.Crc32,
                        string.Empty,
                        "Invalid local TDMS offset."));
                    continue;
                }

                uint actual = ReconstructBlockCrc(group, (ulong)localStart, record.DataCountPerChannel, record.Layout);
                string actualText = actual.ToString("X8", CultureInfo.InvariantCulture);
                checkedBlocks++;
                checkedBytes += record.BufferCount;

                if (!string.Equals(actualText, record.Crc32, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(new TdmsAuditMismatch(
                        record.BlockIndex,
                        record.GroupId,
                        record.TotalDataCount,
                        record.Crc32,
                        actualText,
                        "Reconstructed TDMS block CRC does not match SDK raw block CRC."));
                }
            }
        }
        finally
        {
            if (file != IntPtr.Zero)
            {
                TdmNative.DDC_CloseFile(file);
            }
        }

        return new TdmsAuditVerificationResult(tdmsPath, auditCsvPath, checkedBlocks, checkedBytes, mismatches);
    }

    private static Dictionary<int, TdmsGroupReader> ReadGroups(IntPtr file)
    {
        TdmNative.ThrowIfError(TdmNative.DDC_GetNumChannelGroups(file, out uint groupCount), "DDC_GetNumChannelGroups");
        var groupHandles = new IntPtr[groupCount];
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelGroups(file, groupHandles, (UIntPtr)groupHandles.Length), "DDC_GetChannelGroups");

        var result = new Dictionary<int, TdmsGroupReader>();
        foreach (IntPtr groupHandle in groupHandles)
        {
            string groupName = ReadGroupString(groupHandle, "name");
            int deviceId = ParseDeviceId(groupName);
            if (deviceId < 0)
            {
                continue;
            }

            TdmNative.ThrowIfError(TdmNative.DDC_GetNumChannels(groupHandle, out uint channelCount), "DDC_GetNumChannels");
            var channelHandles = new IntPtr[channelCount];
            TdmNative.ThrowIfError(TdmNative.DDC_GetChannels(groupHandle, channelHandles, (UIntPtr)channelHandles.Length), "DDC_GetChannels");

            var channels = new List<TdmsChannelReader>(channelHandles.Length);
            for (int i = 0; i < channelHandles.Length; i++)
            {
                string name = ReadChannelString(channelHandles[i], "name");
                string description = ReadChannelString(channelHandles[i], "description");
                int fallbackIndex = ParseChannelId(name);
                int dataIndex = ParseDescriptionIndex(description, "DataIndex", fallbackIndex);
                int localDataIndex = ParseDescriptionIndex(description, "LocalDataIndex", fallbackIndex);

                channels.Add(new TdmsChannelReader(channelHandles[i], name, dataIndex, localDataIndex));
            }

            result[deviceId] = new TdmsGroupReader(groupName, channels);
        }

        return result;
    }

    private static uint ReconstructGlobalBlockCrc(IEnumerable<TdmsGroupReader> groups, ulong localStart, int sampleCount, SampleDataLayout layout)
    {
        TdmsChannelReader[] channels = groups
            .SelectMany(group => group.Channels)
            .OrderBy(channel => channel.DataIndex)
            .ThenBy(channel => channel.Name, StringComparer.Ordinal)
            .ToArray();

        return ReconstructChannelsCrc(channels, localStart, sampleCount, useChannelContiguous: layout == SampleDataLayout.ChannelContiguousFloat32);
    }

    private static uint ReconstructBlockCrc(TdmsGroupReader group, ulong localStart, int sampleCount, SampleDataLayout layout)
    {
        if (sampleCount <= 0 || group.Channels.Count == 0)
        {
            return 0;
        }

        if (layout == SampleDataLayout.ChannelContiguousFloat32)
        {
            TdmsChannelReader[] ordered = group.Channels
                .OrderBy(c => c.LocalDataIndex)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToArray();

            return ReconstructChannelsCrc(ordered, localStart, sampleCount, useChannelContiguous: true);
        }

        TdmsChannelReader[] interleavedOrder = group.Channels
            .OrderBy(c => c.DataIndex)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        return ReconstructChannelsCrc(interleavedOrder, localStart, sampleCount, useChannelContiguous: false);
    }

    private static uint ReconstructChannelsCrc(
        IReadOnlyList<TdmsChannelReader> channels,
        ulong localStart,
        int sampleCount,
        bool useChannelContiguous)
    {
        if (sampleCount <= 0 || channels.Count == 0)
        {
            return 0;
        }

        if (useChannelContiguous)
        {
            byte[] blockBytes = new byte[checked(sampleCount * channels.Count * sizeof(float))];
            float[] values = new float[sampleCount];
            for (int i = 0; i < channels.Count; i++)
            {
                ReadFloatValues(channels[i].Handle, localStart, values);
                ReadOnlySpan<byte> channelBytes = MemoryMarshal.AsBytes(values.AsSpan(0, sampleCount));
                channelBytes.CopyTo(blockBytes.AsSpan(i * sampleCount * sizeof(float)));
            }

            return Crc32.Compute(blockBytes);
        }

        float[] blockValues = new float[checked(sampleCount * channels.Count)];
        float[] scratch = new float[sampleCount];
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            ReadFloatValues(channels[channelIndex].Handle, localStart, scratch);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                blockValues[sampleIndex * channels.Count + channelIndex] = scratch[sampleIndex];
            }
        }

        return Crc32.Compute(blockValues);
    }

    private static void ReadFloatValues(IntPtr channel, ulong start, float[] values)
    {
        TdmNative.ThrowIfError(
            TdmNative.DDC_GetDataValuesFloat(channel, (UIntPtr)start, (UIntPtr)values.Length, values),
            "DDC_GetDataValuesFloat");
    }

    private static List<RawBlockAuditRecord> ReadAudit(string path)
    {
        var records = new List<RawBlockAuditRecord>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? header = reader.ReadLine();
        if (header is null)
        {
            return records;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = SplitCsv(line);
            if (fields.Length < 11)
            {
                continue;
            }

            records.Add(new RawBlockAuditRecord(
                ParseInt(fields[1]),
                ParseInt(fields[2]),
                ParseInt(fields[3]),
                ParseInt(fields[4]),
                ParseLong(fields[5]),
                ParseInt(fields[6]),
                ParseInt(fields[7]),
                ParseInt(fields[8]),
                Enum.TryParse(fields[9], ignoreCase: false, out SampleDataLayout layout) ? layout : SampleDataLayout.SampleInterleavedFloat32,
                fields[10].Trim()));
        }

        return records;
    }

    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string ReadGroupString(IntPtr group, string property)
    {
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelGroupStringPropertyLength(group, property, out uint length), "DDC_GetChannelGroupStringPropertyLength");
        if (length == 0)
        {
            return string.Empty;
        }

        var value = new StringBuilder((int)length + 1);
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelGroupPropertyString(group, property, value, (UIntPtr)(length + 1)), "DDC_GetChannelGroupPropertyString");
        return value.ToString();
    }

    private static string ReadChannelString(IntPtr channel, string property)
    {
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelStringPropertyLength(channel, property, out uint length), "DDC_GetChannelStringPropertyLength");
        if (length == 0)
        {
            return string.Empty;
        }

        var value = new StringBuilder((int)length + 1);
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelPropertyString(channel, property, value, (UIntPtr)(length + 1)), "DDC_GetChannelPropertyString");
        return value.ToString();
    }

    private static int ParseDeviceId(string groupName)
    {
        const string prefix = "Device_";
        int start = groupName.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return -1;
        }

        start += prefix.Length;
        int end = groupName.IndexOf('_', start);
        string text = end < 0 ? groupName[start..] : groupName[start..end];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBasedId)
            ? oneBasedId - 1
            : -1;
    }

    private static int ParseChannelId(string channelName)
    {
        if (channelName.Length < 3 || !channelName.StartsWith("AI", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(channelName[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBasedId)
            ? Math.Max(0, oneBasedId - 1)
            : 0;
    }

    private static int ParseDescriptionIndex(string description, string key, int fallback)
    {
        string prefix = key + "=";
        int start = description.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return fallback;
        }

        start += prefix.Length;
        int end = description.IndexOf(';', start);
        string text = end < 0 ? description[start..] : description[start..end];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static long ParseLong(string value) => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

public sealed record TdmsAuditVerificationResult(
    string TdmsPath,
    string AuditCsvPath,
    int CheckedBlocks,
    long CheckedBytes,
    IReadOnlyList<TdmsAuditMismatch> Mismatches)
{
    public bool Success => Mismatches.Count == 0;
}

public sealed record TdmsAuditMismatch(
    int BlockIndex,
    int GroupId,
    long TotalDataCount,
    string ExpectedCrc32,
    string ActualCrc32,
    string Reason);

internal sealed record RawBlockAuditRecord(
    int BlockIndex,
    int MessageType,
    int GroupId,
    int MachineId,
    long TotalDataCount,
    int DataCountPerChannel,
    int BufferCount,
    int ChannelCount,
    SampleDataLayout Layout,
    string Crc32);

internal sealed record TdmsGroupReader(string Name, IReadOnlyList<TdmsChannelReader> Channels);

internal sealed record TdmsChannelReader(IntPtr Handle, string Name, int DataIndex, int LocalDataIndex);
