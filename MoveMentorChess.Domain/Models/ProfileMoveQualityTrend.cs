namespace MoveMentorChess.Domain;

public sealed record ProfileMoveQualityTrend(
    string PeriodKey,
    int GamesAnalyzed,
    double BlundersPerGame,
    double MistakesPerGame,
    double InaccuraciesPerGame,
    double BrilliantGreatBestPerGame);
