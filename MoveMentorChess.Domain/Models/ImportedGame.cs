using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record ImportedGame(
    string PgnText,
    IReadOnlyList<string> SanMoves,
    string? WhitePlayer,
    string? BlackPlayer,
    int? WhiteElo,
    int? BlackElo,
    string? DateText,
    string? Result,
    string? Eco,
    string? Site,
    PgnGameMetadata? Metadata = null);
