namespace MoveMentorChess.Domain;

public sealed record PlayerProfileFormattedOutput(
    string ProfileSummary,
    string StrengthsAndWeaknesses,
    string WhatToFocusNext,
    string ToneAdaptedVersion,
    string? DeepDive = null);
