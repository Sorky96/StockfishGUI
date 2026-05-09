using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record GameAnalysisProgress(
    ReplayPly Replay,
    string Fen,
    GameAnalysisProgressStage Stage,
    int CurrentAnalyzedMove,
    int TotalAnalyzedMoves);
