namespace StockifhsGUI;

public static class LlamaCppServerResolver
{
    public static LlamaCppServerConfig? Resolve()
    {
        string? serverPath = ResolveServerPath();
        string? modelPath = LlamaCppAdviceRuntimeResolver.ResolveModelPath();

        if (string.IsNullOrWhiteSpace(serverPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        int port = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_SERVER_PORT"),
            0);
        int maxTokens = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_MAX_TOKENS"),
            256);
        int contextSize = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_CONTEXT_SIZE"),
            2048);
        int timeoutMs = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_TIMEOUT_MS"),
            30000);
        int startupTimeoutMs = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_SERVER_STARTUP_TIMEOUT_MS"),
            90000);

        return new LlamaCppServerConfig(serverPath, modelPath, port, contextSize, maxTokens, timeoutMs, startupTimeoutMs);
    }

    public static string? ResolveServerPath()
    {
        string? fromEnvironment = Normalize(Environment.GetEnvironmentVariable("STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH"));
        if (File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (string candidate in GetServerCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetServerCandidates()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Directory.GetCurrentDirectory();

        return
        [
            Path.Combine(baseDirectory, "llama-server.exe"),
            Path.Combine(baseDirectory, "llama.cpp", "llama-server.exe"),
            Path.Combine(currentDirectory, "llama-server.exe"),
            Path.Combine(currentDirectory, "llama.cpp", "llama-server.exe"),
            Path.Combine(currentDirectory, "tools", "llama.cpp", "llama-server.exe")
        ];
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
}
