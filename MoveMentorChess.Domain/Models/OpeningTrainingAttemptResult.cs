namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingAttemptResult(
    string PositionId,
    OpeningTrainingMode Mode,
    OpeningTrainingSourceKind PositionSource,
    OpeningTrainingAttemptStatus Status,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    IReadOnlyList<OpeningTrainingMoveOption> ExpectedMoves,
    OpeningTrainingScore Score,
    string ShortExplanation,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences,
    OpeningPositionIdentity? ResolvedPosition = null,
    OpeningMoveIdea? WhyThisMove = null)
{
    public OpeningTrainingAttemptResult(
        string positionId,
        OpeningTrainingMode mode,
        OpeningTrainingSourceKind positionSource,
        string submittedMoveText,
        string? resolvedSan,
        string? resolvedUci,
        IReadOnlyList<OpeningTrainingMoveOption> expectedMoves,
        OpeningTrainingScore score,
        string shortExplanation,
        IReadOnlyList<OpeningTrainingMoveOption> matchingReferences,
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences,
        IReadOnlyList<OpeningTrainingMoveOption> playableReferences)
        : this(
            positionId,
            mode,
            positionSource,
            OpeningTrainingAttemptStatus.Normal,
            submittedMoveText,
            resolvedSan,
            resolvedUci,
            expectedMoves,
            score,
            shortExplanation,
            matchingReferences,
            preferredReferences,
            playableReferences,
            null,
            null)
    {
    }
}
