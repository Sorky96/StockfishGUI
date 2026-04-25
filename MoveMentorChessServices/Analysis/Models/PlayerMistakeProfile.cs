namespace MoveMentorChessServices;

/// <summary>
/// Lightweight summary of a player's recurring mistake patterns from past analyses.
/// Injected into the advice prompt to let the LLM personalize coaching.
/// </summary>
public sealed record PlayerMistakeProfile(
    string PlayerName,
    int GamesAnalyzed,
    int? AverageCentipawnLoss,
    IReadOnlyList<PlayerMistakePatternEntry> TopPatterns,
    GamePhase? WeakestPhase);

public sealed record PlayerMistakePatternEntry(
    string Label,
    int Count);
