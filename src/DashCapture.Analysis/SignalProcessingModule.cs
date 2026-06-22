using System.Text.Json;

namespace DashCapture.Analysis;

public sealed record SignalProcessingModuleDefinition(
    string Name,
    IReadOnlyList<SignalProcessingAlgorithmDefinition> Algorithms)
{
    public static SignalProcessingModuleDefinition BuiltInAmplitudeAnalysis { get; } = new(
        "幅值分析",
        new[]
        {
            new SignalProcessingAlgorithmDefinition("最大值", SignalProcessingAlgorithmType.Maximum),
            new SignalProcessingAlgorithmDefinition("最小值", SignalProcessingAlgorithmType.Minimum),
            new SignalProcessingAlgorithmDefinition("峰峰值", SignalProcessingAlgorithmType.PeakToPeak),
            new SignalProcessingAlgorithmDefinition("RMS", SignalProcessingAlgorithmType.Rms),
            new SignalProcessingAlgorithmDefinition("均值", SignalProcessingAlgorithmType.Mean),
            new SignalProcessingAlgorithmDefinition("标准差", SignalProcessingAlgorithmType.StandardDeviation)
        });

    public static SignalProcessingModuleDefinition LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Module path is required.", nameof(path));
        }

        using FileStream stream = File.OpenRead(path);
        SignalProcessingModuleDto dto = JsonSerializer.Deserialize<SignalProcessingModuleDto>(stream, SignalProcessingJson.Options)
            ?? throw new InvalidDataException("Signal processing module is empty.");

        string name = string.IsNullOrWhiteSpace(dto.Name) ? Path.GetFileNameWithoutExtension(path) : dto.Name.Trim();
        SignalProcessingAlgorithmDefinition[] algorithms = (dto.Algorithms ?? Array.Empty<SignalProcessingAlgorithmDto>())
            .Select(ToAlgorithm)
            .ToArray();
        if (algorithms.Length == 0)
        {
            throw new InvalidDataException("Signal processing module does not contain any algorithms.");
        }

        return new SignalProcessingModuleDefinition(name, algorithms);
    }

    private static SignalProcessingAlgorithmDefinition ToAlgorithm(SignalProcessingAlgorithmDto dto)
    {
        SignalProcessingAlgorithmType type = ParseAlgorithmType(dto.Type);
        string name = string.IsNullOrWhiteSpace(dto.Name) ? DefaultAlgorithmName(type) : dto.Name.Trim();
        return new SignalProcessingAlgorithmDefinition(name, type);
    }

    public static SignalProcessingAlgorithmType ParseAlgorithmType(string? text)
    {
        string normalized = (text ?? string.Empty).Trim().Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "maximum" or "max" or "最大值" or "最大" => SignalProcessingAlgorithmType.Maximum,
            "minimum" or "min" or "最小值" or "最小" => SignalProcessingAlgorithmType.Minimum,
            "peaktopeak" or "ptp" or "峰峰值" or "峰峰" => SignalProcessingAlgorithmType.PeakToPeak,
            "mean" or "average" or "avg" or "均值" or "平均值" => SignalProcessingAlgorithmType.Mean,
            "rms" or "有效值" => SignalProcessingAlgorithmType.Rms,
            "standarddeviation" or "std" or "stddev" or "标准差" => SignalProcessingAlgorithmType.StandardDeviation,
            _ => throw new InvalidDataException($"Unsupported signal processing algorithm type '{text}'.")
        };
    }

    public static string DefaultAlgorithmName(SignalProcessingAlgorithmType type)
    {
        return type switch
        {
            SignalProcessingAlgorithmType.Maximum => "最大值",
            SignalProcessingAlgorithmType.Minimum => "最小值",
            SignalProcessingAlgorithmType.PeakToPeak => "峰峰值",
            SignalProcessingAlgorithmType.Mean => "均值",
            SignalProcessingAlgorithmType.Rms => "RMS",
            SignalProcessingAlgorithmType.StandardDeviation => "标准差",
            _ => type.ToString()
        };
    }
}

public sealed record SignalProcessingAlgorithmDefinition(string Name, SignalProcessingAlgorithmType Type);

public enum SignalProcessingAlgorithmType
{
    Maximum,
    Minimum,
    PeakToPeak,
    Mean,
    Rms,
    StandardDeviation
}

internal sealed class SignalProcessingModuleDto
{
    public string Name { get; set; } = string.Empty;
    public SignalProcessingAlgorithmDto[]? Algorithms { get; set; }
}

internal sealed class SignalProcessingAlgorithmDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

internal static class SignalProcessingJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
