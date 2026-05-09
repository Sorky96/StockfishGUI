using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record StoredMoveAdviceContext(
    string? MistakeLabel,
    double? MistakeConfidence,
    IReadOnlyList<string> Evidence,
    string? ShortExplanation,
    string? DetailedExplanation,
    string? TrainingHint,
    bool IsHighlighted,
    string? OriginalMistakeLabel = null);
