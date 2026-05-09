namespace MoveMentorChess.Domain;

public sealed record OpeningImportPly(
    int Ply,
    int MoveNumber,
    string Side,
    string FenBefore,
    string FenAfter,
    string PositionKeyBefore,
    string PositionKeyAfter,
    string MoveSan,
    string MoveUci);
