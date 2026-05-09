namespace MoveMentorChess.Domain;

public sealed record OpeningLineRecallAttemptResult(
    string PositionId,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    OpeningLineRecallGrade Grade,
    string Summary,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);
