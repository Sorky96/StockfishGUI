namespace MoveMentorChess.Domain;

public sealed record TrainingPriorityItem(
    string Id,
    OpeningLineKey LineKey,
    TrainingPriorityAction Action,
    TrainingPriorityReasonCode ReasonCode,
    string Title,
    string Summary,
    string Evidence,
    double Score,
    int EstimatedMinutes,
    OpeningBranchKey? BranchKey = null,
    OpeningPositionKey? PositionKey = null,
    string? MoveSan = null,
    string? MoveUci = null)
{
    public string PriorityLabel => Score >= 10_000 || ReasonCode == TrainingPriorityReasonCode.RecentMistake
        ? "Highest priority"
        : Score >= 1_000 || ReasonCode == TrainingPriorityReasonCode.DangerousOpponentReply
            ? "Common"
            : "Low priority";
}

public enum TrainingPriorityAction
{
    TrainThisBranch,
    RepairThisPosition,
    ReviewOpponentReply
}

public enum TrainingPriorityReasonCode
{
    CoverageGap,
    RecentMistake,
    DangerousOpponentReply,
    NeglectedBranch
}
