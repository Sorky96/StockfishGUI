namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingAttemptResult(
    string PositionId,
    OpeningTrainingMode Mode,
    OpeningTrainingSourceKind PositionSource,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    IReadOnlyList<OpeningTrainingMoveOption> ExpectedMoves,
    OpeningTrainingScore Score,
    string ShortExplanation,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);
