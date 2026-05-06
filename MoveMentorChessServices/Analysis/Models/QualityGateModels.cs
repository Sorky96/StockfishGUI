namespace MoveMentorChessServices;

public enum QualityGateSeverity
{
    Info,
    Warning,
    Failure
}

public enum AdviceFeedbackKind
{
    Correct,
    WrongLabel,
    NotUseful,
    TooGeneric,
    GoodExplanation
}

public sealed record QualityGateFinding(
    string Code,
    QualityGateSeverity Severity,
    string Message,
    string GameFingerprint,
    int Ply,
    string? Label,
    MoveQualityBucket? Quality,
    string? CorrectiveAction);

public sealed record QualityGateReport(
    DateTime TimestampUtc,
    IReadOnlyList<QualityGateFinding> Findings,
    int CorrectedCount,
    int FallbackCount);

public sealed record AdviceFeedbackEntry(
    DateTime TimestampUtc,
    string GameFingerprint,
    int Ply,
    AdviceFeedbackKind FeedbackKind,
    string Label,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    bool UsedFallback,
    string PlayedSan = "",
    string PlayedUci = "",
    string? BestMoveUci = null,
    string? GeneratorName = null);

public sealed record MoveAdviceFeedback(
    string FeedbackId,
    DateTime TimestampUtc,
    string GameFingerprint,
    PlayerSide AnalyzedSide,
    int Depth,
    int MultiPv,
    int? MoveTimeMs,
    int Ply,
    int MoveNumber,
    string PlayedSan,
    string PlayedUci,
    string FenBefore,
    string FenAfter,
    int? EvalBeforeCp,
    int? EvalAfterCp,
    string? BestMoveUci,
    string? OriginalLabel,
    double? OriginalConfidence,
    IReadOnlyList<string> OriginalEvidence,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    AdviceFeedbackKind FeedbackKind,
    string? CorrectedLabel,
    string? Comment,
    string Source);
