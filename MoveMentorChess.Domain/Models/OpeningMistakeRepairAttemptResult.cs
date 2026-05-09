namespace MoveMentorChess.Domain;

public sealed record OpeningMistakeRepairAttemptResult(
    string PositionId,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    OpeningMistakeRepairGrade Grade,
    string Summary,
    string BetterMoveSummary,
    string WhyBetter,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);
