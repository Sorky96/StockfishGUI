namespace MoveMentorChessServices;

public static class LlamaGpuSettingsResolver
{
    public const string BalancedGpuLayersArgument = "35";
    public const string FullGpuLayersArgument = "all";

    public static LlamaGpuSettings Resolve()
    {
        string? overrideValue = Environment.GetEnvironmentVariable("MoveMentorChessServices_LLAMA_CPP_FULL_GPU");
        if (TryParseBooleanOverride(overrideValue, out bool useFullGpuPower))
        {
            return new LlamaGpuSettings(useFullGpuPower);
        }

        return LlamaGpuSettingsStore.Load();
    }

    public static string ResolveGpuLayersArgument()
    {
        LlamaGpuSettings settings = Resolve();
        return settings.UseFullGpuPower ? FullGpuLayersArgument : BalancedGpuLayersArgument;
    }

    private static bool TryParseBooleanOverride(string? value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("false", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("no", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }
}
