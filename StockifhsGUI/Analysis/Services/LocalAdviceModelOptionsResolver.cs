namespace StockifhsGUI;

public static class LocalAdviceModelOptionsResolver
{
    public static LocalAdviceModelOptions? ResolveFromEnvironment()
    {
        string? command = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_COMMAND");
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string? arguments = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_ARGS");
        string? workingDirectory = Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_WORKDIR");
        int timeoutMs = ParsePositiveInt(
            Environment.GetEnvironmentVariable("STOCKIFHSGUI_LOCAL_ADVICE_TIMEOUT_MS"),
            45000);

        return new LocalAdviceModelOptions(
            command.Trim(),
            Normalize(arguments),
            Normalize(workingDirectory),
            timeoutMs);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
}
