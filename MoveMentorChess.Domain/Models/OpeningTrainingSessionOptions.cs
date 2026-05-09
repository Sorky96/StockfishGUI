namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSessionOptions(
    IReadOnlyList<OpeningTrainingMode>? Modes = null,
    IReadOnlyList<OpeningTrainingSourceKind>? Sources = null,
    int MaxPositions = 18,
    int MaxPositionsPerSource = 6,
    int MaxContinuationMoves = 6,
    IReadOnlyList<string>? TargetOpenings = null);
