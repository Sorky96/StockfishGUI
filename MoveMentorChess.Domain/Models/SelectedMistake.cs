using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record SelectedMistake(
    IReadOnlyList<MoveAnalysisResult> Moves,
    MoveQualityBucket Quality,
    MistakeTag? Tag,
    MoveExplanation Explanation);
