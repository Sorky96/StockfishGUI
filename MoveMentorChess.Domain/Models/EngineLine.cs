using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record EngineLine(string MoveUci, int? Centipawns, int? MateIn, IReadOnlyList<string> Pv);
