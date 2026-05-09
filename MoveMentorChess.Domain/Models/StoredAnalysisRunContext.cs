using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredAnalysisRunContext(
    PlayerSide AnalyzedSide,
    int Depth,
    int MultiPv,
    int? MoveTimeMs,
    DateTime AnalysisUpdatedUtc);
