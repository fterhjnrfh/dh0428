using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DashCapture.Native;

public static class NativeBootstrap
{
    public static void AddSearchDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string fullPath = Path.GetFullPath(directory);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        SetDllDirectory(fullPath);
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Contains(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", fullPath + Path.PathSeparator + path);
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
