using MoveMentorChess.Analysis;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.ViewModels;

internal static class AnalysisFeedbackRecorder
{
    public static string Record(
        IAnalysisWindowDataService dataService,
        ImportedGame importedGame,
        PlayerSide analyzedSide,
        EngineAnalysisOptions analysisOptions,
        SelectedMistakeViewItem item,
        AdviceFeedbackKind feedbackKind,
        string? correctedLabel,
        string? comment)
    {
        DateTime timestampUtc = dataService.UtcNow;
        MoveAdviceFeedback feedback = AnalysisFeedbackService.CreateFeedback(
            importedGame,
            analyzedSide,
            analysisOptions,
            item,
            timestampUtc,
            feedbackKind,
            correctedLabel,
            comment);

        dataService.SaveMoveAdviceFeedback(feedback);
        AdviceFeedbackEntry entry = AnalysisFeedbackService.CreateFeedbackLogEntry(feedback, item, timestampUtc);
        AdviceFeedbackLogger.CreateDefault().Record(entry);
        return $"Feedback saved: {AnalysisFeedbackService.FormatFeedbackKind(feedbackKind)}.";
    }
}
