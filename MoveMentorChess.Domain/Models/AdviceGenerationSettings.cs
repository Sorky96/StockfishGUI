namespace MoveMentorChess.Domain;

public sealed record AdviceGenerationSettings(
    AdviceGeneratorMode Mode,
    int MaxShortTextLength = 220,
    int MaxTrainingHintLength = 220,
    int MaxDetailedTextLength = 540)
{
    public static AdviceGenerationSettings Default { get; } = new(AdviceGeneratorMode.Adaptive);
}
