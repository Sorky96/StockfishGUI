namespace MoveMentorChess.Domain;

public sealed record PlayerRatingTrendReport(
    GameTimeControlCategory? TimeControlCategory,
    int GamesAnalyzed,
    int? CurrentImportedRating,
    MoveMentorStrengthPoint? CurrentStrength,
    IReadOnlyList<PlayerRatingSnapshot> RatingPoints,
    IReadOnlyList<MoveMentorStrengthPoint> StrengthPoints,
    IReadOnlyList<ProfileMonthlyTrend> AverageCentipawnLossTrend,
    IReadOnlyList<ProfileMoveQualityTrend> MoveQualityTrend,
    string Summary);
