using System.Text.Json;

namespace MoveMentorChessServices;

public static class LlamaGpuSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object SyncLock = new();

    public static LlamaGpuSettings Load()
    {
        lock (SyncLock)
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path))
                {
                    return LlamaGpuSettings.Default;
                }

                string json = File.ReadAllText(path);
                LlamaGpuSettings? settings = JsonSerializer.Deserialize<LlamaGpuSettings>(json, JsonOptions);
                return settings ?? LlamaGpuSettings.Default;
            }
            catch
            {
                return LlamaGpuSettings.Default;
            }
        }
    }

    public static void Save(LlamaGpuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (SyncLock)
        {
            string path = GetSettingsPath();
            string directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
    }

    private static string GetSettingsPath()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, "MoveMentorChessServices", "settings", "llama-gpu-settings.json");
    }
}
