namespace StockifhsGUI;

public enum AdviceGeneratorMode
{
    Template,
    Adaptive
}

public sealed record AdviceGenerationSettings(
    AdviceGeneratorMode Mode,
    int MaxShortTextLength = 220,
    int MaxTrainingHintLength = 220,
    int MaxDetailedTextLength = 540)
{
    public static AdviceGenerationSettings Default { get; } = new(AdviceGeneratorMode.Adaptive);
}

public sealed record AdviceGenerationContext(
    string Source,
    string? GameFingerprint,
    PlayerSide? AnalyzedSide = null);

public sealed record AdviceGenerationTrace(
    DateTime TimestampUtc,
    string GeneratorName,
    AdviceGeneratorMode Mode,
    bool UsedFallback,
    string? FallbackReason,
    string Source,
    string? GameFingerprint,
    PlayerSide? AnalyzedSide,
    ExplanationLevel ExplanationLevel,
    MoveQualityBucket Quality,
    string Label,
    string PlayedSan,
    string? BestMoveUci,
    int? CentipawnLoss,
    string ShortText,
    string DetailedText,
    string TrainingHint);
