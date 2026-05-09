using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record GameAnalysisResult(
    ImportedGame Game,
    PlayerSide AnalyzedSide,
    IReadOnlyList<ReplayPly> Replay,
    IReadOnlyList<MoveAnalysisResult> MoveAnalyses,
    IReadOnlyList<SelectedMistake> HighlightedMistakes,
    OpeningPhaseReview? OpeningReview = null);
