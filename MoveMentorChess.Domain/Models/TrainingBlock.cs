namespace MoveMentorChess.Domain;

public sealed record TrainingBlock(
    TrainingBlockPurpose Purpose,
    TrainingBlockKind Kind,
    string Title,
    string Description,
    int EstimatedMinutes,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings);
