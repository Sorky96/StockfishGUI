using System.Collections.Generic;

namespace MoveMentorChess.Presentation.Models;

internal sealed record GameStateSnapshot(
    string?[,] Board,
    List<string> MoveHistory,
    bool WhiteToMove,
    bool RotateBoard,
    bool WhiteKingMoved,
    bool BlackKingMoved,
    bool WhiteRookLeftMoved,
    bool WhiteRookRightMoved,
    bool BlackRookLeftMoved,
    bool BlackRookRightMoved,
    string? EnPassantTargetSquare,
    int HalfmoveClock,
    int FullmoveNumber,
    int ImportedMoveCursor);
