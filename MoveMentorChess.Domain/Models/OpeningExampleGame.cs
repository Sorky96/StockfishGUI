namespace MoveMentorChess.Domain;

public sealed record OpeningExampleGame(
    string GameFingerprint,
    PlayerSide Side,
    string OpponentName,
    string? DateText,
    string? Result,
    string Eco,
    string OpeningDisplayName,
    int? FirstMistakePly,
    string? FirstMistakeSan,
    string? FirstMistakeType,
    int? FirstMistakeCentipawnLoss);
