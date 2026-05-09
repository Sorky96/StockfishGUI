namespace MoveMentorChess.Domain;

public sealed record TrainingRecommendation(
    int Priority,
    string FocusArea,
    string Title,
    string Description,
    GamePhase? EmphasisPhase,
    PlayerSide? EmphasisSide,
    IReadOnlyList<string> RelatedOpenings,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> SuggestedDrills,
    IReadOnlyList<ProfileMistakeExample>? Examples = null,
    IReadOnlyList<TrainingBlock>? Blocks = null);
