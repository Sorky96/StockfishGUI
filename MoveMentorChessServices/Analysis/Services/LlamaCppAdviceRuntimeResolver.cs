namespace MoveMentorChessServices;

public static class LlamaCppAdviceRuntimeResolver
{
    private const string PreferredModelBaseName = "MoveMentorChessServices-advice";
    private static readonly string[] PreferredModelFileNames =
    [
        "MoveMentorChessServices-advice.gguf",
        "MoveMentorChessServices-advice-q4_k_m.gguf",
        "qwen2.5-3b-instruct-q4_k_m.gguf",
        "qwen2.5-3b-instruct-q5_k_m.gguf",
        "advice-model.gguf"
    ];

    public static LlamaCppAdviceRuntime? Resolve()
    {
        string? cliPath = ResolveCliPath();
        string? modelPath = ResolveModelPath();

        if (string.IsNullOrWhiteSpace(cliPath) || string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        int maxTokens = ParsePositiveInt(
            Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_MAX_TOKENS"),
            96);
        int contextSize = ParsePositiveInt(
            Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_CONTEXT_SIZE"),
            2048);
        int timeoutMs = ParsePositiveInt(
            Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_TIMEOUT_MS"),
            120000);
        string gpuLayersArgument = LlamaGpuSettingsResolver.ResolveGpuLayersArgument();

        return new LlamaCppAdviceRuntime(cliPath, modelPath, maxTokens, contextSize, timeoutMs, gpuLayersArgument);
    }

    public static string? ResolveCliPath()
    {
        string? fromEnvironment = Normalize(Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_CLI_PATH"));
        if (File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (string candidate in GetCliCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? ResolveModelPath()
    {
        string? fromEnvironment = Normalize(Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_MODEL_PATH"));
        if (File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (string candidate in GetModelCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string directory in GetModelDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string? matchingModel = Directory
                .EnumerateFiles(directory, $"{PreferredModelBaseName}*.gguf", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(matchingModel))
            {
                return matchingModel;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCliCandidates()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Directory.GetCurrentDirectory();

        return
        [
            Path.Combine(baseDirectory, "llama-cli.exe"),
            Path.Combine(baseDirectory, "llama.cpp", "llama-cli.exe"),
            Path.Combine(currentDirectory, "llama-cli.exe"),
            Path.Combine(currentDirectory, "llama.cpp", "llama-cli.exe"),
            Path.Combine(currentDirectory, "tools", "llama.cpp", "llama-cli.exe")
        ];
    }

    private static IEnumerable<string> GetModelCandidates()
    {
        foreach (string directory in GetModelDirectories())
        {
            foreach (string fileName in PreferredModelFileNames)
            {
                yield return Path.Combine(directory, fileName);
            }
        }
    }

    private static IEnumerable<string> GetModelDirectories()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string currentDirectory = Directory.GetCurrentDirectory();

        return
        [
            Path.Combine(baseDirectory, "Models"),
            Path.Combine(baseDirectory, "llama.cpp", "models"),
            Path.Combine(currentDirectory, "Models"),
            Path.Combine(currentDirectory, "llama.cpp", "models"),
            Path.Combine(currentDirectory, "tools", "llama.cpp", "models")
        ];
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
}
