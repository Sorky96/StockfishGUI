namespace MoveMentorChess.Domain;

public sealed record OpponentReplyProfile(
    OpeningLineKey LineKey,
    RepertoireSide RepertoireSide,
    IReadOnlyList<OpponentMoveFrequency> Frequencies,
    string Summary);
