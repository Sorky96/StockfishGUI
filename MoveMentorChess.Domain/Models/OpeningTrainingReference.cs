namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingReference(
    string GameFingerprint,
    PlayerSide Side,
    string OpponentName,
    string? DateText,
    string? Result,
    string SourceLabel,
    int? FirstMistakePly,
    string? MistakeLabel);
