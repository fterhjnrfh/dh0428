using System.Buffers.Binary;
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
    private const int Float32ByteCount = sizeof(float);

    internal static byte[] EncodePayload(byte[] input, CompressionSettings settings, out int transformedLength, out byte flags)
    {
        if (!settings.Enabled || settings.Algorithm == CompressionAlgorithm.None)
        {
            transformedLength = input.Length;
            flags = CompressedCaptureFormat.StoredChunkFlag | CompressedCaptureFormat.RawStoredChunkFlag;
            return input.ToArray();
        }

        byte[] transformed = input.ToArray();
        Preprocess(transformed, settings.Preprocessor, settings.LpcOrder);
        byte[] compressed = CompressChunk(transformed, settings);
        bool storePlain = compressed.Length >= transformed.Length;
        transformedLength = transformed.Length;
        flags = storePlain ? CompressedCaptureFormat.StoredChunkFlag : (byte)0;
        if (settings.Preprocessor != CompressionPreprocessor.None)
        {
            flags |= CompressedCaptureFormat.PreprocessedChunkFlag;
        }

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
        bool rawStored = (flags & CompressedCaptureFormat.RawStoredChunkFlag) != 0;
        byte[] transformed = rawStored || (flags & CompressedCaptureFormat.StoredChunkFlag) != 0
            ? payload.ToArray()
            : DecompressChunk(payload, algorithm, transformedLength);

        if (transformed.Length != transformedLength)
        {
            throw new InvalidDataException("Compressed chunk decompressed to an unexpected length.");
        }

        if (!rawStored)
        {
            ReversePreprocess(transformed, preprocessor, lpcOrder);
        }

        if (transformed.Length != originalLength)
        {
            throw new InvalidDataException("Compressed chunk restored to an unexpected length.");
        }

        return transformed;
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
            case CompressionPreprocessor.ByteShuffle:
                ByteShuffleEncode(data);
                return;
            case CompressionPreprocessor.FloatXorDelta:
                FloatXorDeltaEncode(data);
                return;
            case CompressionPreprocessor.DeltaFloatPredictor:
                DeltaFloatPredictorEncode(data);
                return;
            case CompressionPreprocessor.IntDeltaZigZag:
                IntDeltaZigZagEncode(data);
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
            case CompressionPreprocessor.ByteShuffle:
                ByteShuffleDecode(data);
                return;
            case CompressionPreprocessor.FloatXorDelta:
                FloatXorDeltaDecode(data);
                return;
            case CompressionPreprocessor.DeltaFloatPredictor:
                DeltaFloatPredictorDecode(data);
                return;
            case CompressionPreprocessor.IntDeltaZigZag:
                IntDeltaZigZagDecode(data);
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

    private static void ByteShuffleEncode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        if (sampleCount <= 1)
        {
            return;
        }

        byte[] original = data.ToArray();
        for (int byteIndex = 0; byteIndex < Float32ByteCount; byteIndex++)
        {
            int destinationBase = byteIndex * sampleCount;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                data[destinationBase + sample] = original[(sample * Float32ByteCount) + byteIndex];
            }
        }
    }

    private static void ByteShuffleDecode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        if (sampleCount <= 1)
        {
            return;
        }

        byte[] shuffled = data.ToArray();
        for (int byteIndex = 0; byteIndex < Float32ByteCount; byteIndex++)
        {
            int sourceBase = byteIndex * sampleCount;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                data[(sample * Float32ByteCount) + byteIndex] = shuffled[sourceBase + sample];
            }
        }
    }

    private static void FloatXorDeltaEncode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        for (int sample = sampleCount - 1; sample > 0; sample--)
        {
            int offset = sample * Float32ByteCount;
            int previousOffset = offset - Float32ByteCount;
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            uint previous = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(previousOffset, Float32ByteCount));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), current ^ previous);
        }
    }

    private static void FloatXorDeltaDecode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        if (sampleCount <= 1)
        {
            return;
        }

        uint previous = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, Float32ByteCount));
        for (int sample = 1; sample < sampleCount; sample++)
        {
            int offset = sample * Float32ByteCount;
            uint encoded = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            uint restored = encoded ^ previous;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), restored);
            previous = restored;
        }
    }

    private static void DeltaFloatPredictorEncode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        for (int sample = sampleCount - 1; sample >= 2; sample--)
        {
            int offset = sample * Float32ByteCount;
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            uint previous = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset - Float32ByteCount, Float32ByteCount));
            uint previous2 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset - (2 * Float32ByteCount), Float32ByteCount));
            uint predicted = unchecked((2u * previous) - previous2);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), unchecked(current - predicted));
        }
    }

    private static void DeltaFloatPredictorDecode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        for (int sample = 2; sample < sampleCount; sample++)
        {
            int offset = sample * Float32ByteCount;
            uint encoded = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            uint previous = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset - Float32ByteCount, Float32ByteCount));
            uint previous2 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset - (2 * Float32ByteCount), Float32ByteCount));
            uint predicted = unchecked((2u * previous) - previous2);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), unchecked(encoded + predicted));
        }
    }

    private static void IntDeltaZigZagEncode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        if (sampleCount <= 1)
        {
            return;
        }

        int previous = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, Float32ByteCount));
        for (int sample = 1; sample < sampleCount; sample++)
        {
            int offset = sample * Float32ByteCount;
            int current = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            int delta = unchecked(current - previous);
            uint zigZag = unchecked((uint)((delta << 1) ^ (delta >> 31)));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), zigZag);
            previous = current;
        }
    }

    private static void IntDeltaZigZagDecode(byte[] data)
    {
        int sampleCount = data.Length / Float32ByteCount;
        if (sampleCount <= 1)
        {
            return;
        }

        int previous = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, Float32ByteCount));
        for (int sample = 1; sample < sampleCount; sample++)
        {
            int offset = sample * Float32ByteCount;
            uint zigZag = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, Float32ByteCount));
            int delta = unchecked((int)((zigZag >> 1) ^ (uint)-(int)(zigZag & 1)));
            int restored = unchecked(previous + delta);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, Float32ByteCount), restored);
            previous = restored;
        }
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
}
