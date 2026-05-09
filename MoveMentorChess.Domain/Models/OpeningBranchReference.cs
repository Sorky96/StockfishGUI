using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record OpeningBranchReference(
    string? Eco,
    string OpeningName,
    string BranchLabel,
    string Source,
    bool UsedFallback);
