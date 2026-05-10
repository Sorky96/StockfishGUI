namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingBranch(
    OpeningBranchKey BranchKey,
    string OpponentMove,
    string? OpponentMoveUci,
    int Frequency,
    string SourceSummary,
    OpeningTrainingMoveOption? RecommendedResponse,
    IReadOnlyList<OpeningTrainingMove> Continuation,
    IReadOnlyList<OpeningTrainingBranchSourceStat> SourceStats,
    OpeningPositionKey? ResultingPositionKey = null)
{
    public OpeningTrainingBranch(
        string opponentMove,
        string? opponentMoveUci,
        int frequency,
        string sourceSummary,
        OpeningTrainingMoveOption? recommendedResponse,
        IReadOnlyList<OpeningTrainingMove> continuation,
        IReadOnlyList<OpeningTrainingBranchSourceStat> sourceStats)
        : this(
            new OpeningBranchKey($"{opponentMoveUci ?? opponentMove}:{frequency}"),
            opponentMove,
            opponentMoveUci,
            frequency,
            sourceSummary,
            recommendedResponse,
            continuation,
            sourceStats,
            null)
    {
    }
}
