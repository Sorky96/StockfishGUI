namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingBranchSourceStat(
    OpeningTrainingBranchSourceKind SourceKind,
    int Count);
