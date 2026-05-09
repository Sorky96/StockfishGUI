namespace MoveMentorChess.Domain;

public sealed record LocalModelAdviceResponse(
    string ShortText,
    string TrainingHint,
    string DetailedText,
    string? ReferencedBestMoveUci = null,
    string? ReferencedLabel = null,
    double? Confidence = null);
