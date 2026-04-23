using System.Collections.Generic;

namespace StockifhsGUI;

public enum PlayerSide
{
    White,
    Black
}

public enum GamePhase
{
    Opening,
    Middlegame,
    Endgame
}

public enum MoveQualityBucket
{
    Good,
    Inaccuracy,
    Mistake,
    Blunder
}

public enum ExplanationLevel
{
    Beginner,
    Intermediate,
    Advanced
}

public enum AdviceNarrationStyle
{
    RegularTrainer,
    LevyRozman,
    HikaruNakamura,
    BotezLive,
    WittyAlien
}

public enum GameAnalysisProgressStage
{
    BeforeMove,
    AfterMove
}

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
    string? Site);

public sealed record SavedImportedGameSummary(
    string GameFingerprint,
    string DisplayTitle,
    string? WhitePlayer,
    string? BlackPlayer,
    string? DateText,
    string? Result,
    string? Eco,
    string? Site,
    DateTime UpdatedUtc);

public sealed record ReplayPly(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string NormalizedSan,
    string Uci,
    string FenBefore,
    string FenAfter,
    string PlacementFenBefore,
    string PlacementFenAfter,
    GamePhase Phase,
    string MovingPiece,
    string? PromotionPiece,
    string FromSquare,
    string ToSquare,
    bool IsCapture,
    bool IsEnPassant,
    bool IsCastle);

public sealed record EngineAnalysisOptions(int Depth = 14, int MultiPv = 3, int? MoveTimeMs = null);

public sealed record EngineLine(string MoveUci, int? Centipawns, int? MateIn, IReadOnlyList<string> Pv);

public sealed record EngineAnalysis(string Fen, IReadOnlyList<EngineLine> Lines, string? BestMoveUci);

public sealed record MistakeTag(string Label, double Confidence, IReadOnlyList<string> Evidence);

public sealed record MoveExplanation(string ShortText, string TrainingHint, string DetailedText = "");

public sealed record MoveHeuristicContext(
    bool MovedPieceHangingAfterMove,
    bool MovedPieceFreeToTake,
    bool MovedPieceLikelyLosesExchange,
    int MovedPieceAttackDeficit,
    int? MovedPieceValueCp,
    int? MovedPieceMobilityBefore,
    int? MovedPieceMobilityAfter,
    bool MovedPieceToEdge,
    bool CastledKingWingPawnPush,
    bool EarlyQueenMove,
    bool EarlyRookMove,
    bool EarlyKingMoveWithoutCastling,
    bool EdgePawnPush,
    bool BestMoveIsCapture,
    bool BestMoveIsCastle,
    bool BestMoveDevelopsMinorPiece,
    bool BestMoveImprovesPieceActivity,
    int? BestMoveMaterialSwingCp,
    int? PlayedLineMaterialSwingCp,
    int DevelopedMinorPiecesBefore,
    int DevelopedMinorPiecesAfter,
    int BestMoveDevelopedMinorPiecesAfter,
    bool CastledBeforeMove,
    bool CastledAfterMove,
    bool KingLeftCastledShelter,
    bool KingCentralizedBeforeMove,
    bool KingCentralizedAfterMove,
    bool BestMoveCentralizesKing,
    bool BestMoveImprovesKingActivity);

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

public sealed record SelectedMistake(
    IReadOnlyList<MoveAnalysisResult> Moves,
    MoveQualityBucket Quality,
    MistakeTag? Tag,
    MoveExplanation Explanation);

public sealed record OpeningBranchReference(
    string? Eco,
    string OpeningName,
    string BranchLabel,
    string Source,
    bool UsedFallback);

public sealed record OpeningCriticalMoment(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string Uci,
    MoveQualityBucket Quality,
    int? CentipawnLoss,
    string? MistakeLabel,
    string Trigger,
    string BranchLabel);

public sealed record OpeningPhaseReview(
    OpeningBranchReference Branch,
    OpeningCriticalMoment? TheoryExit,
    OpeningCriticalMoment? FirstSignificantMistake);

public sealed record GameAnalysisResult(
    ImportedGame Game,
    PlayerSide AnalyzedSide,
    IReadOnlyList<ReplayPly> Replay,
    IReadOnlyList<MoveAnalysisResult> MoveAnalyses,
    IReadOnlyList<SelectedMistake> HighlightedMistakes,
    OpeningPhaseReview? OpeningReview = null);

public sealed record GameAnalysisProgress(
    ReplayPly Replay,
    string Fen,
    GameAnalysisProgressStage Stage,
    int CurrentAnalyzedMove,
    int TotalAnalyzedMoves);

public sealed record StoredMoveAnalysis(
    string GameFingerprint,
    PlayerSide AnalyzedSide,
    int Depth,
    int MultiPv,
    int? MoveTimeMs,
    DateTime AnalysisUpdatedUtc,
    string? WhitePlayer,
    string? BlackPlayer,
    string? DateText,
    string? Result,
    string? Eco,
    string? Site,
    int Ply,
    int MoveNumber,
    string San,
    string Uci,
    string FenBefore,
    string FenAfter,
    GamePhase Phase,
    int? EvalBeforeCp,
    int? EvalAfterCp,
    int? BestMateIn,
    int? PlayedMateIn,
    int? CentipawnLoss,
    MoveQualityBucket Quality,
    int MaterialDeltaCp,
    string? BestMoveUci,
    string? MistakeLabel,
    double? MistakeConfidence,
    IReadOnlyList<string> Evidence,
    string? ShortExplanation,
    string? DetailedExplanation,
    string? TrainingHint,
    bool IsHighlighted);
