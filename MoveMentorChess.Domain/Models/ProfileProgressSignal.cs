namespace MoveMentorChess.Domain;

public sealed record ProfileProgressSignal(
    ProfileProgressDirection Direction,
    string Summary,
    ProfileProgressPeriod? Recent,
    ProfileProgressPeriod? Previous);
