namespace MoveMentorChess.Presentation.Models;

public sealed record AnalysisSelectedDetailsPresentation(
    string EffectiveLabel,
    string MoveText,
    string BestMoveText,
    string QualityText,
    string LossText,
    string EvalSwingText,
    string EvalInterpretationText,
    string ContextText,
    string AdviceText,
    string WhyText,
    string TrainingHintText,
    string ReviewActionText,
    string TopCandidatesText,
    string ChecklistText,
    string DetailsText);

public static class AnalysisSelectedDetailsPresenter
{
    public static AnalysisSelectedDetailsPresentation Build(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        OpeningPhaseReview? openingReview,
        MoveExplanation explanation,
        bool isLoading,
        MoveAdviceFeedback? feedback)
    {
        string effectiveLabel = feedback?.CorrectedLabel ?? mistake.Tag?.Label ?? "unclassified";
        return new AnalysisSelectedDetailsPresentation(
            effectiveLabel,
            AnalysisMistakePresentation.BuildMoveRange(mistake),
            AnalysisDetailsTextFormatter.FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci),
            $"{mistake.Quality} - {AnalysisMistakePresentation.FormatMistakeLabel(effectiveLabel)}",
            $"Evaluation loss: {lead.CentipawnLoss?.ToString() ?? "n/a"} cp",
            AnalysisCoachingTextFormatter.BuildEvalSwingText(lead),
            AnalysisCoachingTextFormatter.BuildEvalInterpretation(lead),
            AnalysisSnapshotTextFormatter.BuildPositionContextText(lead, effectiveLabel),
            AnalysisCoachingTextFormatter.TakeFirstSentences(AnalysisCoachingTextFormatter.SimplifyAdviceText(explanation.ShortText), 2),
            AnalysisCoachingTextFormatter.BuildReadableWhyText(lead, explanation),
            AnalysisCoachingTextFormatter.TakeFirstSentences(AnalysisCoachingTextFormatter.SimplifyAdviceText(explanation.TrainingHint), 2),
            AnalysisCoachingTextFormatter.BuildReviewActionText(lead, effectiveLabel),
            AnalysisCoachingTextFormatter.BuildTopCandidateMovesText(lead),
            AnalysisSnapshotTextFormatter.BuildBeforeMoveChecklistText(effectiveLabel),
            AnalysisDetailsTextFormatter.BuildDetailsText(mistake, lead, openingReview, explanation, isLoading, feedback));
    }
}
