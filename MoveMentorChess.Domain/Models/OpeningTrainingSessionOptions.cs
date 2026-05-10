namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSessionOptions(
    IReadOnlyList<OpeningTrainingMode>? Modes = null,
    IReadOnlyList<OpeningTrainingSourceKind>? Sources = null,
    int MaxPositions = 18,
    int MaxPositionsPerSource = 6,
    int MaxContinuationMoves = 6,
    IReadOnlyList<string>? TargetOpenings = null,
    IReadOnlyList<OpeningKey>? SelectedOpeningKeys = null,
    IReadOnlyList<OpeningLineKey>? SelectedLineKeys = null,
    RepertoireSide SelectedSide = RepertoireSide.Both,
    OpeningTrainingStyle TrainingStyle = OpeningTrainingStyle.Mixed,
    OpeningTrainingStrictness Strictness = OpeningTrainingStrictness.BookFlexible,
    int MaxDepth = 12,
    bool IncludeSideVariations = true,
    bool PrioritizeOpponentFrequency = false,
    bool IncludeTranspositions = true);
