namespace MoveMentorChess.Domain;

public sealed record OpeningMistakeSequenceStat(
    string Key,
    IReadOnlyList<string> Labels,
    int Count,
    int? AverageFirstPly,
    string? RepresentativeEco);
