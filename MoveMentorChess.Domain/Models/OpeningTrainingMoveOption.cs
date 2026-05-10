namespace MoveMentorChess.Domain;

public sealed record OpeningTrainingMoveOption(
    string DisplayText,
    string? Uci,
    OpeningTrainingMoveRole Role,
    bool IsPreferred,
    string? Note = null,
    OpeningLineRecallReferenceKind? ReferenceKind = null,
    OpeningTrainingMoveSourceKind SourceKind = OpeningTrainingMoveSourceKind.UserGame,
    OpeningMoveIdea? Idea = null,
    OpeningPositionKey? ToPositionKey = null);
