using System.Text;
using DashCapture.Core.Configuration;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using K4os.Compression.LZ4;
using Snappier;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace DashCapture.Storage;

internal static class CompressedTdmsCodec
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DHCMP01\0");
    private const int Version = 1;
    private const byte StoredChunk = 1;
    public const string Extension = ".dhc";

    public static bool IsCompressedFile(string path)
    {
        return path.EndsWith(".tdms" + Extension, StringComparison.OrdinalIgnoreCase);
    }

    public static string CompressedPathFor(string tdmsPath)
    {
        return tdmsPath + Extension;
    }

    public static string CompressFile(string sourcePath, CompressionSettings settings)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("TDMS segment was not found.", sourcePath);
        }

        string targetPath = CompressedPathFor(sourcePath);
        string tempPath = targetPath + ".tmp";
        File.Delete(tempPath);

        int chunkSize = Math.Clamp(settings.ChunkSizeMb, 1, 256) * 1024 * 1024;
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            WriteHeader(writer, sourcePath, input.Length, chunkSize, settings);
            byte[] buffer = new byte[chunkSize];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                byte[] transformed = buffer.AsSpan(0, read).ToArray();
                Preprocess(transformed, settings.Preprocessor, settings.LpcOrder);
                byte[] compressed = CompressChunk(transformed, settings);
                bool storePlain = compressed.Length >= transformed.Length;
                byte[] payload = storePlain ? transformed : compressed;

                writer.Write(read);
                writer.Write(transformed.Length);
                writer.Write(payload.Length);
                writer.Write(storePlain ? StoredChunk : (byte)0);
                writer.Write(payload);
            }
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
        if (settings.DeleteSourceAfterCompression)
        {
            File.Delete(sourcePath);
        }

        return targetPath;
    }

    public static MaterializedTdmsFile MaterializeForRead(string path)
    {
        if (!IsCompressedFile(path))
        {
            return new MaterializedTdmsFile(path, null);
        }

        string cacheDir = Path.Combine(Path.GetTempPath(), "DashCapture", "tdms-cache");
        Directory.CreateDirectory(cacheDir);
        string fileName = Path.GetFileNameWithoutExtension(path);
        string tempPath = Path.Combine(cacheDir, $"{fileName}_{Guid.NewGuid():N}.tdms");
        DecompressFile(path, tempPath);
        return new MaterializedTdmsFile(tempPath, tempPath);
    }

    public static void DecompressFile(string sourcePath, string targetPath)
    {
        string tempPath = targetPath + ".tmp";
        File.Delete(tempPath);

        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true))
        {
            CompressionHeader header = ReadHeader(reader);
            long written = 0;
            while (input.Position < input.Length)
            {
                int originalLength = reader.ReadInt32();
                int transformedLength = reader.ReadInt32();
                int payloadLength = reader.ReadInt32();
                byte flags = reader.ReadByte();

                if (originalLength < 0 || transformedLength < 0 || payloadLength < 0)
                {
                    throw new InvalidDataException("Compressed TDMS chunk has invalid lengths.");
                }

                byte[] payload = ReadBytesExact(reader, payloadLength);
                byte[] transformed = DecodePayload(
                    payload,
                    header.Algorithm,
                    header.Preprocessor,
                    header.LpcOrder,
                    originalLength,
                    transformedLength,
                    flags);

                output.Write(transformed, 0, transformed.Length);
                written += transformed.Length;
            }

            if (written != header.OriginalLength)
            {
                throw new InvalidDataException("Compressed TDMS file restored to an unexpected length.");
            }
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
    }

    public static string Describe(CompressionSettings settings)
    {
        if (!settings.Enabled)
        {
            return "未启用";
        }

        string preprocessor = settings.Preprocessor == CompressionPreprocessor.None ? "无预处理" : PreprocessorName(settings.Preprocessor);
        return $"{preprocessor} + {AlgorithmName(settings.Algorithm)}";
    }

    private static void WriteHeader(BinaryWriter writer, string sourcePath, long sourceLength, int chunkSize, CompressionSettings settings)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(Path.GetFileName(sourcePath));
        writer.Write(sourceLength);
        writer.Write(chunkSize);
        writer.Write((byte)settings.Algorithm);
        writer.Write((byte)settings.Preprocessor);
        writer.Write(Math.Clamp(settings.ZstdLevel, -5, 22));
        writer.Write(Math.Clamp(settings.ZstdWindowLog, 0, 31));
        writer.Write(Math.Clamp(settings.Lz4Level, 0, 12));
        writer.Write(Math.Clamp(settings.Lz4HcLevel, 3, 12));
        writer.Write(Math.Clamp(settings.ZlibLevel, 0, 9));
        writer.Write(Math.Clamp(settings.BZip2BlockSize, 1, 9));
        writer.Write(Math.Clamp(settings.LpcOrder, 1, 4));
        writer.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static CompressionHeader ReadHeader(BinaryReader reader)
    {
        byte[] magic = ReadBytesExact(reader, Magic.Length);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("The file is not a DASH compressed TDMS segment.");
        }

        int version = reader.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported compressed TDMS version {version}.");
        }

        string originalFileName = reader.ReadString();
        long originalLength = reader.ReadInt64();
        int chunkSize = reader.ReadInt32();
        var algorithm = (CompressionAlgorithm)reader.ReadByte();
        var preprocessor = (CompressionPreprocessor)reader.ReadByte();
        int zstdLevel = reader.ReadInt32();
        int zstdWindowLog = reader.ReadInt32();
        int lz4Level = reader.ReadInt32();
        int lz4HcLevel = reader.ReadInt32();
        int zlibLevel = reader.ReadInt32();
        int bzip2BlockSize = reader.ReadInt32();
        int lpcOrder = reader.ReadInt32();
        long createdAtUnixMs = reader.ReadInt64();

        if (originalLength < 0 || chunkSize <= 0)
        {
            throw new InvalidDataException("Compressed TDMS header is invalid.");
        }

        return new CompressionHeader(
            originalFileName,
            originalLength,
            chunkSize,
            algorithm,
            preprocessor,
            zstdLevel,
            zstdWindowLog,
            lz4Level,
            lz4HcLevel,
            zlibLevel,
            bzip2BlockSize,
            lpcOrder,
            createdAtUnixMs);
    }

    private static byte[] CompressChunk(byte[] input, CompressionSettings settings)
    {
        return settings.Algorithm switch
        {
            CompressionAlgorithm.Zstd => CompressZstd(input, Math.Clamp(settings.ZstdLevel, -5, 22), Math.Clamp(settings.ZstdWindowLog, 0, 31)),
            CompressionAlgorithm.Lz4 => CompressLz4(input, LZ4Level.L00_FAST),
            CompressionAlgorithm.Snappy => Snappy.CompressToArray(input),
            CompressionAlgorithm.Zlib => CompressZlib(input, Math.Clamp(settings.ZlibLevel, 0, 9)),
            CompressionAlgorithm.Lz4Hc => CompressLz4(input, ToLz4Level(Math.Clamp(settings.Lz4HcLevel, 3, 12))),
            CompressionAlgorithm.BZip2 => CompressBZip2(input, Math.Clamp(settings.BZip2BlockSize, 1, 9)),
            _ => throw new NotSupportedException($"Compression algorithm {settings.Algorithm} is not supported.")
        };
    }

    internal static byte[] EncodePayload(byte[] input, CompressionSettings settings, out int transformedLength, out byte flags)
    {
        byte[] transformed = input.ToArray();
        Preprocess(transformed, settings.Preprocessor, settings.LpcOrder);
        byte[] compressed = CompressChunk(transformed, settings);
        bool storePlain = compressed.Length >= transformed.Length;
        transformedLength = transformed.Length;
        flags = storePlain ? StoredChunk : (byte)0;
        return storePlain ? transformed : compressed;
    }

    internal static byte[] DecodePayload(
        byte[] payload,
        CompressionAlgorithm algorithm,
        CompressionPreprocessor preprocessor,
        int lpcOrder,
        int originalLength,
        int transformedLength,
        byte flags)
    {
        byte[] transformed = (flags & StoredChunk) != 0
            ? payload.ToArray()
            : DecompressChunk(payload, algorithm, transformedLength);

        if (transformed.Length != transformedLength)
        {
            throw new InvalidDataException("Compressed chunk decompressed to an unexpected length.");
        }

        ReversePreprocess(transformed, preprocessor, lpcOrder);
        if (transformed.Length != originalLength)
        {
            throw new InvalidDataException("Compressed chunk restored to an unexpected length.");
        }

        return transformed;
    }

    private static byte[] DecompressChunk(byte[] input, CompressionHeader header, int outputLength)
    {
        return DecompressChunk(input, header.Algorithm, outputLength);
    }

    private static byte[] DecompressChunk(byte[] input, CompressionAlgorithm algorithm, int outputLength)
    {
        return algorithm switch
        {
            CompressionAlgorithm.Zstd => DecompressZstd(input),
            CompressionAlgorithm.Lz4 => DecompressLz4(input, outputLength),
            CompressionAlgorithm.Snappy => DecompressSnappy(input, outputLength),
            CompressionAlgorithm.Zlib => DecompressZlib(input, outputLength),
            CompressionAlgorithm.Lz4Hc => DecompressLz4(input, outputLength),
            CompressionAlgorithm.BZip2 => DecompressBZip2(input, outputLength),
            _ => throw new NotSupportedException($"Compression algorithm {algorithm} is not supported.")
        };
    }

    private static byte[] CompressZstd(byte[] input, int level, int windowLog)
    {
        using var compressor = new Compressor(level);
        if (windowLog > 0)
        {
            compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, windowLog);
        }

        return compressor.Wrap(input).ToArray();
    }

    private static byte[] DecompressZstd(byte[] input)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(input).ToArray();
    }

    private static byte[] CompressLz4(byte[] input, LZ4Level level)
    {
        byte[] output = new byte[LZ4Codec.MaximumOutputSize(input.Length)];
        int length = LZ4Codec.Encode(input, output, level);
        if (length <= 0)
        {
            throw new InvalidDataException("LZ4 compression failed.");
        }

        Array.Resize(ref output, length);
        return output;
    }

    private static byte[] DecompressLz4(byte[] input, int outputLength)
    {
        byte[] output = new byte[outputLength];
        int length = LZ4Codec.Decode(input, output);
        if (length != outputLength)
        {
            throw new InvalidDataException("LZ4 decompression returned an unexpected length.");
        }

        return output;
    }

    private static byte[] DecompressSnappy(byte[] input, int outputLength)
    {
        byte[] output = new byte[outputLength];
        int length = Snappy.Decompress(input, output);
        if (length != outputLength)
        {
            throw new InvalidDataException("Snappy decompression returned an unexpected length.");
        }

        return output;
    }

    private static byte[] CompressZlib(byte[] input, int level)
    {
        using var output = new MemoryStream();
        var deflater = new Deflater(level, noZlibHeaderOrFooter: false);
        using (var stream = new DeflaterOutputStream(output, deflater) { IsStreamOwner = false })
        {
            stream.Write(input, 0, input.Length);
            stream.Finish();
        }

        return output.ToArray();
    }

    private static byte[] DecompressZlib(byte[] input, int outputLength)
    {
        using var source = new MemoryStream(input);
        using var inflater = new InflaterInputStream(source, new Inflater(noHeader: false));
        return ReadToExpectedLength(inflater, outputLength);
    }

    private static byte[] CompressBZip2(byte[] input, int blockSize)
    {
        using var output = new MemoryStream();
        using (var stream = new BZip2OutputStream(output, blockSize) { IsStreamOwner = false })
        {
            stream.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    private static byte[] DecompressBZip2(byte[] input, int outputLength)
    {
        using var source = new MemoryStream(input);
        using var stream = new BZip2InputStream(source);
        return ReadToExpectedLength(stream, outputLength);
    }

    private static byte[] ReadToExpectedLength(Stream stream, int outputLength)
    {
        byte[] output = new byte[outputLength];
        int offset = 0;
        while (offset < output.Length)
        {
            int read = stream.Read(output, offset, output.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset != output.Length)
        {
            throw new InvalidDataException("Decompression returned an unexpected length.");
        }

        return output;
    }

    private static void Preprocess(byte[] data, CompressionPreprocessor preprocessor, int lpcOrder)
    {
        switch (preprocessor)
        {
            case CompressionPreprocessor.None:
                return;
            case CompressionPreprocessor.Delta1:
                DeltaEncode(data);
                return;
            case CompressionPreprocessor.Delta2:
                DeltaEncode(data);
                DeltaEncode(data);
                return;
            case CompressionPreprocessor.Lpc:
                LpcEncode(data, Math.Clamp(lpcOrder, 1, 4));
                return;
            default:
                throw new NotSupportedException($"Preprocessor {preprocessor} is not supported.");
        }
    }

    private static void ReversePreprocess(byte[] data, CompressionPreprocessor preprocessor, int lpcOrder)
    {
        switch (preprocessor)
        {
            case CompressionPreprocessor.None:
                return;
            case CompressionPreprocessor.Delta1:
                DeltaDecode(data);
                return;
            case CompressionPreprocessor.Delta2:
                DeltaDecode(data);
                DeltaDecode(data);
                return;
            case CompressionPreprocessor.Lpc:
                LpcDecode(data, Math.Clamp(lpcOrder, 1, 4));
                return;
            default:
                throw new NotSupportedException($"Preprocessor {preprocessor} is not supported.");
        }
    }

    private static void DeltaEncode(byte[] data)
    {
        for (int i = data.Length - 1; i > 0; i--)
        {
            unchecked
            {
                data[i] = (byte)(data[i] - data[i - 1]);
            }
        }
    }

    private static void DeltaDecode(byte[] data)
    {
        for (int i = 1; i < data.Length; i++)
        {
            unchecked
            {
                data[i] = (byte)(data[i] + data[i - 1]);
            }
        }
    }

    private static void LpcEncode(byte[] data, int order)
    {
        if (data.Length <= order)
        {
            return;
        }

        byte[] original = data.ToArray();
        for (int i = order; i < data.Length; i++)
        {
            int predicted = Predict(original, i, order);
            unchecked
            {
                data[i] = (byte)(original[i] - predicted);
            }
        }
    }

    private static void LpcDecode(byte[] data, int order)
    {
        if (data.Length <= order)
        {
            return;
        }

        for (int i = order; i < data.Length; i++)
        {
            int predicted = Predict(data, i, order);
            unchecked
            {
                data[i] = (byte)(data[i] + predicted);
            }
        }
    }

    private static int Predict(byte[] data, int index, int order)
    {
        return order <= 1
            ? data[index - 1]
            : 2 * data[index - 1] - data[index - 2];
    }

    private static byte[] ReadBytesExact(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count)
        {
            throw new EndOfStreamException("Unexpected end of compressed TDMS file.");
        }

        return data;
    }

    private static LZ4Level ToLz4Level(int level)
    {
        return level switch
        {
            <= 3 => LZ4Level.L03_HC,
            4 => LZ4Level.L04_HC,
            5 => LZ4Level.L05_HC,
            6 => LZ4Level.L06_HC,
            7 => LZ4Level.L07_HC,
            8 => LZ4Level.L08_HC,
            9 => LZ4Level.L09_HC,
            10 => LZ4Level.L10_OPT,
            11 => LZ4Level.L11_OPT,
            _ => LZ4Level.L12_MAX
        };
    }

    private static string AlgorithmName(CompressionAlgorithm algorithm)
    {
        return algorithm switch
        {
            CompressionAlgorithm.Zstd => "ZSTD",
            CompressionAlgorithm.Lz4 => "LZ4",
            CompressionAlgorithm.Snappy => "Snappy",
            CompressionAlgorithm.Zlib => "Zlib",
            CompressionAlgorithm.Lz4Hc => "LZ4 HC",
            CompressionAlgorithm.BZip2 => "BZip2",
            _ => algorithm.ToString()
        };
    }

    private static string PreprocessorName(CompressionPreprocessor preprocessor)
    {
        return preprocessor switch
        {
            CompressionPreprocessor.Delta1 => "一阶差分",
            CompressionPreprocessor.Delta2 => "二阶差分",
            CompressionPreprocessor.Lpc => "LPC",
            _ => "无预处理"
        };
    }

    private sealed record CompressionHeader(
        string OriginalFileName,
        long OriginalLength,
        int ChunkSize,
        CompressionAlgorithm Algorithm,
        CompressionPreprocessor Preprocessor,
        int ZstdLevel,
        int ZstdWindowLog,
        int Lz4Level,
        int Lz4HcLevel,
        int ZlibLevel,
        int BZip2BlockSize,
        int LpcOrder,
        long CreatedAtUnixMs);
}

internal sealed record MaterializedTdmsFile(string Path, string? TempPath) : IDisposable
{
    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(TempPath))
        {
            return;
        }

        try
        {
            File.Delete(TempPath);
        }
        catch
        {
            // Temporary cache cleanup is best effort.
        }
    }
}
