using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DashCapture.Native;

public static class NativeString
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding LegacyChineseEncoding = CreateLegacyChineseEncoding();

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
        return Decode(bytes.AsSpan(0, length)).Trim();
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return LegacyChineseEncoding.GetString(bytes);
        }
    }

    private static Encoding CreateLegacyChineseEncoding()
    {
        try
        {
            Type? providerType = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages");
            if (providerType?.GetProperty("Instance")?.GetValue(null) is EncodingProvider provider)
            {
                Encoding.RegisterProvider(provider);
            }

            return Encoding.GetEncoding("GB18030");
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
