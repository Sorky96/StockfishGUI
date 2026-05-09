namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingOutcomeSummary(
    int SessionCount,
    int AttemptCount,
    int CorrectCount,
    int PlayableCount,
    int WrongCount,
    double Accuracy,
    double WrongRate,
    DateTime? LastCompletedUtc,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> ThemeLabels);
