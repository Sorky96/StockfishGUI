using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredMoveContext(
    int Ply,
    int MoveNumber,
    string San,
    string Uci,
    string FenBefore,
    string FenAfter,
    GamePhase Phase,
    int? EvalBeforeCp,
    int? EvalAfterCp,
    int? BestMateIn,
    int? PlayedMateIn,
    int? CentipawnLoss,
    MoveQualityBucket Quality,
    int MaterialDeltaCp,
    string? BestMoveUci);
