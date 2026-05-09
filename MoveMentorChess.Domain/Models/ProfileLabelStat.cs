namespace MoveMentorChess.Domain;

public sealed record ProfileLabelStat(
    string Label,
    int Count,
    double AverageConfidence);
