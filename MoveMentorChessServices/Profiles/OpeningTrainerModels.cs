namespace MoveMentorChessServices;

public enum OpeningTrainingMode
{
    LineRecall,
    MistakeRepair,
    BranchAwareness
}

public enum OpeningTrainingSourceKind
{
    ExampleGame,
    OpeningWeakness,
    FirstOpeningMistake
}

public enum OpeningTrainingMoveRole
{
    Expected,
    Repair,
    Alternative,
    Continuation,
    Historical
}

public enum OpeningLineRecallGrade
{
    Correct,
    Playable,
    Wrong
}

public enum OpeningMistakeRepairGrade
{
    Correct,
    Playable,
    Wrong
}

public enum OpeningTrainingScore
{
    Correct,
    Playable,
    Wrong
}

public enum OpeningLineRecallReferenceKind
{
    BetterMove,
    BestMove,
    ReferenceLine,
    HistoricalGame
}

public enum OpeningTrainingMoveSourceKind
{
    UserGame,
    EngineBestMove,
    OpeningBook,
    EcoReference
}

public sealed record OpeningTrainingSessionOptions(
    IReadOnlyList<OpeningTrainingMode>? Modes = null,
    IReadOnlyList<OpeningTrainingSourceKind>? Sources = null,
    int MaxPositions = 18,
    int MaxPositionsPerSource = 6,
    int MaxContinuationMoves = 6,
    IReadOnlyList<string>? TargetOpenings = null);

public sealed record OpeningTrainingSession(
    string SessionId,
    string PlayerKey,
    string DisplayName,
    DateTime CreatedUtc,
    IReadOnlyList<OpeningTrainingMode> SupportedModes,
    IReadOnlyList<OpeningTrainingSourceKind> IncludedSources,
    IReadOnlyList<OpeningTrainingSourceSummary> SourceSummaries,
    IReadOnlyList<OpeningTrainingLine> Lines,
    IReadOnlyList<OpeningTrainingPosition> Positions);

public sealed record OpeningTrainingSourceSummary(
    OpeningTrainingSourceKind SourceKind,
    int PositionCount,
    int LineCount,
    IReadOnlyList<string> RelatedOpenings);

public sealed record OpeningTrainingLine(
    string LineId,
    OpeningTrainingSourceKind SourceKind,
    string Eco,
    string OpeningName,
    string StartFen,
    int AnchorPly,
    int AnchorMoveNumber,
    PlayerSide SideToMove,
    string AnchorLabel,
    IReadOnlyList<OpeningTrainingMove> Moves,
    OpeningTrainingReference Reference);

public sealed record OpeningTrainingPosition(
    string PositionId,
    OpeningTrainingMode Mode,
    OpeningTrainingSourceKind SourceKind,
    string Eco,
    string OpeningName,
    string Fen,
    int Ply,
    int MoveNumber,
    PlayerSide SideToMove,
    string Prompt,
    string Instruction,
    int Priority,
    string? ThemeLabel,
    string? PlayedMove,
    string? BetterMove,
    string? BetterMoveReason,
    IReadOnlyList<string> Tags,
    IReadOnlyList<OpeningTrainingMoveOption> CandidateMoves,
    IReadOnlyList<OpeningTrainingMove> Continuation,
    OpeningTrainingReference Reference,
    string? LineId = null,
    IReadOnlyList<OpeningTrainingBranch>? Branches = null,
    string? BranchSelectionSummary = null);

public sealed record OpeningTrainingMove(
    int Ply,
    int MoveNumber,
    PlayerSide Side,
    string San,
    string? Uci,
    OpeningTrainingMoveRole Role,
    bool IsPreferred = false,
    string? Note = null);

public sealed record OpeningTrainingMoveOption(
    string DisplayText,
    string? Uci,
    OpeningTrainingMoveRole Role,
    bool IsPreferred,
    string? Note = null,
    OpeningLineRecallReferenceKind? ReferenceKind = null,
    OpeningTrainingMoveSourceKind SourceKind = OpeningTrainingMoveSourceKind.UserGame);

public enum OpeningTrainingBranchSourceKind
{
    ExampleGame,
    RecurringMistake,
    SavedContinuation
}

public sealed record OpeningTrainingBranchSourceStat(
    OpeningTrainingBranchSourceKind SourceKind,
    int Count);

public sealed record OpeningTrainingBranch(
    string OpponentMove,
    string? OpponentMoveUci,
    int Frequency,
    string SourceSummary,
    OpeningTrainingMoveOption? RecommendedResponse,
    IReadOnlyList<OpeningTrainingMove> Continuation,
    IReadOnlyList<OpeningTrainingBranchSourceStat> SourceStats);

public sealed record OpeningTrainingReference(
    string GameFingerprint,
    PlayerSide Side,
    string OpponentName,
    string? DateText,
    string? Result,
    string SourceLabel,
    int? FirstMistakePly,
    string? MistakeLabel);

public sealed record OpeningTrainingAttemptResult(
    string PositionId,
    OpeningTrainingMode Mode,
    OpeningTrainingSourceKind PositionSource,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    IReadOnlyList<OpeningTrainingMoveOption> ExpectedMoves,
    OpeningTrainingScore Score,
    string ShortExplanation,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);

public sealed record OpeningLineRecallAttemptResult(
    string PositionId,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    OpeningLineRecallGrade Grade,
    string Summary,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);

public sealed record OpeningMistakeRepairAttemptResult(
    string PositionId,
    string SubmittedMoveText,
    string? ResolvedSan,
    string? ResolvedUci,
    OpeningMistakeRepairGrade Grade,
    string Summary,
    string BetterMoveSummary,
    string WhyBetter,
    IReadOnlyList<OpeningTrainingMoveOption> MatchingReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PreferredReferences,
    IReadOnlyList<OpeningTrainingMoveOption> PlayableReferences);
