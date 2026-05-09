namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingSourceSummary(
    OpeningTrainingSourceKind SourceKind,
    int PositionCount,
    int LineCount,
    IReadOnlyList<string> RelatedOpenings);
