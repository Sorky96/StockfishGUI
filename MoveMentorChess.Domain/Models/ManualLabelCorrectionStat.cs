namespace MoveMentorChess.Domain;

/// <summary>
/// A diagnostic snapshot recorded when the classifier assigns low confidence
/// or falls back to the <c>missed_tactic</c> generic label.
/// Saved to <c>classifier-low-confidence.jsonl</c> for offline analysis.
/// </summary>

public sealed record ManualLabelCorrectionStat(
    string OriginalLabel,
    string CorrectedLabel,
    int Count);

/// <summary>
/// A single row exported to the training dataset (CSV / JSONL).
/// Contains enough signal for offline experiments with a local model.
/// </summary>
