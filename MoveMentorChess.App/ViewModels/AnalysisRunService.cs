using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;
using MoveMentorChess.Opening;

namespace MoveMentorChess.App.ViewModels;

internal static class AnalysisRunService
{
    public static Task<GameAnalysisResult> AnalyzeImportedGameAsync(
        IEngineAnalyzer engineAnalyzer,
        ImportedGame importedGame,
        PlayerSide side,
        EngineAnalysisOptions analysisOptions,
        Action<GameAnalysisProgress>? analysisProgress,
        OpeningTheoryQueryService? openingTheory)
    {
        ArgumentNullException.ThrowIfNull(engineAnalyzer);
        ArgumentNullException.ThrowIfNull(importedGame);
        ArgumentNullException.ThrowIfNull(analysisOptions);

        GameAnalysisService analysisService = new(
            engineAnalyzer,
            adviceGenerator: new SettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateBulkAnalysisGenerator()),
            openingTheory: openingTheory);
        IProgress<GameAnalysisProgress>? progress = analysisProgress is null
            ? null
            : new Progress<GameAnalysisProgress>(analysisProgress);

        return Task.Run(() => analysisService.AnalyzeGame(importedGame, side, analysisOptions, progress));
    }
}
