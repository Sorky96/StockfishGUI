namespace MoveMentorChessServices;

/// <summary>
/// A diagnostic snapshot recorded when the classifier assigns low confidence
/// or falls back to the <c>missed_tactic</c> generic label.
/// Saved to <c>classifier-low-confidence.jsonl</c> for offline analysis.
/// </summary>
public sealed record ClassifierDiagnosticEntry(
    DateTime TimestampUtc,
    string GameFingerprint,
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    GamePhase Phase,
    string PlayedSan,
    string PlayedUci,
    string? BestMoveUci,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    int MaterialDeltaCp,
    string AssignedLabel,
    double Confidence,
    IReadOnlyList<string> Evidence,
    string DiagnosticReason);

/// <summary>
/// Aggregate quality statistics computed from the two diagnostic log files
/// (classifier low-confidence log and advice generation traces).
/// </summary>
public sealed record AnalysisQualityReport(
    DateTime GeneratedUtc,
    int TotalClassifiedMoves,
    int LowConfidenceMoves,
    double LowConfidenceRate,
    int UnclassifiedMoves,
    double UnclassifiedRate,
    int GenericFallbackMoves,
    double GenericFallbackRate,
    IReadOnlyList<LabelQualityStat> LabelStats,
    int TotalAdviceTraces,
    int FallbackAdviceCount,
    double FallbackAdviceRate,
    IReadOnlyDictionary<string, int> FallbackReasonBreakdown,
    int HelpfulFeedbackCount = 0,
    int TooVagueFeedbackCount = 0,
    int DoNotUnderstandFeedbackCount = 0,
    int LooksWrongFeedbackCount = 0,
    int GoodTrainingTipFeedbackCount = 0,
    IReadOnlyList<LabelFeedbackStat>? LabelFeedbackStats = null,
    int QualityGateFindingCount = 0,
    int QualityGateFailureCount = 0,
    int QualityGateCorrectedCount = 0,
    int QualityGateFallbackCount = 0,
    IReadOnlyDictionary<string, int>? QualityGateCodeBreakdown = null,
    int ManualFeedbackCount = 0,
    IReadOnlyDictionary<string, int>? ManualFeedbackKindBreakdown = null,
    IReadOnlyList<ManualLabelCorrectionStat>? ManualLabelCorrectionStats = null,
    int ManualDiagnosticCaseCount = 0);

public sealed record LabelQualityStat(
    string Label,
    int Count,
    double AverageConfidence,
    int LowConfidenceCount);

public sealed record LabelFeedbackStat(
    string Label,
    int Total,
    int NegativeCount,
    double NegativeRate);

public sealed record ManualLabelCorrectionStat(
    string OriginalLabel,
    string CorrectedLabel,
    int Count);

/// <summary>
/// A single row exported to the training dataset (CSV / JSONL).
/// Contains enough signal for offline experiments with a local model.
/// </summary>
public sealed record DatasetRow(
    string GameFingerprint,
    int Ply,
    int MoveNumber,
    string Side,
    string Phase,
    string PlayedSan,
    string PlayedUci,
    string? BestMoveUci,
    string Quality,
    int? CentipawnLoss,
    int MaterialDeltaCp,
    string? OriginalMistakeLabel,
    string? EffectiveMistakeLabel,
    double? MistakeConfidence,
    string Evidence,
    string? ManualFeedbackKind,
    string? ManualCorrectedLabel,
    string? ManualComment,
    DateTime? ManualCorrectedUtc,
    string? ShortExplanation,
    string? DetailedExplanation,
    string? TrainingHint,
    bool IsHighlighted,
    string FenBefore,
    string FenAfter);
