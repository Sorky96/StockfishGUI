namespace MoveMentorChess.Domain;

public sealed record PlayerProfileLlmInput(
    string Player,
    PlayerProfileAudienceLevel AudienceLevel,
    string AudienceDescription,
    AdviceNarrationStyle TrainerStyle,
    string TrainerDescription,
    int GamesAnalyzed,
    int TotalAnalyzedMoves,
    int HighlightedMistakes,
    int? AverageCentipawnLoss,
    string RecentTrend,
    IReadOnlyList<string> TopMistakeLabels,
    IReadOnlyList<string> CostliestMistakeLabels,
    IReadOnlyList<string> WeakestPhases,
    IReadOnlyList<string> ProblemOpenings,
    IReadOnlyList<string> TrainingTopics,
    IReadOnlyList<string> NextActions);
