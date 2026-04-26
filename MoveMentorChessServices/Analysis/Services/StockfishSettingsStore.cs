using System.Text.Json;

namespace MoveMentorChessServices;

public static class StockfishSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object SyncLock = new();

    public static StockfishSettings Load()
    {
        lock (SyncLock)
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    return StockfishSettings.Default;
                }

                string json = File.ReadAllText(path);
                StockfishSettings? settings = JsonSerializer.Deserialize<StockfishSettings>(json, JsonOptions);
                return Normalize(settings);
            }
            catch
            {
                return StockfishSettings.Default;
            }
        }
    }

    public static void Save(StockfishSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (SyncLock)
        {
            string path = GetSettingsPath();
            string directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(Normalize(settings), JsonOptions);
            File.WriteAllText(path, json);
        }
    }

    private static StockfishSettings Normalize(StockfishSettings? settings)
    {
        settings ??= StockfishSettings.Default;
        return new StockfishSettings(
            Threads: Math.Clamp(settings.Threads <= 0 ? StockfishSettings.Default.Threads : settings.Threads, 1, 64),
            HashMb: Math.Clamp(settings.HashMb <= 0 ? StockfishSettings.Default.HashMb : settings.HashMb, 16, 4096),
            BulkAnalysisDepth: Math.Clamp(settings.BulkAnalysisDepth <= 0 ? StockfishSettings.Default.BulkAnalysisDepth : settings.BulkAnalysisDepth, 1, 30),
            BulkAnalysisMultiPv: Math.Clamp(settings.BulkAnalysisMultiPv <= 0 ? StockfishSettings.Default.BulkAnalysisMultiPv : settings.BulkAnalysisMultiPv, 1, 5),
            BulkAnalysisMoveTimeMs: Math.Clamp(settings.BulkAnalysisMoveTimeMs <= 0 ? StockfishSettings.Default.BulkAnalysisMoveTimeMs : settings.BulkAnalysisMoveTimeMs, 25, 5000));
    }

    private static string GetSettingsPath()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "MoveMentorChessServices", "settings", "stockfish-settings.json");
    }
}
