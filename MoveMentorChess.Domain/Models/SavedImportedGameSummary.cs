using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record SavedImportedGameSummary(
    string GameFingerprint,
    string DisplayTitle,
    string? WhitePlayer,
    string? BlackPlayer,
    string? DateText,
    string? Result,
    string? Eco,
    string? Site,
    int? WhiteElo,
    int? BlackElo,
    string? TimeControl,
    GameTimeControlCategory TimeControlCategory,
    DateTime UpdatedUtc)
{
    public SavedImportedGameSummary(
        string GameFingerprint,
        string DisplayTitle,
        string? WhitePlayer,
        string? BlackPlayer,
        string? DateText,
        string? Result,
        string? Eco,
        string? Site,
        DateTime UpdatedUtc)
        : this(
            GameFingerprint,
            DisplayTitle,
            WhitePlayer,
            BlackPlayer,
            DateText,
            Result,
            Eco,
            Site,
            null,
            null,
            null,
            GameTimeControlCategory.Unknown,
            UpdatedUtc)
    {
    }
}
