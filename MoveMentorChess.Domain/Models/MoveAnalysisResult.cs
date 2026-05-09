using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record MoveAnalysisResult(
    ReplayPly Replay,
    EngineAnalysis BeforeAnalysis,
    EngineAnalysis AfterAnalysis,
    int? EvalBeforeCp,
    int? EvalAfterCp,
    int? BestMateIn,
    int? PlayedMateIn,
    int? CentipawnLoss,
    MoveQualityBucket Quality,
    int MaterialDeltaCp,
    MistakeTag? MistakeTag,
    MoveExplanation? Explanation);
