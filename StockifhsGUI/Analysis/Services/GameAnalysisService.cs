namespace StockifhsGUI;

public sealed class GameAnalysisService
{
    private readonly IEngineAnalyzer engineAnalyzer;
    private readonly GameReplayService replayService;
    private readonly MistakeClassifier mistakeClassifier;
    private readonly IAdviceGenerator adviceGenerator;
    private readonly MistakeSelector mistakeSelector;

    public GameAnalysisService(
        IEngineAnalyzer engineAnalyzer,
        GameReplayService? replayService = null,
        MistakeClassifier? mistakeClassifier = null,
        IAdviceGenerator? adviceGenerator = null,
        MistakeSelector? mistakeSelector = null)
    {
        this.engineAnalyzer = engineAnalyzer ?? throw new ArgumentNullException(nameof(engineAnalyzer));
        this.replayService = replayService ?? new GameReplayService();
        this.mistakeClassifier = mistakeClassifier ?? new MistakeClassifier();
        this.adviceGenerator = adviceGenerator ?? AdviceGeneratorFactory.CreateDefault();
        this.mistakeSelector = mistakeSelector ?? new MistakeSelector();
    }

    public GameAnalysisResult AnalyzeGame(ImportedGame game, PlayerSide analyzedSide, EngineAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<ReplayPly> replay = replayService.Replay(game);
        List<MoveAnalysisResult> moveAnalyses = new();
        Dictionary<EngineCacheKey, EngineAnalysis> analysisCache = new();

        foreach (ReplayPly ply in replay.Where(item => item.Side == analyzedSide))
        {
            EngineAnalysis beforeAnalysis = AnalyzeCached(ply.FenBefore, options, analysisCache);
            EngineAnalysis afterAnalysis = AnalyzeCached(ply.FenAfter, options, analysisCache);

            EngineLine? bestLine = beforeAnalysis.Lines.FirstOrDefault();
            EngineLine? playedLine = afterAnalysis.Lines.FirstOrDefault();

            ScoreSnapshot bestScore = NormalizeScore(bestLine, analyzedSide, ply.Side);
            ScoreSnapshot playedScore = NormalizeScore(playedLine, analyzedSide, Opponent(ply.Side));

            int materialBefore = PositionInspector.MaterialScore(ply.FenBefore, analyzedSide);
            int materialAfter = PositionInspector.MaterialScore(ply.FenAfter, analyzedSide);
            int materialDelta = materialAfter - materialBefore;
            int? centipawnLoss = ComputeCentipawnLoss(bestScore, playedScore);
            MoveQualityBucket quality = ClassifyQuality(bestScore, playedScore, centipawnLoss);
            MoveHeuristicContext heuristicContext = BuildHeuristicContext(ply, analyzedSide, beforeAnalysis, afterAnalysis);

            MistakeTag? tag = mistakeClassifier.Classify(ply, analyzedSide, quality, centipawnLoss, materialDelta, heuristicContext);
            MoveExplanation? explanation = quality == MoveQualityBucket.Good
                ? null
                : adviceGenerator.Generate(
                    ply,
                    quality,
                    tag,
                    bestLine?.MoveUci,
                    centipawnLoss,
                    context: new AdviceGenerationContext(
                        "game-analysis-service",
                        GameFingerprint.Compute(game.PgnText),
                        analyzedSide));

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

    private EngineAnalysis AnalyzeCached(
        string fen,
        EngineAnalysisOptions options,
        IDictionary<EngineCacheKey, EngineAnalysis> analysisCache)
    {
        EngineCacheKey cacheKey = new(NormalizeFenForCache(fen), options.Depth, options.MultiPv, options.MoveTimeMs);
        if (analysisCache.TryGetValue(cacheKey, out EngineAnalysis? cached))
        {
            return cached;
        }

        EngineAnalysis analysis = engineAnalyzer.AnalyzePosition(fen, options);
        analysisCache[cacheKey] = analysis;
        return analysis;
    }

    private static string NormalizeFenForCache(string fen)
    {
        string[] parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4
            ? string.Join(' ', parts.Take(4))
            : fen;
    }

    private static MoveHeuristicContext BuildHeuristicContext(
        ReplayPly replay,
        PlayerSide analyzedSide,
        EngineAnalysis beforeAnalysis,
        EngineAnalysis afterAnalysis)
    {
        char movedPiece = char.ToLowerInvariant(replay.MovingPiece[0]);
        char fromFile = replay.FromSquare[0];
        AppliedMoveInfo? bestMove = TryApplyBestMove(replay.FenBefore, beforeAnalysis.BestMoveUci);
        PositionInspector.MaterialSwingSummary? bestLineSwing = PositionInspector.AnalyzeMaterialSwingAlongLine(
            replay.FenBefore,
            analyzedSide,
            beforeAnalysis.Lines.FirstOrDefault()?.Pv);
        PositionInspector.MaterialSwingSummary? playedLineSwing = PositionInspector.AnalyzeMaterialSwingAlongLine(
            replay.FenAfter,
            analyzedSide,
            afterAnalysis.Lines.FirstOrDefault()?.Pv);

        int? bestMoveMaterialSwing = bestLineSwing?.BestDeltaCp
            ?? (bestMove is null
                ? null
                : PositionInspector.MaterialScore(bestMove.FenAfter, analyzedSide) - PositionInspector.MaterialScore(bestMove.FenBefore, analyzedSide));
        int developedMinorPiecesBefore = PositionInspector.CountDevelopedMinorPieces(replay.FenBefore, analyzedSide);
        int developedMinorPiecesAfter = PositionInspector.CountDevelopedMinorPieces(replay.FenAfter, analyzedSide);
        bool castledBeforeMove = PositionInspector.IsKingOnCastledWing(replay.FenBefore, analyzedSide);
        bool castledAfterMove = PositionInspector.IsKingOnCastledWing(replay.FenAfter, analyzedSide);
        bool kingCentralizedBeforeMove = PositionInspector.IsKingCentralized(replay.FenBefore, analyzedSide);
        bool kingCentralizedAfterMove = PositionInspector.IsKingCentralized(replay.FenAfter, analyzedSide);
        bool bestMoveCentralizesKing = bestMove is not null
            && PositionInspector.IsKingCentralized(bestMove.FenAfter, analyzedSide);
        PositionInspector.SquareSafetySummary? movedPieceSafety = PositionInspector.AnalyzeSquareSafety(replay.FenAfter, replay.ToSquare, analyzedSide);
        int? movedPieceMobilityBefore = PositionInspector.CountPieceMobility(replay.FenBefore, replay.FromSquare, analyzedSide);
        int? movedPieceMobilityAfter = PositionInspector.CountPieceMobility(replay.FenAfter, replay.ToSquare, analyzedSide);

        return new MoveHeuristicContext(
            movedPieceSafety?.IsHanging == true,
            movedPieceSafety?.IsFreeToTake == true,
            movedPieceSafety?.LikelyLosesExchange == true,
            movedPieceSafety is null ? 0 : movedPieceSafety.Value.Attackers - movedPieceSafety.Value.Defenders,
            movedPieceSafety?.PieceValueCp,
            movedPieceMobilityBefore,
            movedPieceMobilityAfter,
            PositionInspector.IsEdgeSquare(replay.ToSquare),
            movedPiece == 'p' && fromFile is 'f' or 'g' or 'h' && castledBeforeMove,
            replay.Phase == GamePhase.Opening && movedPiece == 'q',
            replay.Phase == GamePhase.Opening && movedPiece == 'r',
            replay.Phase == GamePhase.Opening && movedPiece == 'k' && !replay.IsCastle,
            replay.Phase == GamePhase.Opening && movedPiece == 'p' && fromFile is 'a' or 'b' or 'g' or 'h',
            bestMove?.IsCapture == true,
            bestMoveMaterialSwing,
            playedLineSwing?.WorstDeltaCp,
            developedMinorPiecesBefore,
            developedMinorPiecesAfter,
            castledBeforeMove,
            castledAfterMove,
            kingCentralizedBeforeMove,
            kingCentralizedAfterMove,
            bestMoveCentralizesKing);
    }

    private static AppliedMoveInfo? TryApplyBestMove(string fenBefore, string? bestMoveUci)
    {
        if (string.IsNullOrWhiteSpace(bestMoveUci))
        {
            return null;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(bestMoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return null;
        }

        return appliedMove;
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
    private readonly record struct EngineCacheKey(string Fen, int Depth, int MultiPv, int? MoveTimeMs);
}
