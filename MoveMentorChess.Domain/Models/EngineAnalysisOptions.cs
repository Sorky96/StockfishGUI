using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record EngineAnalysisOptions(int Depth = 14, int MultiPv = 3, int? MoveTimeMs = null);
