namespace MoveMentorChess.Domain;

/// <summary>
/// A diagnostic snapshot recorded when the classifier assigns low confidence
/// or falls back to the <c>missed_tactic</c> generic label.
/// Saved to <c>classifier-low-confidence.jsonl</c> for offline analysis.
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
