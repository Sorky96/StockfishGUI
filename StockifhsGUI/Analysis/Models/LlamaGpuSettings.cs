namespace StockifhsGUI;

public sealed record LlamaGpuSettings(bool UseFullGpuPower)
{
    public static LlamaGpuSettings Default { get; } = new(false);
}
