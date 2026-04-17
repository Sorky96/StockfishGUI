using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1
{
    private readonly Dictionary<string, IReadOnlyList<PieceMoveOption>> pieceMoveOptionsCache = new();
    private Label? pieceMoveOptionsLabel;
    private ListBox? pieceMoveOptionsList;
    private int pieceMoveOptionsRequestId;
    private Point? pieceMoveOptionTargetSquare;

    private void InitializePieceMoveOptionsControls()
    {
        pieceMoveOptionsLabel = new Label
        {
            AutoSize = false,
            Location = new Point(TileSize * GridSize + 20, 560),
            Size = new Size(260, 36),
            Text = "Selected piece: none"
        };
        Controls.Add(pieceMoveOptionsLabel);

        pieceMoveOptionsList = new ListBox
        {
            Location = new Point(TileSize * GridSize + 20, 600),
            Size = new Size(260, 148),
            Font = new Font("Consolas", 9)
        };
        pieceMoveOptionsList.SelectedIndexChanged += (_, _) => UpdatePieceMoveOptionPreview();
        Controls.Add(pieceMoveOptionsList);
        ClearPieceMoveOptions();
    }

    private void UpdateSelectedPieceMoveOptions(
        string currentFen,
        Point selectedPoint,
        IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        if (pieceMoveOptionsLabel is null || pieceMoveOptionsList is null)
        {
            return;
        }

        string fromSquare = ToUCI(selectedPoint);
        string pieceName = board[selectedPoint.X, selectedPoint.Y] ?? "?";
        pieceMoveOptionsLabel.Text = $"Selected piece: {pieceName} from {fromSquare} | legal moves: {movesForPiece.Count}";
        pieceMoveOptionTargetSquare = null;
        pieceMoveOptionsList.Items.Clear();

        if (movesForPiece.Count == 0)
        {
            pieceMoveOptionsList.Items.Add("No legal moves for this piece.");
            Invalidate();
            return;
        }

        foreach (LegalMoveInfo move in movesForPiece.OrderBy(item => item.San, StringComparer.Ordinal))
        {
            pieceMoveOptionsList.Items.Add(new PieceMoveOptionListItem(
                new PieceMoveOption(move.San, move.Uci, null, null, false),
                $"{FormatSanAndUci(move.San, move.Uci),-18} | analyzing..."));
        }

        string cacheKey = BuildPieceMoveOptionsCacheKey(currentFen, fromSquare);
        if (pieceMoveOptionsCache.TryGetValue(cacheKey, out IReadOnlyList<PieceMoveOption>? cachedOptions))
        {
            ApplyPieceMoveOptions(cachedOptions, movesForPiece.Count, pieceName, fromSquare, appendCachedMarker: true);
            return;
        }

        if (engine is null)
        {
            ApplyPieceMoveOptions(
                movesForPiece.Select(move => new PieceMoveOption(move.San, move.Uci, null, null, false)).ToList(),
                movesForPiece.Count,
                pieceName,
                fromSquare,
                appendCachedMarker: false);
            return;
        }

        int requestId = ++pieceMoveOptionsRequestId;
        _ = AnalyzePieceMoveOptionsAsync(currentFen, fromSquare, pieceName, movesForPiece.ToList(), requestId);
    }

    private async Task AnalyzePieceMoveOptionsAsync(
        string currentFen,
        string fromSquare,
        string pieceName,
        IReadOnlyList<LegalMoveInfo> movesForPiece,
        int requestId)
    {
        IReadOnlyList<PieceMoveOption> analyzedOptions;
        try
        {
            analyzedOptions = await Task.Run(() => BuildPieceMoveOptions(currentFen, movesForPiece));
        }
        catch (Exception ex)
        {
            analyzedOptions =
            [
                new PieceMoveOption("analysis error", string.Empty, null, null, false, $"Could not analyze moves: {ex.Message}")
            ];
        }

        if (IsDisposed || requestId != pieceMoveOptionsRequestId)
        {
            return;
        }

        string cacheKey = BuildPieceMoveOptionsCacheKey(currentFen, fromSquare);
        pieceMoveOptionsCache[cacheKey] = analyzedOptions;
        if (!IsHandleCreated)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || requestId != pieceMoveOptionsRequestId)
            {
                return;
            }

            ApplyPieceMoveOptions(analyzedOptions, movesForPiece.Count, pieceName, fromSquare, appendCachedMarker: false);
        }));
    }

    private IReadOnlyList<PieceMoveOption> BuildPieceMoveOptions(string currentFen, IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        if (engine is null)
        {
            return movesForPiece
                .Select(move => new PieceMoveOption(move.San, move.Uci, null, null, false))
                .ToList();
        }

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

    private void ApplyPieceMoveOptions(
        IReadOnlyList<PieceMoveOption> options,
        int moveCount,
        string pieceName,
        string fromSquare,
        bool appendCachedMarker)
    {
        if (pieceMoveOptionsLabel is null || pieceMoveOptionsList is null)
        {
            return;
        }

        string cachedSuffix = appendCachedMarker ? " | cached" : string.Empty;
        string? selectedUci = pieceMoveOptionsList.SelectedItem is PieceMoveOptionListItem selectedItem
            ? selectedItem.Option.Uci
            : null;
        pieceMoveOptionsLabel.Text = $"Selected piece: {pieceName} from {fromSquare} | legal moves: {moveCount}{cachedSuffix}";
        pieceMoveOptionsList.Items.Clear();

        foreach (PieceMoveOption option in options)
        {
            pieceMoveOptionsList.Items.Add(new PieceMoveOptionListItem(option, FormatPieceMoveOption(option)));
        }

        if (!string.IsNullOrWhiteSpace(selectedUci))
        {
            for (int i = 0; i < pieceMoveOptionsList.Items.Count; i++)
            {
                if (pieceMoveOptionsList.Items[i] is PieceMoveOptionListItem optionItem
                    && string.Equals(optionItem.Option.Uci, selectedUci, StringComparison.OrdinalIgnoreCase))
                {
                    pieceMoveOptionsList.SelectedIndex = i;
                    break;
                }
            }
        }

        UpdatePieceMoveOptionPreview();
    }

    private void ClearPieceMoveOptions()
    {
        pieceMoveOptionsRequestId++;
        pieceMoveOptionTargetSquare = null;
        if (pieceMoveOptionsLabel is not null)
        {
            pieceMoveOptionsLabel.Text = "Selected piece: none";
        }

        if (pieceMoveOptionsList is not null)
        {
            pieceMoveOptionsList.Items.Clear();
            pieceMoveOptionsList.Items.Add("Select a piece to inspect all legal moves.");
        }

        Invalidate();
    }

    private void UpdatePieceMoveOptionPreview()
    {
        if (pieceMoveOptionsList?.SelectedItem is PieceMoveOptionListItem optionItem
            && TryParseUciMove(optionItem.Option.Uci, out _, out Point to))
        {
            pieceMoveOptionTargetSquare = to;
        }
        else
        {
            pieceMoveOptionTargetSquare = null;
        }

        Invalidate();
    }

    private static string BuildPieceMoveOptionsCacheKey(string fen, string fromSquare)
    {
        return $"{fen}|{fromSquare}";
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

        string bestMarker = option.IsBestMove ? "* " : "  ";
        string scoreText = FormatEvaluatedScore(option.Score);
        string deltaText = option.DeltaCp is int delta
            ? (delta >= 0 ? $"+{delta}" : delta.ToString())
            : "n/a";

        return $"{bestMarker}{FormatSanAndUci(option.San, option.Uci),-18} | {scoreText,-10} | d {deltaText}";
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

    private sealed record PieceMoveOption(
        string San,
        string Uci,
        EvaluatedScore? Score,
        int? DeltaCp,
        bool IsBestMove,
        string? ErrorText = null);

    private sealed record PieceMoveOptionListItem(PieceMoveOption Option, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record EvaluatedScore(int? Centipawns, int? MateIn);
}
