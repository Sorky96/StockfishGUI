using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record ReplayPly(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string NormalizedSan,
    string Uci,
    string FenBefore,
    string FenAfter,
    string PlacementFenBefore,
    string PlacementFenAfter,
    GamePhase Phase,
    string MovingPiece,
    string? PromotionPiece,
    string FromSquare,
    string ToSquare,
    bool IsCapture,
    bool IsEnPassant,
    bool IsCastle);
