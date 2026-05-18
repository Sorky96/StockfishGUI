using MoveMentorChess.Analysis;
using MoveMentorChess.Persistence;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.ViewModels;

internal static class AnalysisFeedbackService
{
    public static MoveAdviceFeedback CreateFeedback(
        ImportedGame importedGame,
        PlayerSide analyzedSide,
        EngineAnalysisOptions analysisOptions,
        SelectedMistakeViewItem item,
        DateTime timestampUtc,
        AdviceFeedbackKind feedbackKind,
        string? correctedLabel,
        string? comment)
    {
        MoveAnalysisResult lead = item.LeadMove;
        string gameFingerprint = GameFingerprint.Compute(importedGame.PgnText);
        GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(importedGame, analyzedSide, analysisOptions);

        return new MoveAdviceFeedback(
            Guid.NewGuid().ToString("N"),
            timestampUtc,
            gameFingerprint,
            analyzedSide,
            key.Depth,
            key.MultiPv,
            key.MoveTimeMs,
            lead.Replay.Ply,
            lead.Replay.MoveNumber,
            lead.Replay.San,
            lead.Replay.Uci,
            lead.Replay.FenBefore,
            lead.Replay.FenAfter,
            lead.EvalBeforeCp,
            lead.EvalAfterCp,
            lead.BeforeAnalysis.BestMoveUci,
            item.RawLabel,
            lead.MistakeTag?.Confidence ?? item.Mistake.Tag?.Confidence,
            lead.MistakeTag?.Evidence ?? item.Mistake.Tag?.Evidence ?? [],
            item.Mistake.Quality,
            lead.CentipawnLoss,
            feedbackKind,
            correctedLabel,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            "analysis-window");
    }

    public static AdviceFeedbackEntry CreateFeedbackLogEntry(
        MoveAdviceFeedback feedback,
        SelectedMistakeViewItem item,
        DateTime timestampUtc)
    {
        return new AdviceFeedbackEntry(
            timestampUtc,
            feedback.GameFingerprint,
            feedback.Ply,
            feedback.FeedbackKind,
            item.RawLabel,
            item.Mistake.Quality,
            item.LeadMove.CentipawnLoss,
            UsedFallback: false,
            item.LeadMove.Replay.San,
            item.LeadMove.Replay.Uci,
            item.LeadMove.BeforeAnalysis.BestMoveUci,
            "analysis-window");
    }

    public static MoveAdviceFeedback? FindLatestFeedback(
        IAnalysisStore? store,
        ImportedGame importedGame,
        PlayerSide analyzedSide,
        EngineAnalysisOptions analysisOptions,
        MoveAnalysisResult lead)
    {
        if (store is null)
        {
            return null;
        }

        GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(importedGame, analyzedSide, analysisOptions);
        return store.ListMoveAdviceFeedback(limit: 2000)
            .Where(feedback =>
                feedback.GameFingerprint == key.GameFingerprint
                && feedback.AnalyzedSide == key.Side
                && feedback.Depth == key.Depth
                && feedback.MultiPv == key.MultiPv
                && feedback.MoveTimeMs == key.MoveTimeMs
                && feedback.Ply == lead.Replay.Ply)
            .OrderByDescending(feedback => feedback.TimestampUtc)
            .ThenByDescending(feedback => feedback.FeedbackId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static string? NormalizeManualLabel(string? customLabel, string? selectedLabel)
    {
        string candidate = string.IsNullOrWhiteSpace(customLabel) ? selectedLabel ?? string.Empty : customLabel;
        candidate = candidate.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    public static string FormatFeedbackKind(AdviceFeedbackKind kind)
    {
        return kind switch
        {
            AdviceFeedbackKind.Correct => "Helpful",
            AdviceFeedbackKind.WrongLabel => "Wrong diagnosis",
            AdviceFeedbackKind.NotUseful => "Not useful",
            AdviceFeedbackKind.TooGeneric => "Needs clearer explanation",
            AdviceFeedbackKind.GoodExplanation => "Good explanation",
            _ => kind.ToString()
        };
    }
}
