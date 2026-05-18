using MoveMentorChess.Analysis;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

internal sealed record AnalysisExplanationRequest(
    ExplanationLevel Level,
    AdviceNarrationStyle NarrationStyle,
    string CacheKey);

internal sealed record AnalysisPreparedExplanation(
    AnalysisExplanationRequest Request,
    MoveExplanation Explanation,
    bool IsCached);

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

internal sealed class AnalysisExplanationService
{
    private readonly Dictionary<string, MoveExplanation> explanationCache = [];
    private IAdviceGenerator adviceGenerator = new SettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
    private int requestId;

    public AdviceRuntimeStatus RefreshRuntimeState()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        adviceGenerator = new SettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
        return status;
    }

    public AnalysisPreparedExplanation Prepare(MoveAnalysisResult lead)
    {
        AnalysisExplanationRequest request = AnalysisExplanationRuntime.CreateRequest(lead);
        MoveExplanation explanation = lead.Explanation
            ?? new MoveExplanation("Explanation is loading...", "Training hint is loading...");

        bool isCached = explanationCache.TryGetValue(request.CacheKey, out MoveExplanation? cachedExplanation);
        if (isCached && cachedExplanation is not null)
        {
            explanation = cachedExplanation;
        }

        return new AnalysisPreparedExplanation(request, explanation, isCached);
    }

    public int BeginRequest()
        => ++requestId;

    public void InvalidatePendingRequests()
    {
        requestId++;
    }

    public async Task<MoveExplanation?> GenerateAndCacheAsync(
        ImportedGame importedGame,
        MoveAnalysisResult lead,
        PlayerSide? analyzedSide,
        AnalysisExplanationRequest request,
        int activeRequestId)
    {
        MoveExplanation explanation;
        try
        {
            explanation = await Task.Run(() => adviceGenerator.Generate(
                lead.Replay,
                lead.Quality,
                lead.MistakeTag,
                lead.BeforeAnalysis.BestMoveUci,
                lead.CentipawnLoss,
                request.Level,
                new AdviceGenerationContext(
                    "avalonia-analysis-window",
                    GameFingerprint.Compute(importedGame.PgnText),
                    analyzedSide,
                    NarrationStyle: request.NarrationStyle)));
        }
        catch (Exception ex)
        {
            explanation = new MoveExplanation(
                "Local advice generation failed.",
                "Use the engine lines and training hint for now.",
                ex.Message);
        }

        if (activeRequestId != requestId)
        {
            return null;
        }

        explanationCache[request.CacheKey] = explanation;
        return explanation;
    }
}
