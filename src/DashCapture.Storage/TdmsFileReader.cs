using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DashCapture.Native;

namespace DashCapture.Storage;

public sealed class TdmsFileReader : IDisposable
{
    private const int ChunkSamples = 1_048_576;
    private readonly List<IReadableSegment> _segments;

    private TdmsFileReader(string path, TdmsFileInfo fileInfo, List<IReadableSegment> segments)
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
        IReadableSegment segment = OpenSegment(path);
        return new TdmsFileReader(path, segment.FileInfo, new List<IReadableSegment> { segment });
    }

    private static TdmsFileReader OpenFolder(string path, string tdmRuntimeDir)
    {
        string[] files = DiscoverSegmentFiles(path);

        if (files.Length == 0)
        {
            throw new FileNotFoundException("Folder does not contain any .tdms, .tdms.dhc, or .dhcap files.", path);
        }

        NativeBootstrap.AddSearchDirectory(tdmRuntimeDir);
        var segments = new List<IReadableSegment>(files.Length);
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
            foreach (IReadableSegment segment in segments)
            {
                segment.Dispose();
            }

            throw;
        }
    }

    private static IReadableSegment OpenSegment(string path)
    {
        if (CompressedCaptureFormat.IsCompressedCaptureFile(path))
        {
            return CompressedCaptureSegment.Open(path);
        }

        MaterializedTdmsFile materialized = CompressedTdmsCodec.MaterializeForRead(path);
        IntPtr file = IntPtr.Zero;
        try
        {
            TdmNative.ThrowIfError(
                TdmNative.DDC_OpenFileEx(materialized.Path, TdmNative.TdmsFileType, readOnly: 1, out file),
                "DDC_OpenFileEx");
            Dictionary<TdmsChannelKey, IntPtr> handles = new();
            List<TdmsGroupInfo> groups = ReadGroups(file, handles);
            return new DdcTdmsSegment(path, new TdmsFileInfo(path, groups), file, handles, materialized);
        }
        catch
        {
            if (file != IntPtr.Zero)
            {
                TdmNative.DDC_CloseFile(file);
            }

            materialized.Dispose();
            throw;
        }
    }

    private static string[] DiscoverSegmentFiles(string path)
    {
        return Directory
            .EnumerateFiles(path, "*.tdms", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(path, "*.tdms" + CompressedTdmsCodec.Extension, SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(path, "*" + CompressedCaptureFormat.Extension, SearchOption.TopDirectoryOnly))
            .GroupBy(SegmentIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(file => CompressedTdmsCodec.IsCompressedFile(file) ? 1 : 0)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(file => SegmentIdentity(file), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string SegmentIdentity(string path)
    {
        return CompressedTdmsCodec.IsCompressedFile(path)
            ? path[..^CompressedTdmsCodec.Extension.Length]
            : CompressedCaptureFormat.IsCompressedCaptureFile(path)
                ? CompressedCaptureIdentity(path)
            : path;
    }

    private static string CompressedCaptureIdentity(string path)
    {
        return path.EndsWith(CompressedCaptureFormat.Extension, StringComparison.OrdinalIgnoreCase)
            ? path[..^CompressedCaptureFormat.Extension.Length]
            : path[..^".tdms".Length];
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
        var accumulators = new TdmsEnvelopeAccumulator[buckets];
        var buffer = new float[(int)Math.Min((ulong)ChunkSamples, sampleCount)];
        ulong remainingStart = startSample;
        ulong remainingCount = sampleCount;
        ulong outputOffset = 0;
        foreach (IReadableSegment segment in _segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!segment.Channels.TryGetValue(channel.Key, out TdmsChannelInfo? segmentChannel))
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
            segment.AccumulateEnvelope(
                channel.Key,
                segmentStart,
                segmentCount,
                outputOffset,
                sampleCount,
                accumulators,
                buffer,
                cancellationToken);

            remainingCount -= segmentCount;
            outputOffset += segmentCount;
            remainingStart = 0;
            if (remainingCount == 0)
            {
                break;
            }
        }

        TdmsEnvelopePoint[] points = accumulators
            .Select((accumulator, index) => accumulator.ToPoint(index))
            .ToArray();
        return new TdmsChannelEnvelope(channel, startSample, sampleCount, points);
    }

    public void ExportToTdms(string targetPath, CancellationToken cancellationToken, IProgress<TdmsExportProgress>? progress = null)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Export target path is required.", nameof(targetPath));
        }

        targetPath = System.IO.Path.GetFullPath(targetPath);
        string? directory = System.IO.Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = System.IO.Path.Combine(
            directory ?? Environment.CurrentDirectory,
            $"{System.IO.Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.tmp.tdms");
        File.Delete(tempPath);

        IntPtr file = IntPtr.Zero;
        try
        {
            TdmNative.ThrowIfError(
                TdmNative.DDC_CreateFile(
                    tempPath,
                    TdmNative.TdmsFileType,
                    System.IO.Path.GetFileNameWithoutExtension(targetPath),
                    $"Exported from DASH capture source. Source={Path}",
                    "DASH Capture Export",
                    Environment.UserName,
                    out file),
                "DDC_CreateFile");

            Dictionary<TdmsChannelKey, IntPtr> channelHandles = CreateExportStructure(file);
            ExportChannelData(channelHandles, cancellationToken, progress);

            TdmNative.ThrowIfError(TdmNative.DDC_SaveFile(file), "DDC_SaveFile");
            TdmNative.ThrowIfError(TdmNative.DDC_CloseFile(file), "DDC_CloseFile");
            file = IntPtr.Zero;

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
        }
        catch
        {
            if (file != IntPtr.Zero)
            {
                TdmNative.DDC_CloseFile(file);
            }

            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Export cleanup is best effort.
            }

            throw;
        }
    }

    public void Dispose()
    {
        foreach (IReadableSegment segment in _segments)
        {
            segment.Dispose();
        }

        _segments.Clear();
    }

    private static TdmsFileInfo AggregateInfo(string path, IReadOnlyList<IReadableSegment> segments)
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

    private Dictionary<TdmsChannelKey, IntPtr> CreateExportStructure(IntPtr file)
    {
        var channelHandles = new Dictionary<TdmsChannelKey, IntPtr>();
        foreach (TdmsGroupInfo group in FileInfo.Groups)
        {
            TdmNative.ThrowIfError(
                TdmNative.DDC_AddChannelGroup(file, group.Name, group.Description, out IntPtr groupHandle),
                "DDC_AddChannelGroup");

            foreach (TdmsChannelInfo channel in group.Channels)
            {
                TdmNative.ThrowIfError(
                    TdmNative.DDC_AddChannel(
                        groupHandle,
                        DdcDataType.Float,
                        channel.Name,
                        channel.Description,
                        string.IsNullOrWhiteSpace(channel.Unit) ? "raw" : channel.Unit,
                        out IntPtr channelHandle),
                    "DDC_AddChannel");
                channelHandles[channel.Key] = channelHandle;
            }
        }

        return channelHandles;
    }

    private void ExportChannelData(
        Dictionary<TdmsChannelKey, IntPtr> channelHandles,
        CancellationToken cancellationToken,
        IProgress<TdmsExportProgress>? progress)
    {
        float[] buffer = new float[ChunkSamples];
        int totalChannels = Math.Max(1, FileInfo.ChannelCount);
        int exportedChannels = 0;
        foreach (TdmsGroupInfo group in FileInfo.Groups)
        {
            foreach (TdmsChannelInfo channel in group.Channels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!channelHandles.TryGetValue(channel.Key, out IntPtr channelHandle))
                {
                    continue;
                }

                ulong exportedSamples = 0;
                progress?.Report(new TdmsExportProgress(channel.DisplayName, exportedChannels, totalChannels, exportedSamples, channel.SampleCount));
                foreach (IReadableSegment segment in _segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!segment.Channels.TryGetValue(channel.Key, out TdmsChannelInfo? segmentChannel))
                    {
                        continue;
                    }

                    ulong offset = 0;
                    while (offset < segmentChannel.SampleCount)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = (int)Math.Min((ulong)buffer.Length, segmentChannel.SampleCount - offset);
                        int read = segment.ReadSamples(channel.Key, offset, count, buffer, cancellationToken);
                        if (read <= 0)
                        {
                            throw new InvalidDataException($"No samples were read while exporting {channel.DisplayName}.");
                        }

                        AppendFloatValues(channelHandle, buffer, read);
                        offset += (ulong)read;
                        exportedSamples += (ulong)read;
                        progress?.Report(new TdmsExportProgress(channel.DisplayName, exportedChannels, totalChannels, exportedSamples, channel.SampleCount));
                    }
                }

                exportedChannels++;
                progress?.Report(new TdmsExportProgress(channel.DisplayName, exportedChannels, totalChannels, exportedSamples, channel.SampleCount));
            }
        }
    }

    private static unsafe void AppendFloatValues(IntPtr channel, float[] values, int count)
    {
        fixed (float* ptr = values)
        {
            TdmNative.ThrowIfError(
                TdmNative.DDC_AppendDataValuesFloat(channel, new IntPtr(ptr), (UIntPtr)count),
                "DDC_AppendDataValuesFloat");
        }
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

    private interface IReadableSegment : IDisposable
    {
        TdmsFileInfo FileInfo { get; }
        Dictionary<TdmsChannelKey, TdmsChannelInfo> Channels { get; }

        void AccumulateEnvelope(
            TdmsChannelKey key,
            ulong segmentStart,
            ulong segmentCount,
            ulong outputOffset,
            ulong totalSampleCount,
            TdmsEnvelopeAccumulator[] accumulators,
            float[] buffer,
            CancellationToken cancellationToken);

        int ReadSamples(
            TdmsChannelKey key,
            ulong segmentStart,
            int sampleCount,
            float[] buffer,
            CancellationToken cancellationToken);
    }

    private struct TdmsEnvelopeAccumulator
    {
        private bool _hasValue;
        private float _first;
        private float _last;
        private float _min;
        private float _max;

        public void Add(float value)
        {
            if (!_hasValue)
            {
                _first = value;
                _min = value;
                _max = value;
                _hasValue = true;
            }

            _last = value;
            if (value < _min) _min = value;
            if (value > _max) _max = value;
        }

        public void AddSummary(float first, float last, float min, float max)
        {
            if (!_hasValue)
            {
                _first = first;
                _min = min;
                _max = max;
                _hasValue = true;
            }

            _last = last;
            if (min < _min) _min = min;
            if (max > _max) _max = max;
        }

        public readonly TdmsEnvelopePoint ToPoint(int pixel)
        {
            return _hasValue
                ? new TdmsEnvelopePoint(pixel, _first, _last, _min, _max)
                : new TdmsEnvelopePoint(pixel, 0, 0, 0, 0);
        }
    }

    private sealed class DdcTdmsSegment : IReadableSegment
    {
        private readonly MaterializedTdmsFile _materialized;

        public DdcTdmsSegment(
            string path,
            TdmsFileInfo fileInfo,
            IntPtr file,
            Dictionary<TdmsChannelKey, IntPtr> channelHandles,
            MaterializedTdmsFile materialized)
        {
            Path = path;
            FileInfo = fileInfo;
            File = file;
            ChannelHandles = channelHandles;
            _materialized = materialized;
            Channels = fileInfo.Groups
                .SelectMany(group => group.Channels)
                .ToDictionary(channel => channel.Key, channel => channel);
        }

        public string Path { get; }
        public TdmsFileInfo FileInfo { get; }
        public IntPtr File { get; private set; }
        public Dictionary<TdmsChannelKey, IntPtr> ChannelHandles { get; }
        public Dictionary<TdmsChannelKey, TdmsChannelInfo> Channels { get; }

        public void AccumulateEnvelope(
            TdmsChannelKey key,
            ulong segmentStart,
            ulong segmentCount,
            ulong outputOffset,
            ulong totalSampleCount,
            TdmsEnvelopeAccumulator[] accumulators,
            float[] buffer,
            CancellationToken cancellationToken)
        {
            if (!ChannelHandles.TryGetValue(key, out IntPtr handle))
            {
                return;
            }

            ulong outputEnd = outputOffset + segmentCount;
            for (int bucket = 0; bucket < accumulators.Length; bucket++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong bucketStart = Scale(bucket, totalSampleCount, accumulators.Length);
                ulong bucketEnd = Scale(bucket + 1, totalSampleCount, accumulators.Length);
                if (bucketEnd <= bucketStart)
                {
                    bucketEnd = bucketStart + 1;
                }

                ulong overlapStart = Math.Max(bucketStart, outputOffset);
                ulong overlapEnd = Math.Min(bucketEnd, outputEnd);
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                ulong localStart = segmentStart + overlapStart - outputOffset;
                ulong localCount = overlapEnd - overlapStart;
                ReadBucket(handle, localStart, localCount, buffer, out float first, out float last, out float min, out float max, cancellationToken);
                accumulators[bucket].AddSummary(first, last, min, max);
            }
        }

        public int ReadSamples(
            TdmsChannelKey key,
            ulong segmentStart,
            int sampleCount,
            float[] buffer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ChannelHandles.TryGetValue(key, out IntPtr handle))
            {
                return 0;
            }

            TdmNative.ThrowIfError(
                TdmNative.DDC_GetDataValuesFloat(handle, (UIntPtr)segmentStart, (UIntPtr)sampleCount, buffer),
                "DDC_GetDataValuesFloat");
            return sampleCount;
        }

        public void Dispose()
        {
            if (File != IntPtr.Zero)
            {
                TdmNative.DDC_CloseFile(File);
                File = IntPtr.Zero;
            }

            ChannelHandles.Clear();
            Channels.Clear();
            _materialized.Dispose();
        }
    }

    private sealed class CompressedCaptureSegment : IReadableSegment
    {
        private const int RecordHeaderSize = sizeof(int) + sizeof(ulong) + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(int) + sizeof(byte);
        private readonly FileStream _stream;
        private readonly object _streamSync = new();
        private readonly CompressedCaptureManifest _manifest;
        private readonly Dictionary<int, CompressedCaptureChannelManifest> _channelsByOrdinal;
        private readonly Dictionary<TdmsChannelKey, List<CompressedRecordIndex>> _recordsByChannel = new();

        private CompressedCaptureSegment(string path, FileStream stream, CompressedCaptureManifest manifest)
        {
            Path = path;
            _stream = stream;
            _manifest = manifest;
            _channelsByOrdinal = manifest.Channels.ToDictionary(channel => channel.Ordinal, channel => channel);
            Channels = new Dictionary<TdmsChannelKey, TdmsChannelInfo>();
            FileInfo = new TdmsFileInfo(path, Array.Empty<TdmsGroupInfo>());
        }

        public string Path { get; }
        public TdmsFileInfo FileInfo { get; private set; }
        public Dictionary<TdmsChannelKey, TdmsChannelInfo> Channels { get; private set; }

        public static CompressedCaptureSegment Open(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                byte[] magic = reader.ReadBytes(CompressedCaptureFormat.Magic.Length);
                if (!magic.SequenceEqual(CompressedCaptureFormat.Magic))
                {
                    throw new InvalidDataException("The file is not a DASH compressed capture segment.");
                }

                int version = reader.ReadInt32();
                if (version != CompressedCaptureFormat.Version)
                {
                    throw new InvalidDataException($"Unsupported compressed capture version {version}.");
                }

                int manifestLength = reader.ReadInt32();
                if (manifestLength <= 0 || manifestLength > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException("Compressed capture manifest length is invalid.");
                }

                byte[] manifestBytes = reader.ReadBytes(manifestLength);
                if (manifestBytes.Length != manifestLength)
                {
                    throw new EndOfStreamException("Unexpected end of compressed capture manifest.");
                }

                CompressedCaptureManifest manifest =
                    JsonSerializer.Deserialize<CompressedCaptureManifest>(manifestBytes, CompressedCaptureFormat.JsonOptions) ??
                    throw new InvalidDataException("Compressed capture manifest is invalid.");

                var segment = new CompressedCaptureSegment(path, stream, manifest);
                segment.ScanRecords(reader);
                segment.BuildFileInfo();
                return segment;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public void AccumulateEnvelope(
            TdmsChannelKey key,
            ulong segmentStart,
            ulong segmentCount,
            ulong outputOffset,
            ulong totalSampleCount,
            TdmsEnvelopeAccumulator[] accumulators,
            float[] buffer,
            CancellationToken cancellationToken)
        {
            if (!_recordsByChannel.TryGetValue(key, out List<CompressedRecordIndex>? records))
            {
                return;
            }

            ulong requestEnd = segmentStart + segmentCount;
            foreach (CompressedRecordIndex record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong recordEnd = record.SampleStart + (ulong)record.SampleCount;
                if (recordEnd <= segmentStart || record.SampleStart >= requestEnd)
                {
                    continue;
                }

                byte[] raw = ReadRecord(record);
                ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(raw);
                int localStart = (int)(Math.Max(segmentStart, record.SampleStart) - record.SampleStart);
                int localEnd = (int)(Math.Min(requestEnd, recordEnd) - record.SampleStart);
                for (int index = localStart; index < localEnd; index++)
                {
                    ulong relative = outputOffset + record.SampleStart + (ulong)index - segmentStart;
                    int bucket = BucketIndex(relative, totalSampleCount, accumulators.Length);
                    accumulators[bucket].Add(values[index]);
                }
            }
        }

        public int ReadSamples(
            TdmsChannelKey key,
            ulong segmentStart,
            int sampleCount,
            float[] buffer,
            CancellationToken cancellationToken)
        {
            if (!_recordsByChannel.TryGetValue(key, out List<CompressedRecordIndex>? records) || sampleCount <= 0)
            {
                return 0;
            }

            Array.Clear(buffer, 0, sampleCount);
            ulong requestEnd = segmentStart + (ulong)sampleCount;
            int copied = 0;
            foreach (CompressedRecordIndex record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong recordEnd = record.SampleStart + (ulong)record.SampleCount;
                if (recordEnd <= segmentStart || record.SampleStart >= requestEnd)
                {
                    continue;
                }

                byte[] raw = ReadRecord(record);
                ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(raw);
                int localStart = (int)(Math.Max(segmentStart, record.SampleStart) - record.SampleStart);
                int localEnd = (int)(Math.Min(requestEnd, recordEnd) - record.SampleStart);
                int length = localEnd - localStart;
                int destination = (int)(Math.Max(segmentStart, record.SampleStart) - segmentStart);
                values.Slice(localStart, length).CopyTo(buffer.AsSpan(destination, length));
                copied += length;
            }

            return copied == 0 ? 0 : sampleCount;
        }

        public void Dispose()
        {
            _stream.Dispose();
            Channels.Clear();
            _recordsByChannel.Clear();
        }

        private void ScanRecords(BinaryReader reader)
        {
            while (_stream.Position < _stream.Length)
            {
                if (_stream.Length - _stream.Position < RecordHeaderSize)
                {
                    break;
                }

                int channelOrdinal = reader.ReadInt32();
                ulong sampleStart = reader.ReadUInt64();
                int sampleCount = reader.ReadInt32();
                int originalLength = reader.ReadInt32();
                int transformedLength = reader.ReadInt32();
                int payloadLength = reader.ReadInt32();
                byte flags = reader.ReadByte();

                if (!_channelsByOrdinal.TryGetValue(channelOrdinal, out CompressedCaptureChannelManifest? channel) ||
                    sampleCount <= 0 ||
                    originalLength != sampleCount * sizeof(float) ||
                    transformedLength < 0 ||
                    payloadLength < 0)
                {
                    throw new InvalidDataException("Compressed capture record header is invalid.");
                }

                if (_stream.Length - _stream.Position < payloadLength)
                {
                    break;
                }

                long payloadOffset = _stream.Position;
                _stream.Position += payloadLength;
                var key = new TdmsChannelKey(channel.GroupName, channel.ChannelName);
                if (!_recordsByChannel.TryGetValue(key, out List<CompressedRecordIndex>? records))
                {
                    records = new List<CompressedRecordIndex>();
                    _recordsByChannel[key] = records;
                }

                records.Add(new CompressedRecordIndex(
                    channelOrdinal,
                    sampleStart,
                    sampleCount,
                    originalLength,
                    transformedLength,
                    payloadLength,
                    flags,
                    payloadOffset));
            }
        }

        private void BuildFileInfo()
        {
            var groups = new List<TdmsGroupInfo>();
            foreach (IGrouping<string, CompressedCaptureChannelManifest> groupSet in _manifest.Channels
                .GroupBy(channel => channel.GroupName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().DeviceId))
            {
                CompressedCaptureChannelManifest first = groupSet.First();
                var channels = groupSet
                    .Select(channel =>
                    {
                        var key = new TdmsChannelKey(channel.GroupName, channel.ChannelName);
                        ulong sampleCount = _recordsByChannel.TryGetValue(key, out List<CompressedRecordIndex>? records)
                            ? records.Aggregate(0UL, (max, record) => Math.Max(max, record.SampleStart + (ulong)record.SampleCount))
                            : 0;
                        return new TdmsChannelInfo(
                            key,
                            channel.DeviceId,
                            channel.GroupName,
                            channel.ChannelName,
                            channel.ChannelDescription,
                            string.IsNullOrWhiteSpace(channel.Unit) ? "raw" : channel.Unit,
                            channel.ChannelId,
                            channel.DataIndex,
                            channel.LocalDataIndex,
                            sampleCount,
                            channel.SampleRate);
                    })
                    .OrderBy(channel => channel.LocalDataIndex)
                    .ThenBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                groups.Add(new TdmsGroupInfo(first.DeviceId, first.GroupName, first.GroupDescription, first.SampleRate, channels));
            }

            FileInfo = new TdmsFileInfo(Path, groups);
            Channels = groups
                .SelectMany(group => group.Channels)
                .ToDictionary(channel => channel.Key, channel => channel);
        }

        private byte[] ReadRecord(CompressedRecordIndex record)
        {
            byte[] payload = new byte[record.PayloadLength];
            lock (_streamSync)
            {
                _stream.Position = record.PayloadOffset;
                int offset = 0;
                while (offset < payload.Length)
                {
                    int read = _stream.Read(payload, offset, payload.Length - offset);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Unexpected end of compressed capture record.");
                    }

                    offset += read;
                }
            }

            return CompressedTdmsCodec.DecodePayload(
                payload,
                _manifest.Algorithm,
                _manifest.Preprocessor,
                _manifest.LpcOrder,
                record.OriginalLength,
                record.TransformedLength,
                record.Flags);
        }

        private static int BucketIndex(ulong relative, ulong totalSampleCount, int bucketCount)
        {
            if (bucketCount <= 1 || totalSampleCount == 0)
            {
                return 0;
            }

            int bucket = (int)((double)relative / totalSampleCount * bucketCount);
            return Math.Clamp(bucket, 0, bucketCount - 1);
        }

        private readonly record struct CompressedRecordIndex(
            int ChannelOrdinal,
            ulong SampleStart,
            int SampleCount,
            int OriginalLength,
            int TransformedLength,
            int PayloadLength,
            byte Flags,
            long PayloadOffset);
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

public readonly record struct TdmsExportProgress(
    string ChannelName,
    int CompletedChannels,
    int TotalChannels,
    ulong ChannelSamplesDone,
    ulong ChannelSamplesTotal);
