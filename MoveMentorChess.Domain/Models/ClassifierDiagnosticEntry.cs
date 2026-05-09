namespace MoveMentorChess.Domain;

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
