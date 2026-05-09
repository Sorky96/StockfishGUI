using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredGameContext(
    string GameFingerprint,
    string? WhitePlayer,
    string? BlackPlayer,
    string? DateText,
    string? Result,
    string? Eco,
    string? Site,
    int? WhiteElo = null,
    int? BlackElo = null,
    string? TimeControl = null,
    GameTimeControlCategory TimeControlCategory = GameTimeControlCategory.Unknown,
    string? UtcDate = null,
    string? UtcTime = null,
    string? EndDate = null,
    string? EndTime = null,
    string? Termination = null,
    string? Link = null);
