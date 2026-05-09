namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSessionResult(
    string SessionId,
    string PlayerKey,
    string DisplayName,
    DateTime CreatedUtc,
    DateTime CompletedUtc,
    OpeningTrainingSessionOutcome Outcome,
    int PositionCount,
    int AttemptCount,
    int CorrectCount,
    int PlayableCount,
    int WrongCount,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> ThemeLabels,
    IReadOnlyList<OpeningTrainingRecordedAttempt> Attempts);
