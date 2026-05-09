namespace MoveMentorChess.Domain;

public sealed record MoveMentorStrengthPoint(
    string GameFingerprint,
    DateTime? GameDate,
    GameTimeControlCategory TimeControlCategory,
    int EstimatedStrength,
    int Low,
    int High,
    MoveMentorStrengthConfidence Confidence,
    MoveMentorStrengthEstimatorKind EstimatorKind,
    string ReasonSummary);
