using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record MoveHeuristicContext(
    bool MovedPieceHangingAfterMove,
    bool MovedPieceFreeToTake,
    bool MovedPieceLikelyLosesExchange,
    int MovedPieceAttackDeficit,
    int? MovedPieceValueCp,
    int? MovedPieceMobilityBefore,
    int? MovedPieceMobilityAfter,
    bool MovedPieceToEdge,
    bool CastledKingWingPawnPush,
    bool EarlyQueenMove,
    bool EarlyRookMove,
    bool EarlyKingMoveWithoutCastling,
    bool EdgePawnPush,
    bool BestMoveIsCapture,
    bool BestMoveIsCastle,
    bool BestMoveDevelopsMinorPiece,
    bool BestMoveImprovesPieceActivity,
    int? BestMoveMaterialSwingCp,
    int? PlayedLineMaterialSwingCp,
    int DevelopedMinorPiecesBefore,
    int DevelopedMinorPiecesAfter,
    int BestMoveDevelopedMinorPiecesAfter,
    bool CastledBeforeMove,
    bool CastledAfterMove,
    bool KingLeftCastledShelter,
    bool KingCentralizedBeforeMove,
    bool KingCentralizedAfterMove,
    bool BestMoveCentralizesKing,
    bool BestMoveImprovesKingActivity);
