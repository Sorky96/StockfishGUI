namespace MoveMentorChess.Domain;

public sealed record OpeningCoverageSummary(
    int TotalBookBranches,
    int CoveredBranches,
    int WeakBranches,
    int UnseenCommonBranches,
    double CoveragePercent,
    int KnownPositions,
    int StableBranches,
    int KnowledgeBoundaryPly);
