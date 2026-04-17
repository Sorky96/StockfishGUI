namespace StockifhsGUI;

public sealed class GameAnalysisService
{
    private readonly IEngineAnalyzer engineAnalyzer;
    private readonly GameReplayService replayService;
    private readonly MistakeClassifier mistakeClassifier;
    private readonly ExplanationGenerator explanationGenerator;
    private readonly MistakeSelector mistakeSelector;

    public GameAnalysisService(
        IEngineAnalyzer engineAnalyzer,
        GameReplayService? replayService = null,
        MistakeClassifier? mistakeClassifier = null,
        ExplanationGenerator? explanationGenerator = null,
        MistakeSelector? mistakeSelector = null)
    {
        this.engineAnalyzer = engineAnalyzer ?? throw new ArgumentNullException(nameof(engineAnalyzer));
        this.replayService = replayService ?? new GameReplayService();
        this.mistakeClassifier = mistakeClassifier ?? new MistakeClassifier();
        this.explanationGenerator = explanationGenerator ?? new ExplanationGenerator();
        this.mistakeSelector = mistakeSelector ?? new MistakeSelector();
    }

    public GameAnalysisResult AnalyzeGame(ImportedGame game, PlayerSide analyzedSide, EngineAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);
        List<MoveAnalysisResult> moveAnalyses = new();

        foreach (ReplayPly ply in replay.Where(item => item.Side == analyzedSide))
        {
            EngineAnalysis beforeAnalysis = engineAnalyzer.AnalyzePosition(ply.FenBefore, options);
            EngineAnalysis afterAnalysis = engineAnalyzer.AnalyzePosition(ply.FenAfter, options);

            EngineLine? bestLine = beforeAnalysis.Lines.FirstOrDefault();
            EngineLine? playedLine = afterAnalysis.Lines.FirstOrDefault();

            ScoreSnapshot bestScore = NormalizeScore(bestLine, analyzedSide, ply.Side);
            ScoreSnapshot playedScore = NormalizeScore(playedLine, analyzedSide, Opponent(ply.Side));

            int materialBefore = PositionInspector.MaterialScore(ply.FenBefore, analyzedSide);
            int materialAfter = PositionInspector.MaterialScore(ply.FenAfter, analyzedSide);
            int materialDelta = materialAfter - materialBefore;
            int? centipawnLoss = ComputeCentipawnLoss(bestScore, playedScore);
            MoveQualityBucket quality = ClassifyQuality(bestScore, playedScore, centipawnLoss);

            MistakeTag? tag = mistakeClassifier.Classify(ply, analyzedSide, quality, centipawnLoss, materialDelta);
            MoveExplanation? explanation = quality == MoveQualityBucket.Good
                ? null
                : explanationGenerator.Generate(ply, quality, tag, bestLine?.MoveUci, centipawnLoss);

            moveAnalyses.Add(new MoveAnalysisResult(
                ply,
                beforeAnalysis,
                afterAnalysis,
                bestScore.Centipawns,
                playedScore.Centipawns,
                bestScore.MateIn,
                playedScore.MateIn,
                centipawnLoss,
                quality,
                materialDelta,
                tag,
                explanation));
        }

        IReadOnlyList<SelectedMistake> highlightedMistakes = mistakeSelector.Select(moveAnalyses);
        return new GameAnalysisResult(game, analyzedSide, replay, moveAnalyses, highlightedMistakes);
    }

    private static ScoreSnapshot NormalizeScore(EngineLine? line, PlayerSide analyzedSide, PlayerSide sideToMove)
    {
        if (line is null)
        {
            return new ScoreSnapshot(null, null);
        }

        int sign = analyzedSide == sideToMove ? 1 : -1;
        return new ScoreSnapshot(
            line.Centipawns is int cp ? cp * sign : null,
            line.MateIn is int mate ? mate * sign : null);
    }

    private static int? ComputeCentipawnLoss(ScoreSnapshot best, ScoreSnapshot played)
    {
        if (best.Centipawns is not int bestCp || played.Centipawns is not int playedCp)
        {
            return null;
        }

        return Math.Max(0, bestCp - playedCp);
    }

    private static MoveQualityBucket ClassifyQuality(ScoreSnapshot best, ScoreSnapshot played, int? centipawnLoss)
    {
        if (best.MateIn is > 0 && played.MateIn is null)
        {
            return MoveQualityBucket.Blunder;
        }

        if (best.MateIn is > 0 && played.MateIn is <= 0)
        {
            return MoveQualityBucket.Blunder;
        }

        if (played.MateIn is < 0)
        {
            return MoveQualityBucket.Blunder;
        }

        int loss = centipawnLoss ?? 0;
        if (loss > 300)
        {
            return MoveQualityBucket.Blunder;
        }

        if (loss > 150)
        {
            return MoveQualityBucket.Mistake;
        }

        if (loss > 80)
        {
            return MoveQualityBucket.Inaccuracy;
        }

        return MoveQualityBucket.Good;
    }

    private static PlayerSide Opponent(PlayerSide side) => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;

    private readonly record struct ScoreSnapshot(int? Centipawns, int? MateIn);
}
