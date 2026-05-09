namespace MoveMentorChess.Domain;

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
