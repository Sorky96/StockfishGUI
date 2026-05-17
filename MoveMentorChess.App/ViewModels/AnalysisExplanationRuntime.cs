using MoveMentorChess.Analysis;

namespace MoveMentorChess.App.ViewModels;

internal sealed record AnalysisExplanationRequest(
    ExplanationLevel Level,
    AdviceNarrationStyle NarrationStyle,
    string CacheKey);

internal static class AnalysisExplanationRuntime
{
    public static AnalysisExplanationRequest CreateRequest(MoveAnalysisResult lead)
    {
        LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
        return new AnalysisExplanationRequest(
            settings.DefaultExplanationLevel,
            settings.NarrationStyle,
            BuildCacheKey(lead, settings.DefaultExplanationLevel, settings.NarrationStyle));
    }

    public static string BuildCacheKey(
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        AdviceNarrationStyle narrationStyle)
        => $"{lead.Replay.Ply}:{lead.Replay.Uci}:{explanationLevel}:{narrationStyle}";
}
