namespace MoveMentorChess.Domain;

public sealed record ProfileCostlyLabelStat(
    string Label,
    int Count,
    int TotalCentipawnLoss,
    int? AverageCentipawnLoss);
