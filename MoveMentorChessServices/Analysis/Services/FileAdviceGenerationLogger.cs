using System.Text.Json;

namespace MoveMentorChessServices;

public sealed class FileAdviceGenerationLogger : IAdviceGenerationLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string filePath;
    private readonly object sync = new();

    public FileAdviceGenerationLogger(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        this.filePath = filePath;
        FilePath = filePath;
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Absolute path of the JSONL log file.</summary>
    public string FilePath { get; }

    public static FileAdviceGenerationLogger CreateDefault()
    {
        string baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoveMentorChessServices");
        return new FileAdviceGenerationLogger(Path.Combine(baseDirectory, "advice-traces.jsonl"));
    }

    public void Record(AdviceGenerationTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        string line = JsonSerializer.Serialize(trace, JsonOptions);
        try
        {
            lock (sync)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Diagnostic logging must never block local analysis.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostic logging must never block local analysis.
        }
    }
}
