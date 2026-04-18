namespace StockifhsGUI;

public static class AdviceRuntimeCatalog
{
    public static AdviceRuntimeStatus GetStatus()
    {
        LlamaCppServerConfig? serverConfig = LlamaCppServerResolver.Resolve();
        if (serverConfig is not null)
        {
            return new AdviceRuntimeStatus(
                true,
                $"Advice model: llama.cpp server ready ({Path.GetFileName(serverConfig.ModelPath)})",
                "llama.cpp (server)",
                BuildInstallHint());
        }

        LlamaCppAdviceRuntime? llamaRuntime = LlamaCppAdviceRuntimeResolver.Resolve();
        if (llamaRuntime is not null)
        {
            return new AdviceRuntimeStatus(
                true,
                $"Advice model: llama.cpp ready ({Path.GetFileName(llamaRuntime.ModelPath)})",
                "llama.cpp",
                BuildInstallHint());
        }

        string? cliPath = LlamaCppAdviceRuntimeResolver.ResolveCliPath();
        string? serverPath = LlamaCppServerResolver.ResolveServerPath();
        string? modelPath = LlamaCppAdviceRuntimeResolver.ResolveModelPath();

        if ((!string.IsNullOrWhiteSpace(cliPath) || !string.IsNullOrWhiteSpace(serverPath))
            && string.IsNullOrWhiteSpace(modelPath))
        {
            return new AdviceRuntimeStatus(
                false,
                "Advice model: llama.cpp found, but the supported GGUF model is missing. Heuristic fallback is active.",
                "llama.cpp",
                BuildInstallHint());
        }

        if (string.IsNullOrWhiteSpace(cliPath) && string.IsNullOrWhiteSpace(serverPath)
            && !string.IsNullOrWhiteSpace(modelPath))
        {
            return new AdviceRuntimeStatus(
                false,
                "Advice model: GGUF found, but llama-server.exe and llama-cli.exe are both missing. Heuristic fallback is active.",
                "llama.cpp",
                BuildInstallHint());
        }

        LocalAdviceModelOptions? customOptions = LocalAdviceModelOptionsResolver.ResolveFromEnvironment();
        if (customOptions is not null)
        {
            LocalProcessAdviceModel customModel = new(customOptions);
            if (customModel.IsAvailable)
            {
                return new AdviceRuntimeStatus(
                    true,
                    $"Advice model: custom local runtime ready ({customModel.Name}).",
                    customModel.Name,
                    BuildInstallHint());
            }

            return new AdviceRuntimeStatus(
                false,
                $"Advice model: custom local runtime configured ({customModel.Name}), but unavailable. Heuristic fallback is active.",
                customModel.Name,
                BuildInstallHint());
        }

        return new AdviceRuntimeStatus(
            false,
            "Advice model: heuristic fallback active. Install the supported llama.cpp package to enable local LLM guidance.",
            null,
            BuildInstallHint());
    }

    public static ILocalAdviceModel? TryCreateConfiguredModel()
    {
        // Priority 1: llama-server (persistent process, fastest for multiple requests).
        LlamaCppServerConfig? serverConfig = LlamaCppServerResolver.Resolve();
        if (serverConfig is not null)
        {
            return new LlamaCppHttpAdviceModel(serverConfig);
        }

        // Priority 2: llama-cli (per-request process, slower but functional).
        LlamaCppAdviceRuntime? llamaRuntime = LlamaCppAdviceRuntimeResolver.Resolve();
        if (llamaRuntime is not null)
        {
            return new LlamaCppAdviceModel(llamaRuntime);
        }

        // Priority 3: custom local process model.
        LocalAdviceModelOptions? localModelOptions = LocalAdviceModelOptionsResolver.ResolveFromEnvironment();
        return localModelOptions is null
            ? null
            : new LocalProcessAdviceModel(localModelOptions);
    }

    public static string BuildInstallHint()
    {
        return string.Join(
            Environment.NewLine,
            [
                "Supported local setup:",
                "- preferred: place llama-server.exe in the app folder, ./llama.cpp, or ./tools/llama.cpp",
                "- alternative: place llama-cli.exe in the same locations (slower, one process per request)",
                "- recommended model: Qwen2.5-3B-Instruct-GGUF, file qwen2.5-3b-instruct-q4_k_m.gguf",
                "- place the model in ./Models or ./llama.cpp/models",
                "- optional overrides: STOCKIFHSGUI_LLAMA_CPP_SERVER_PATH, STOCKIFHSGUI_LLAMA_CPP_CLI_PATH, STOCKIFHSGUI_LLAMA_CPP_MODEL_PATH",
                "- full guide: LOCAL_LLM_SETUP.md"
            ]);
    }
}
