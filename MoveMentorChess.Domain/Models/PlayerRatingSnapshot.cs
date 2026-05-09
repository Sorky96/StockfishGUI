namespace MoveMentorChess.Domain;

public sealed record PlayerRatingSnapshot(
    string GameFingerprint,
    DateTime? GameDate,
    GameTimeControlCategory TimeControlCategory,
    int? PlayerRating,
    int? OpponentRating,
    double? ActualScore,
    double? ExpectedScore);
