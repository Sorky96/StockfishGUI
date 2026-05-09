namespace MoveMentorChess.Domain;

public sealed record ProfileSideStat(
    PlayerSide Side,
    int GamesAnalyzed,
    int HighlightedMistakes);
