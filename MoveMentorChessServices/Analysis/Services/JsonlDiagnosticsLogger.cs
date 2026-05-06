using System.Text.Json;

namespace MoveMentorChessServices;

public sealed class JsonlDiagnosticsLogger<T>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string filePath;
    private readonly object sync = new();

    public JsonlDiagnosticsLogger(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        this.filePath = filePath;
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string FilePath => filePath;

    public void Record(T entry)
    {
        try
        {
            string line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (sync)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class QualityGateDiagnosticsLogger
{
    public static string DefaultLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoveMentorChessServices",
            "quality-gate-findings.jsonl");
    }

    public static JsonlDiagnosticsLogger<QualityGateReport> CreateDefault()
        => new(DefaultLogPath());
}

public static class AdviceFeedbackLogger
{
    public static string DefaultLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoveMentorChessServices",
            "advice-feedback.jsonl");
    }

    public static JsonlDiagnosticsLogger<AdviceFeedbackEntry> CreateDefault()
        => new(DefaultLogPath());
}
