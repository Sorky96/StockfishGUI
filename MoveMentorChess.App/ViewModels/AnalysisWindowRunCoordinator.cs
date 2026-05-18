using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;

namespace MoveMentorChess.App.ViewModels;

internal sealed record AnalysisWindowRunOutcome(
    GameAnalysisResult? Result,
    bool IsCached,
    string StatusText,
    string? ErrorMessage)
{
    public bool HasResult => Result is not null;

    public bool IsError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static AnalysisWindowRunOutcome MissingContext()
        => new(null, IsCached: false, "Analysis window is missing required game context.", ErrorMessage: null);

    public static AnalysisWindowRunOutcome Cached(GameAnalysisResult result, string statusText)
        => new(result, IsCached: true, statusText, ErrorMessage: null);

    public static AnalysisWindowRunOutcome Completed(GameAnalysisResult result, PlayerSide side)
        => new(result, IsCached: false, $"Analysis finished for {side}.", ErrorMessage: null);

    public static AnalysisWindowRunOutcome Failed(Exception exception)
        => new(null, IsCached: false, $"Analysis failed: {exception.Message}", exception.Message);
}

internal sealed class AnalysisWindowRunCoordinator(
    IAnalysisWindowDataService dataService,
    IReadOnlyDictionary<PlayerSide, GameAnalysisResult> initialResultsBySide,
    Action<GameAnalysisProgress>? analysisProgress)
{
    public AnalysisWindowRunOutcome TryLoadCached(
        ImportedGame? importedGame,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions)
    {
        if (importedGame is null)
        {
            return AnalysisWindowRunOutcome.MissingContext();
        }

        return dataService.TryLoadExistingResult(
            importedGame,
            side,
            analysisOptions,
            initialResultsBySide,
            out GameAnalysisResult? cachedResult,
            out _,
            out string cacheStatus)
            && cachedResult is not null
                ? AnalysisWindowRunOutcome.Cached(cachedResult, cacheStatus)
                : new AnalysisWindowRunOutcome(null, IsCached: false, cacheStatus, ErrorMessage: null);
    }

    public async Task<AnalysisWindowRunOutcome> AnalyzeAsync(
        ImportedGame? importedGame,
        IEngineAnalyzer? engineAnalyzer,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions)
    {
        if (importedGame is null || engineAnalyzer is null)
        {
            return AnalysisWindowRunOutcome.MissingContext();
        }

        if (dataService.TryLoadExistingResult(
            importedGame,
            side,
            analysisOptions,
            initialResultsBySide,
            out GameAnalysisResult? cachedResult,
            out GameAnalysisCacheKey cacheKey,
            out string cacheStatus)
            && cachedResult is not null)
        {
            return AnalysisWindowRunOutcome.Cached(cachedResult, cacheStatus);
        }

        try
        {
            GameAnalysisResult result = await AnalysisRunService.AnalyzeImportedGameAsync(
                engineAnalyzer,
                importedGame,
                side,
                analysisOptions,
                analysisProgress,
                dataService.CreateOpeningTheory());
            dataService.StoreResult(cacheKey, result);
            return AnalysisWindowRunOutcome.Completed(result, side);
        }
        catch (Exception ex)
        {
            return AnalysisWindowRunOutcome.Failed(ex);
        }
    }
}
