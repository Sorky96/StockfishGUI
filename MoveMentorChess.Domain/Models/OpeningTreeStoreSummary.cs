namespace MoveMentorChess.Domain;

public sealed record OpeningTreeStoreSummary(
    int NodeCount,
    int EdgeCount,
    int TagCount);
