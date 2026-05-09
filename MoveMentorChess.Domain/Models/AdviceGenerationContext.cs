namespace MoveMentorChess.Domain;

public sealed record AdviceGenerationContext(
    string Source,
    string? GameFingerprint,
    PlayerSide? AnalyzedSide = null,
    AdvicePromptContext? PromptContext = null,
    AdviceNarrationStyle? NarrationStyle = null);
