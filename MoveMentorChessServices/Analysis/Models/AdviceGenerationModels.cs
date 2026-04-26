namespace MoveMentorChessServices;

public enum AdviceGeneratorMode
{
    Template,
    Adaptive
}

public sealed record AdviceGenerationSettings(
    AdviceGeneratorMode Mode,
    int MaxShortTextLength = 220,
    int MaxTrainingHintLength = 220,
    int MaxDetailedTextLength = 540)
{
    public static AdviceGenerationSettings Default { get; } = new(AdviceGeneratorMode.Adaptive);
}

public sealed record LocalAdviceModelOptions(
    string Command,
    string? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutMs = 45000);

public sealed record AdvicePromptContext(
    string? OpeningName = null,
    string? AnalyzedPlayer = null,
    string? OpponentPlayer = null,
    string? BestMoveSan = null,
    IReadOnlyList<string>? Evidence = null,
    IReadOnlyList<string>? HeuristicNotes = null,
    PlayerMistakeProfile? PlayerProfile = null);

public sealed record LocalModelAdviceRequest(
    ReplayPly Replay,
    MoveQualityBucket Quality,
    MistakeTag? Tag,
    string? BestMoveUci,
    int? CentipawnLoss,
    ExplanationLevel ExplanationLevel,
    AdviceGenerationContext? Context,
    string Prompt,
    AdviceNarrationStyle NarrationStyle = AdviceNarrationStyle.RegularTrainer,
    IReadOnlyList<string>? JsonOutputKeys = null);

public sealed record LocalModelAdviceResponse(
    string ShortText,
    string TrainingHint,
    string DetailedText);

public sealed record AdviceGenerationContext(
    string Source,
    string? GameFingerprint,
    PlayerSide? AnalyzedSide = null,
    AdvicePromptContext? PromptContext = null,
    AdviceNarrationStyle? NarrationStyle = null);

public sealed record AdviceGenerationTrace(
    DateTime TimestampUtc,
    string GeneratorName,
    AdviceGeneratorMode Mode,
    bool UsedFallback,
    string? FallbackReason,
    string Source,
    string? GameFingerprint,
    PlayerSide? AnalyzedSide,
    ExplanationLevel ExplanationLevel,
    MoveQualityBucket Quality,
    string Label,
    string PlayedSan,
    string? BestMoveUci,
    int? CentipawnLoss,
    string ShortText,
    string DetailedText,
    string TrainingHint);
