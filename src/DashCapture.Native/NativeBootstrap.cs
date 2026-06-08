using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DashCapture.Native;

public static class NativeBootstrap
{
    private static readonly ConcurrentDictionary<string, byte> SearchDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, IntPtr> LoadedLibraries = new(StringComparer.OrdinalIgnoreCase);

    static NativeBootstrap()
    {
        AddDefaultSearchDirectories();
        NativeLibrary.SetDllImportResolver(typeof(NativeBootstrap).Assembly, ResolveNativeLibrary);
    }

    public static string CurrentRuntimeId => CreateRuntimeId();

    public static void ConfigureSearchDirectories(IEnumerable<string>? directories)
    {
        if (directories is null)
        {
            return;
        }

        foreach (string directory in directories)
        {
            AddSearchDirectory(directory);
        }
    }

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

        SearchDirectories.TryAdd(fullPath, 0);
        if (OperatingSystem.IsWindows())
        {
            SetDllDirectory(fullPath);
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!path.Contains(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", fullPath + Path.PathSeparator + path);
        }
    }

    private static void AddDefaultSearchDirectories()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Environment.CurrentDirectory;
        string runtimeId = CurrentRuntimeId;

        AddSearchDirectory(baseDirectory);
        AddSearchDirectory(Path.Combine(baseDirectory, "native"));
        AddSearchDirectory(Path.Combine(baseDirectory, "native", runtimeId));
        AddSearchDirectory(Path.Combine(baseDirectory, "runtimes", runtimeId, "native"));
        AddSearchDirectory(currentDirectory);
        AddSearchDirectory(Path.Combine(currentDirectory, "native"));
        AddSearchDirectory(Path.Combine(currentDirectory, "native", runtimeId));
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        foreach (string candidateName in CandidateLibraryNames(libraryName))
        {
            foreach (string directory in SearchDirectories.Keys)
            {
                string candidatePath = Path.Combine(directory, candidateName);
                if (LoadedLibraries.TryGetValue(candidatePath, out IntPtr loaded))
                {
                    return loaded;
                }

                if (NativeLibrary.TryLoad(candidatePath, out IntPtr handle))
                {
                    LoadedLibraries[candidatePath] = handle;
                    return handle;
                }
            }
        }

        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr defaultHandle))
        {
            return defaultHandle;
        }

        foreach (string candidateName in CandidateLibraryNames(libraryName))
        {
            if (NativeLibrary.TryLoad(candidateName, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidateLibraryNames(string libraryName)
    {
        yield return libraryName;

        string fileName = Path.GetFileName(libraryName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return baseName + ".dll";
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "lib" + baseName + ".dylib";
            yield return baseName + ".dylib";
            yield break;
        }

        yield return "lib" + baseName + ".so";
        yield return baseName + ".so";
    }

    private static string CreateRuntimeId()
    {
        string os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : "linux";

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return $"{os}-{architecture}";
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}
