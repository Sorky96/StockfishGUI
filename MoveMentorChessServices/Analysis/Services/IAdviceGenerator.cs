namespace MoveMentorChessServices;

public interface IAdviceGenerator
{
    MoveExplanation Generate(
        ReplayPly replay,
        MoveQualityBucket quality,
        MistakeTag? tag,
        string? bestMoveUci,
        int? centipawnLoss,
        ExplanationLevel level = ExplanationLevel.Intermediate,
        AdviceGenerationContext? context = null);
}
