namespace MoveMentorChess.Domain;

public sealed record OpponentMoveFrequency(
    string MoveSan,
    string? MoveUci,
    int Weight,
    int BookCount,
    int MyGamesCount,
    int MistakeCount,
    bool IsManuallyPrioritized,
    OpponentMoveFrequencySourceKind PrimarySource,
    string Summary);
