namespace MoveMentorChess.Domain;

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
