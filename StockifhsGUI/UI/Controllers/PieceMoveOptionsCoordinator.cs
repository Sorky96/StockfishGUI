using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StockifhsGUI;

internal sealed class PieceMoveOptionsCoordinator
{
    private readonly Dictionary<string, IReadOnlyList<PieceMoveOption>> cache = new();

    public IReadOnlyList<PieceMoveOption> CreatePendingOptions(IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        return movesForPiece
            .OrderBy(item => item.San, StringComparer.Ordinal)
            .Select(move => PieceMoveOption.Pending(move.San, move.Uci))
            .ToList();
    }

    public IReadOnlyList<PieceMoveOption> CreateFallbackOptions(IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        return movesForPiece
            .Select(move => new PieceMoveOption(move.San, move.Uci, null, null, false))
            .ToList();
    }

    public bool TryGetCachedOptions(string fen, string fromSquare, out IReadOnlyList<PieceMoveOption>? cachedOptions)
    {
        return cache.TryGetValue(BuildCacheKey(fen, fromSquare), out cachedOptions);
    }

    public void StoreOptions(string fen, string fromSquare, IReadOnlyList<PieceMoveOption> options)
    {
        cache[BuildCacheKey(fen, fromSquare)] = options;
    }

    public Task<IReadOnlyList<PieceMoveOption>> AnalyzeAsync(
        string currentFen,
        IReadOnlyList<LegalMoveInfo> movesForPiece,
        StockfishEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        return Task.Run(() => BuildPieceMoveOptions(currentFen, movesForPiece, engine));
    }

    public string BuildHeaderText(string pieceName, string fromSquare, int moveCount, bool appendCachedMarker)
    {
        string cachedSuffix = appendCachedMarker ? " | cached" : string.Empty;
        return $"Selected piece: {pieceName} from {fromSquare} | legal moves: {moveCount}{cachedSuffix}";
    }

    public IReadOnlyList<PieceMoveOptionListItem> BuildDisplayItems(IReadOnlyList<PieceMoveOption> options)
    {
        return options
            .Select(option => new PieceMoveOptionListItem(option, FormatPieceMoveOption(option)))
            .ToList();
    }

    public int FindSelectionIndex(IReadOnlyList<PieceMoveOptionListItem> items, string? selectedUci)
    {
        if (string.IsNullOrWhiteSpace(selectedUci))
        {
            return -1;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Option.Uci, selectedUci, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string BuildCacheKey(string fen, string fromSquare)
    {
        return $"{fen}|{fromSquare}";
    }

    private static IReadOnlyList<PieceMoveOption> BuildPieceMoveOptions(
        string currentFen,
        IReadOnlyList<LegalMoveInfo> movesForPiece,
        StockfishEngine engine)
    {
        ChessGame baselineGame = new();
        if (!baselineGame.TryLoadFen(currentFen, out _))
        {
            return movesForPiece
                .Select(move => new PieceMoveOption(move.San, move.Uci, null, null, false))
                .ToList();
        }

        EngineAnalysisOptions options = new(Depth: 10, MultiPv: 1, MoveTimeMs: 90);
        PlayerSide perspective = baselineGame.WhiteToMove ? PlayerSide.White : PlayerSide.Black;
        EngineAnalysis currentAnalysis = engine.AnalyzePosition(currentFen, options);
        EvaluatedScore baseline = NormalizeScore(currentAnalysis.Lines.FirstOrDefault(), perspective, perspective);
        string? bestMoveUci = currentAnalysis.BestMoveUci;

        List<PieceMoveOption> optionsForPiece = new();
        foreach (LegalMoveInfo move in movesForPiece)
        {
            ChessGame analysisGame = new();
            if (!analysisGame.TryLoadFen(currentFen, out _)
                || !analysisGame.TryApplyUci(move.Uci, out AppliedMoveInfo? appliedMove, out _)
                || appliedMove is null)
            {
                optionsForPiece.Add(new PieceMoveOption(move.San, move.Uci, null, null, string.Equals(move.Uci, bestMoveUci, StringComparison.OrdinalIgnoreCase)));
                continue;
            }

            EngineAnalysis moveAnalysis = engine.AnalyzePosition(appliedMove.FenAfter, options);
            EvaluatedScore moveScore = NormalizeScore(moveAnalysis.Lines.FirstOrDefault(), perspective, Opponent(perspective));
            optionsForPiece.Add(new PieceMoveOption(
                move.San,
                move.Uci,
                moveScore,
                ComputeDelta(baseline, moveScore),
                string.Equals(move.Uci, bestMoveUci, StringComparison.OrdinalIgnoreCase)));
        }

        engine.SetPositionFen(currentFen);

        return optionsForPiece
            .OrderByDescending(option => ScoreForSorting(option.Score))
            .ThenBy(option => option.San, StringComparer.Ordinal)
            .ToList();
    }

    private static EvaluatedScore NormalizeScore(EngineLine? line, PlayerSide perspective, PlayerSide sideToMove)
    {
        if (line is null)
        {
            return new EvaluatedScore(null, null);
        }

        int sign = perspective == sideToMove ? 1 : -1;
        return new EvaluatedScore(
            line.Centipawns is int cp ? cp * sign : null,
            line.MateIn is int mate ? mate * sign : null);
    }

    private static int? ComputeDelta(EvaluatedScore baseline, EvaluatedScore moveScore)
    {
        if (baseline.Centipawns is not int baselineCp || moveScore.Centipawns is not int moveCp)
        {
            return null;
        }

        return moveCp - baselineCp;
    }

    private static int ScoreForSorting(EvaluatedScore? score)
    {
        if (score?.MateIn is int mate)
        {
            return mate > 0
                ? 100000 - (Math.Abs(mate) * 1000)
                : -100000 + (Math.Abs(mate) * 1000);
        }

        return score?.Centipawns ?? int.MinValue / 2;
    }

    private static string FormatPieceMoveOption(PieceMoveOption option)
    {
        if (!string.IsNullOrWhiteSpace(option.ErrorText))
        {
            return option.ErrorText;
        }

        if (option.IsPending)
        {
            return $"{ChessMoveDisplayHelper.FormatSanAndUci(option.San, option.Uci),-18} | analyzing...";
        }

        string bestMarker = option.IsBestMove ? "* " : "  ";
        string scoreText = FormatEvaluatedScore(option.Score);
        string deltaText = option.DeltaCp is int delta
            ? (delta >= 0 ? $"+{delta}" : delta.ToString())
            : "n/a";

        return $"{bestMarker}{ChessMoveDisplayHelper.FormatSanAndUci(option.San, option.Uci),-18} | {scoreText,-10} | d {deltaText}";
    }

    private static string FormatEvaluatedScore(EvaluatedScore? score)
    {
        if (score?.MateIn is int mate)
        {
            return $"mate {mate}";
        }

        if (score?.Centipawns is int cp)
        {
            double pawns = cp / 100.0;
            return pawns >= 0 ? $"+{pawns:0.0}" : $"{pawns:0.0}";
        }

        return "n/a";
    }

    private static PlayerSide Opponent(PlayerSide side) => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;
}
