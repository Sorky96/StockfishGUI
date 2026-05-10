namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSessionResult(
    string SessionId,
    string PlayerKey,
    string DisplayName,
    DateTime CreatedUtc,
    DateTime CompletedUtc,
    OpeningTrainingSessionOutcome Outcome,
    OpeningTrainingStyle TrainingStyle,
    OpeningTrainingStrictness Strictness,
    int PositionCount,
    int AttemptCount,
    int CorrectCount,
    int PlayableCount,
    int WrongCount,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> ThemeLabels,
    IReadOnlyList<OpeningTrainingRecordedAttempt> Attempts,
    IReadOnlyList<OpeningReviewItem>? ReviewItems = null)
{
    public OpeningTrainingSessionResult(
        string sessionId,
        string playerKey,
        string displayName,
        DateTime createdUtc,
        DateTime completedUtc,
        OpeningTrainingSessionOutcome outcome,
        int positionCount,
        int attemptCount,
        int correctCount,
        int playableCount,
        int wrongCount,
        IReadOnlyList<string> relatedOpenings,
        IReadOnlyList<string> themeLabels,
        IReadOnlyList<OpeningTrainingRecordedAttempt> attempts)
        : this(
            sessionId,
            playerKey,
            displayName,
            createdUtc,
            completedUtc,
            outcome,
            OpeningTrainingStyle.Mixed,
            OpeningTrainingStrictness.BookFlexible,
            positionCount,
            attemptCount,
            correctCount,
            playableCount,
            wrongCount,
            relatedOpenings,
            themeLabels,
            attempts,
            null)
    {
    }
}
