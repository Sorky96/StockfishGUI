using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record OpeningCriticalMoment(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string Uci,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    string? MistakeLabel,
    string Trigger,
    string BranchLabel);
