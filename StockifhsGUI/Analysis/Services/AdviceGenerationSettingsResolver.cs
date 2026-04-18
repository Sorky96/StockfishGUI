namespace StockifhsGUI;

public static class AdviceGenerationSettingsResolver
{
    public static AdviceGenerationSettings ResolveFromEnvironment()
    {
        AdviceGeneratorMode mode = ParseMode(Environment.GetEnvironmentVariable("STOCKIFHSGUI_ADVICE_MODE"));
        int maxShort = ParsePositiveInt(Environment.GetEnvironmentVariable("STOCKIFHSGUI_ADVICE_SHORT_MAX"), AdviceGenerationSettings.Default.MaxShortTextLength);
        int maxHint = ParsePositiveInt(Environment.GetEnvironmentVariable("STOCKIFHSGUI_ADVICE_HINT_MAX"), AdviceGenerationSettings.Default.MaxTrainingHintLength);
        int maxDetailed = ParsePositiveInt(Environment.GetEnvironmentVariable("STOCKIFHSGUI_ADVICE_DETAILED_MAX"), AdviceGenerationSettings.Default.MaxDetailedTextLength);
        return new AdviceGenerationSettings(mode, maxShort, maxHint, maxDetailed);
    }

    private static AdviceGeneratorMode ParseMode(string? value)
    {
        if (string.Equals(value, "template", StringComparison.OrdinalIgnoreCase))
        {
            return AdviceGeneratorMode.Template;
        }

        if (string.Equals(value, "adaptive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "local", StringComparison.OrdinalIgnoreCase))
        {
            return AdviceGeneratorMode.Adaptive;
        }

        return AdviceGenerationSettings.Default.Mode;
    }

    private static int ParsePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
