using System.Text.Json;
using System.Text.Json.Serialization;
using DashCapture.Core.Configuration;

namespace DashCapture.App;

public static class AppSettingsLoader
{
    public static CaptureSettings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Environment.CurrentDirectory, "appsettings.json");
        }

        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path), Options()) ?? new CaptureSettings()
            : new CaptureSettings();

        settings.Sdk.DashRoot = Resolve(settings.Sdk.DashRoot);
        settings.Sdk.ConfigDir = Resolve(settings.Sdk.ConfigDir);
        settings.Sdk.ParamDir = Resolve(settings.Sdk.ParamDir);
        settings.Storage.RootPath = Resolve(settings.Storage.RootPath);
        settings.Storage.TdmRuntimeDir = Resolve(settings.Storage.TdmRuntimeDir);
        return settings;
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
