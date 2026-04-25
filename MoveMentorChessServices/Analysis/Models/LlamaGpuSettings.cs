namespace MoveMentorChessServices;

public sealed record LlamaGpuSettings(
    bool UseFullGpuPower,
    ExplanationLevel DefaultExplanationLevel = ExplanationLevel.Intermediate,
    AdviceNarrationStyle NarrationStyle = AdviceNarrationStyle.RegularTrainer)
{
    public static LlamaGpuSettings Default { get; } = new(
        false,
        ExplanationLevel.Intermediate,
        AdviceNarrationStyle.RegularTrainer);
}
