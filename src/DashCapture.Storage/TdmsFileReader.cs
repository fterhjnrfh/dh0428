using System.Globalization;
using System.Text;
using DashCapture.Native;

namespace DashCapture.Storage;

public sealed class TdmsFileReader : IDisposable
{
    private const int ChunkSamples = 1_048_576;
    private readonly List<TdmsSegment> _segments;

    private TdmsFileReader(string path, TdmsFileInfo fileInfo, List<TdmsSegment> segments)
    {
        Path = path;
        FileInfo = fileInfo;
        _segments = segments;
    }

    public string Path { get; }
    public TdmsFileInfo FileInfo { get; }

    public static TdmsFileReader Open(string path, string tdmRuntimeDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("TDMS path is required.", nameof(path));
        }

        path = System.IO.Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            return OpenFolder(path, tdmRuntimeDir);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("TDMS file was not found.", path);
        }

        NativeBootstrap.AddSearchDirectory(tdmRuntimeDir);
        TdmsSegment segment = OpenSegment(path);
        return new TdmsFileReader(path, segment.FileInfo, new List<TdmsSegment> { segment });
    }

    private static TdmsFileReader OpenFolder(string path, string tdmRuntimeDir)
    {
        string[] files = Directory
            .EnumerateFiles(path, "*.tdms", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new FileNotFoundException("TDMS folder does not contain any .tdms files.", path);
        }

        NativeBootstrap.AddSearchDirectory(tdmRuntimeDir);
        var segments = new List<TdmsSegment>(files.Length);
        try
        {
            foreach (string file in files)
            {
                segments.Add(OpenSegment(file));
            }

            return new TdmsFileReader(path, AggregateInfo(path, segments), segments);
        }
        catch
        {
            foreach (TdmsSegment segment in segments)
            {
                segment.Dispose();
            }

            throw;
        }
    }

    private static TdmsSegment OpenSegment(string path)
    {
        TdmNative.ThrowIfError(
            TdmNative.DDC_OpenFileEx(path, TdmNative.TdmsFileType, readOnly: 1, out IntPtr file),
            "DDC_OpenFileEx");

        try
        {
            Dictionary<TdmsChannelKey, IntPtr> handles = new();
            List<TdmsGroupInfo> groups = ReadGroups(file, handles);
            return new TdmsSegment(path, new TdmsFileInfo(path, groups), file, handles);
        }
        catch
        {
            TdmNative.DDC_CloseFile(file);
            throw;
        }
    }

    public TdmsChannelEnvelope ReadEnvelope(TdmsChannelInfo channel, ulong startSample, ulong sampleCount, int bucketCount, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (startSample >= channel.SampleCount)
        {
            return new TdmsChannelEnvelope(channel, startSample, 0, Array.Empty<TdmsEnvelopePoint>());
        }

        sampleCount = Math.Min(sampleCount, channel.SampleCount - startSample);
        if (sampleCount == 0)
        {
            return new TdmsChannelEnvelope(channel, startSample, 0, Array.Empty<TdmsEnvelopePoint>());
        }

        int buckets = (int)Math.Min((ulong)Math.Max(1, bucketCount), sampleCount);
        var points = new TdmsEnvelopePoint[buckets];
        var buffer = new float[(int)Math.Min((ulong)ChunkSamples, sampleCount)];

        for (int bucket = 0; bucket < buckets; bucket++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ulong relativeStart = Scale(bucket, sampleCount, buckets);
            ulong relativeEnd = Scale(bucket + 1, sampleCount, buckets);
            if (relativeEnd <= relativeStart)
            {
                relativeEnd = relativeStart + 1;
            }

            ReadBucketAcrossSegments(channel.Key, startSample + relativeStart, relativeEnd - relativeStart, buffer, out float first, out float last, out float min, out float max, cancellationToken);
            points[bucket] = new TdmsEnvelopePoint(bucket, first, last, min, max);
        }

        return new TdmsChannelEnvelope(channel, startSample, sampleCount, points);
    }

    public void Dispose()
    {
        foreach (TdmsSegment segment in _segments)
        {
            segment.Dispose();
        }

        _segments.Clear();
    }

    private static TdmsFileInfo AggregateInfo(string path, IReadOnlyList<TdmsSegment> segments)
    {
        var groups = new List<TdmsGroupInfo>();
        foreach (IGrouping<string, TdmsGroupInfo> groupSet in segments
            .SelectMany(segment => segment.FileInfo.Groups)
            .GroupBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().DeviceId))
        {
            TdmsGroupInfo firstGroup = groupSet.First();
            var channels = groupSet
                .SelectMany(group => group.Channels)
                .GroupBy(channel => channel.Key)
                .Select(channelSet =>
                {
                    TdmsChannelInfo first = channelSet.First();
                    ulong sampleCount = channelSet.Aggregate(0UL, (sum, channel) => sum + channel.SampleCount);
                    return first with { SampleCount = sampleCount };
                })
                .OrderBy(channel => channel.LocalDataIndex)
                .ThenBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            groups.Add(firstGroup with { Channels = channels });
        }

        return new TdmsFileInfo(path, groups);
    }

    private static List<TdmsGroupInfo> ReadGroups(IntPtr file, Dictionary<TdmsChannelKey, IntPtr> handles)
    {
        TdmNative.ThrowIfError(TdmNative.DDC_GetNumChannelGroups(file, out uint groupCount), "DDC_GetNumChannelGroups");
        var groupHandles = new IntPtr[groupCount];
        TdmNative.ThrowIfError(TdmNative.DDC_GetChannelGroups(file, groupHandles, (UIntPtr)groupHandles.Length), "DDC_GetChannelGroups");

        var groups = new List<TdmsGroupInfo>(groupHandles.Length);
        for (int groupIndex = 0; groupIndex < groupHandles.Length; groupIndex++)
        {
            IntPtr groupHandle = groupHandles[groupIndex];
            string groupName = ReadString(groupHandle, isGroup: true, "name");
            string description = ReadString(groupHandle, isGroup: true, "description");
            int deviceId = ParseIntProperty(description, "DeviceId", ParseDeviceId(groupName, groupIndex));
            double sampleRate = ParseDoubleProperty(description, "SampleRate", 1);

            TdmNative.ThrowIfError(TdmNative.DDC_GetNumChannels(groupHandle, out uint channelCount), "DDC_GetNumChannels");
            var channelHandles = new IntPtr[channelCount];
            TdmNative.ThrowIfError(TdmNative.DDC_GetChannels(groupHandle, channelHandles, (UIntPtr)channelHandles.Length), "DDC_GetChannels");

            var channels = new List<TdmsChannelInfo>(channelHandles.Length);
            for (int channelIndex = 0; channelIndex < channelHandles.Length; channelIndex++)
            {
                IntPtr channelHandle = channelHandles[channelIndex];
                string channelName = ReadString(channelHandle, isGroup: false, "name");
                string channelDescription = ReadString(channelHandle, isGroup: false, "description");
                string unit = ReadString(channelHandle, isGroup: false, "unit_string");
                TdmNative.ThrowIfError(TdmNative.DDC_GetNumDataValues(channelHandle, out ulong values), "DDC_GetNumDataValues");

                int fallbackChannelId = ParseChannelId(channelName, channelIndex);
                int channelId = ParseIntProperty(channelDescription, "ChannelId", fallbackChannelId);
                int dataIndex = ParseIntProperty(channelDescription, "DataIndex", channelIndex);
                int localDataIndex = ParseIntProperty(channelDescription, "LocalDataIndex", channelIndex);
                var key = new TdmsChannelKey(groupName, channelName);

                handles[key] = channelHandle;
                channels.Add(new TdmsChannelInfo(
                    key,
                    deviceId,
                    groupName,
                    channelName,
                    channelDescription,
                    string.IsNullOrWhiteSpace(unit) ? "raw" : unit,
                    channelId,
                    dataIndex,
                    localDataIndex,
                    values,
                    sampleRate));
            }

            channels.Sort((left, right) =>
            {
                int indexCompare = left.LocalDataIndex.CompareTo(right.LocalDataIndex);
                return indexCompare != 0 ? indexCompare : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            });
            groups.Add(new TdmsGroupInfo(deviceId, groupName, description, sampleRate, channels));
        }

        groups.Sort((left, right) => left.DeviceId.CompareTo(right.DeviceId));
        return groups;
    }

    private static void ReadBucket(
        IntPtr handle,
        ulong startSample,
        ulong sampleCount,
        float[] buffer,
        out float first,
        out float last,
        out float min,
        out float max,
        CancellationToken cancellationToken)
    {
        first = 0;
        last = 0;
        min = float.MaxValue;
        max = float.MinValue;
        bool hasValue = false;
        ulong offset = 0;

        while (offset < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min((ulong)buffer.Length, sampleCount - offset);
            TdmNative.ThrowIfError(
                TdmNative.DDC_GetDataValuesFloat(handle, (UIntPtr)(startSample + offset), (UIntPtr)count, buffer),
                "DDC_GetDataValuesFloat");

            for (int i = 0; i < count; i++)
            {
                float value = buffer[i];
                if (!hasValue)
                {
                    first = value;
                    hasValue = true;
                }

                last = value;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            offset += (ulong)count;
        }

        if (!hasValue)
        {
            min = 0;
            max = 0;
        }
    }

    private void ReadBucketAcrossSegments(
        TdmsChannelKey key,
        ulong startSample,
        ulong sampleCount,
        float[] buffer,
        out float first,
        out float last,
        out float min,
        out float max,
        CancellationToken cancellationToken)
    {
        first = 0;
        last = 0;
        min = float.MaxValue;
        max = float.MinValue;
        bool hasValue = false;
        ulong remainingStart = startSample;
        ulong remainingCount = sampleCount;

        foreach (TdmsSegment segment in _segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!segment.ChannelHandles.TryGetValue(key, out IntPtr handle) ||
                !segment.Channels.TryGetValue(key, out TdmsChannelInfo? segmentChannel))
            {
                continue;
            }

            if (remainingStart >= segmentChannel.SampleCount)
            {
                remainingStart -= segmentChannel.SampleCount;
                continue;
            }

            ulong segmentStart = remainingStart;
            ulong segmentCount = Math.Min(remainingCount, segmentChannel.SampleCount - segmentStart);
            ReadBucket(handle, segmentStart, segmentCount, buffer, out float partFirst, out float partLast, out float partMin, out float partMax, cancellationToken);

            if (!hasValue)
            {
                first = partFirst;
                hasValue = true;
            }

            last = partLast;
            if (partMin < min) min = partMin;
            if (partMax > max) max = partMax;

            remainingCount -= segmentCount;
            remainingStart = 0;
            if (remainingCount == 0)
            {
                break;
            }
        }

        if (!hasValue)
        {
            min = 0;
            max = 0;
        }
    }

    private static ulong Scale(int index, ulong sampleCount, int buckets)
    {
        return (ulong)Math.Floor((decimal)index * sampleCount / buckets);
    }

    private static string ReadString(IntPtr handle, bool isGroup, string property)
    {
        uint length;
        int lengthResult = isGroup
            ? TdmNative.DDC_GetChannelGroupStringPropertyLength(handle, property, out length)
            : TdmNative.DDC_GetChannelStringPropertyLength(handle, property, out length);

        if (lengthResult < 0 || length == 0)
        {
            return string.Empty;
        }

        var value = new StringBuilder((int)length + 1);
        int valueResult = isGroup
            ? TdmNative.DDC_GetChannelGroupPropertyString(handle, property, value, (UIntPtr)(length + 1))
            : TdmNative.DDC_GetChannelPropertyString(handle, property, value, (UIntPtr)(length + 1));

        return valueResult < 0 ? string.Empty : value.ToString();
    }

    private static int ParseDeviceId(string groupName, int fallback)
    {
        const string prefix = "Device_";
        int start = groupName.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return fallback;
        }

        start += prefix.Length;
        int end = groupName.IndexOf('_', start);
        string text = end < 0 ? groupName[start..] : groupName[start..end];
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBasedId)
            ? oneBasedId - 1
            : fallback;
    }

    private static int ParseChannelId(string channelName, int fallback)
    {
        if (channelName.Length < 3 || !channelName.StartsWith("AI", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return int.TryParse(channelName[2..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBasedId)
            ? oneBasedId - 1
            : fallback;
    }

    private static int ParseIntProperty(string description, string key, int fallback)
    {
        string? value = ParseProperty(description, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }

    private static double ParseDoubleProperty(string description, string key, double fallback)
    {
        string? value = ParseProperty(description, key);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static string? ParseProperty(string description, string key)
    {
        string prefix = key + "=";
        int start = description.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        int end = description.IndexOf(';', start);
        return end < 0 ? description[start..].Trim() : description[start..end].Trim();
    }

    private void ThrowIfDisposed()
    {
        if (_segments.Count == 0)
        {
            throw new ObjectDisposedException(nameof(TdmsFileReader));
        }
    }

    private sealed class TdmsSegment : IDisposable
    {
        public TdmsSegment(string path, TdmsFileInfo fileInfo, IntPtr file, Dictionary<TdmsChannelKey, IntPtr> channelHandles)
        {
            Path = path;
            FileInfo = fileInfo;
            File = file;
            ChannelHandles = channelHandles;
            Channels = fileInfo.Groups
                .SelectMany(group => group.Channels)
                .ToDictionary(channel => channel.Key, channel => channel);
        }

        public string Path { get; }
        public TdmsFileInfo FileInfo { get; }
        public IntPtr File { get; private set; }
        public Dictionary<TdmsChannelKey, IntPtr> ChannelHandles { get; }
        public Dictionary<TdmsChannelKey, TdmsChannelInfo> Channels { get; }

        public void Dispose()
        {
            if (File != IntPtr.Zero)
            {
                TdmNative.DDC_CloseFile(File);
                File = IntPtr.Zero;
            }

            ChannelHandles.Clear();
            Channels.Clear();
        }
    }
}

public sealed record TdmsFileInfo(string Path, IReadOnlyList<TdmsGroupInfo> Groups)
{
    public int ChannelCount => Groups.Sum(group => group.Channels.Count);

    public ulong MaxSampleCount => Groups
        .SelectMany(group => group.Channels)
        .Select(channel => channel.SampleCount)
        .DefaultIfEmpty(0UL)
        .Max();
}

public sealed record TdmsGroupInfo(
    int DeviceId,
    string Name,
    string Description,
    double SampleRate,
    IReadOnlyList<TdmsChannelInfo> Channels);

public sealed record TdmsChannelInfo(
    TdmsChannelKey Key,
    int DeviceId,
    string GroupName,
    string Name,
    string Description,
    string Unit,
    int ChannelId,
    int DataIndex,
    int LocalDataIndex,
    ulong SampleCount,
    double SampleRate)
{
    public double DurationSeconds => SampleRate <= 0 ? 0 : SampleCount / SampleRate;

    public string DisplayName => $"{GroupName}/{Name}";
}

public readonly record struct TdmsChannelKey(string GroupName, string ChannelName);

public sealed record TdmsChannelEnvelope(
    TdmsChannelInfo Channel,
    ulong StartSample,
    ulong SampleCount,
    IReadOnlyList<TdmsEnvelopePoint> Points);

public readonly record struct TdmsEnvelopePoint(int Pixel, float First, float Last, float Minimum, float Maximum);
