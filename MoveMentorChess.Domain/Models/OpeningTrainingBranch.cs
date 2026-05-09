namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingBranch(
    string OpponentMove,
    string? OpponentMoveUci,
    int Frequency,
    string SourceSummary,
    OpeningTrainingMoveOption? RecommendedResponse,
    IReadOnlyList<OpeningTrainingMove> Continuation,
    IReadOnlyList<OpeningTrainingBranchSourceStat> SourceStats);
