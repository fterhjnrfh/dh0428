using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DashCapture.Core.Configuration;
using DashCapture.Native;

namespace DashCapture.App;

public static class AppSettingsLoader
{
    public static CaptureSettings Load()
    {
        string path = FindSettingsPath();
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path), Options()) ?? new CaptureSettings()
            : new CaptureSettings();

        settings.Sdk.DashRoot = Resolve(settings.Sdk.DashRoot);
        settings.Sdk.ConfigDir = Resolve(settings.Sdk.ConfigDir);
        settings.Sdk.ParamDir = Resolve(settings.Sdk.ParamDir);
        settings.Storage.RootPath = Resolve(settings.Storage.RootPath);
        settings.Storage.TdmRuntimeDir = Resolve(settings.Storage.TdmRuntimeDir);
        settings.Storage.ChannelSelection ??= new StorageChannelSelectionSettings();
        settings.Storage.ChannelSelection.Channels ??= new List<MonitorChannelSettings>();
        settings.Platform.NativeLibraryRoot = Resolve(settings.Platform.NativeLibraryRoot);
        NativeBootstrap.ConfigureSearchDirectories(PlatformNativeDirectories(settings).Select(Resolve));
        return settings;
    }

    public static void SaveDisplayViews(IEnumerable<MonitorViewSettings> views)
    {
        string path = FindSettingsPath();
        JsonObject root = ReadSettingsRoot(path);
        var display = root["Display"] as JsonObject;
        if (display is null)
        {
            display = new JsonObject();
            root["Display"] = display;
        }

        display["Views"] = JsonSerializer.SerializeToNode(views, Options());
        WriteSettingsRoot(path, root);
    }

    public static void SaveStorageChannelSelection(StorageChannelSelectionSettings channelSelection)
    {
        string path = FindSettingsPath();
        JsonObject root = ReadSettingsRoot(path);
        var storage = root["Storage"] as JsonObject;
        if (storage is null)
        {
            storage = new JsonObject();
            root["Storage"] = storage;
        }

        storage["ChannelSelection"] = JsonSerializer.SerializeToNode(channelSelection, Options());
        WriteSettingsRoot(path, root);
    }

    private static void WriteSettingsRoot(string path, JsonObject root)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string FindSettingsPath()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(basePath))
        {
            return basePath;
        }

        string currentPath = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
        if (File.Exists(currentPath))
        {
            return currentPath;
        }

        return basePath;
    }

    private static JsonObject ReadSettingsRoot(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var documentOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: documentOptions) as JsonObject ?? new JsonObject();
    }

    private static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        foreach (string root in CandidateRoots())
        {
            string candidate = Path.GetFullPath(path, root);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(path, Environment.CurrentDirectory);
    }

    private static IEnumerable<string> PlatformNativeDirectories(CaptureSettings settings)
    {
        string runtimeId = NativeBootstrap.CurrentRuntimeId;
        string osKey = runtimeId.Split('-')[0];

        yield return settings.Platform.NativeLibraryRoot;
        yield return Path.Combine(settings.Platform.NativeLibraryRoot, runtimeId);
        yield return settings.Sdk.DashRoot;
        yield return settings.Storage.TdmRuntimeDir;

        foreach (string directory in FindConfiguredNativeDirectories(settings.Platform, runtimeId))
        {
            yield return directory;
        }

        foreach (string directory in FindConfiguredNativeDirectories(settings.Platform, osKey))
        {
            yield return directory;
        }
    }

    private static IEnumerable<string> FindConfiguredNativeDirectories(PlatformSettings platform, string key)
    {
        if (!platform.NativeLibraryDirectories.TryGetValue(key, out string[]? directories))
        {
            yield break;
        }

        foreach (string directory in directories)
        {
            yield return directory;
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        foreach (string root in WalkUp(Environment.CurrentDirectory))
        {
            yield return root;
        }

        foreach (string root in WalkUp(AppContext.BaseDirectory))
        {
            yield return root;
        }
    }

    private static IEnumerable<string> WalkUp(string start)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(start));
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
