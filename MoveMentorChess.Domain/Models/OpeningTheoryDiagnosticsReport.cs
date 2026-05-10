namespace MoveMentorChess.Domain;

public sealed record OpeningTheoryDiagnosticsReport(
    DateTime GeneratedUtc,
    int ImportedOpeningLines,
    int PositionsWithCandidateMoves,
    int PositionsWithoutEco,
    int BranchesWithZeroFrequency,
    int DuplicatePositionKeys,
    int LinesWithoutExplicitSide);
