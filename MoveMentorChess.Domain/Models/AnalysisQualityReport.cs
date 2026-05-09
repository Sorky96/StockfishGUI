namespace MoveMentorChess.Domain;

/// <summary>
/// A diagnostic snapshot recorded when the classifier assigns low confidence
/// or falls back to the <c>missed_tactic</c> generic label.
/// Saved to <c>classifier-low-confidence.jsonl</c> for offline analysis.
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
