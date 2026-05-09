using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record EngineAnalysis(string Fen, IReadOnlyList<EngineLine> Lines, string? BestMoveUci);
