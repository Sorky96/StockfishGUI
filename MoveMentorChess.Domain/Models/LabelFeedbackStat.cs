namespace MoveMentorChess.Domain;

/// <summary>
/// A diagnostic snapshot recorded when the classifier assigns low confidence
/// or falls back to the <c>missed_tactic</c> generic label.
/// Saved to <c>classifier-low-confidence.jsonl</c> for offline analysis.
/// </summary>

public sealed record LabelFeedbackStat(
    string Label,
    int Total,
    int NegativeCount,
    double NegativeRate);
