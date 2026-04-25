using System.Text.Json;

namespace MoveMentorChessServices;

public static class AdviceRuntimeDiagnosticsLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Write(AdviceRuntimeInvocationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        string baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoveMentorChessServices",
            "runtime-diagnostics");
        Directory.CreateDirectory(baseDirectory);

        string fileName = $"llama-runtime-{log.TimestampUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json";
        string filePath = Path.Combine(baseDirectory, fileName);
        string json = JsonSerializer.Serialize(log, JsonOptions);
        File.WriteAllText(filePath, json);
        return filePath;
    }
}
