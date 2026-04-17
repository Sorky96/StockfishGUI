using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1
{
    private Task OpenImportedGameAnalysisAsync()
    {
        if (importedGame is null || importedGame.SanMoves.Count == 0)
        {
            MessageBox.Show("Import a PGN game before starting analysis.", "Analyze Imported Game", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        if (engine is null)
        {
            MessageBox.Show(MissingEngineMessage, "Analyze Imported Game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.CompletedTask;
        }

        using GameAnalysisForm analysisForm = new(importedGame, engine, NavigateToAnalysisMistake);
        analysisForm.ShowDialog(this);
        return Task.CompletedTask;
    }

    private void OpenPlayerProfiles()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            MessageBox.Show("Local analysis storage is unavailable on this machine.", "Player Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using PlayerProfilesForm profilesForm = new(new PlayerProfileService(store));
        profilesForm.ShowDialog(this);
    }

    private void NavigateToAnalysisMistake(MoveAnalysisResult moveAnalysis)
    {
        if (importedGame is null || importedMoves.Count == 0)
        {
            return;
        }

        int targetIndex = moveAnalysis.Replay.Ply - 1;
        if (targetIndex < 0 || targetIndex >= importedMoves.Count)
        {
            return;
        }

        ReplayImportedMovesThrough(targetIndex);

        analysisArrows.Clear();
        analysisTargetSquare = null;
        AddAnalysisArrow(moveAnalysis.Replay.Uci, Color.Crimson);
        if (!string.IsNullOrWhiteSpace(moveAnalysis.BeforeAnalysis.BestMoveUci))
        {
            AddAnalysisArrow(moveAnalysis.BeforeAnalysis.BestMoveUci, Color.DeepSkyBlue);
        }
        SetAnalysisTargetSquare(moveAnalysis.Replay.Uci);

        suggestionLabel.Text = $"Analysis focus: {FormatSanAndUci(moveAnalysis.Replay.San, moveAnalysis.Replay.Uci)} | best {FormatBestMoveLabel(moveAnalysis)}";
        Invalidate();
    }

    private void AddAnalysisArrow(string uciMove, Color color)
    {
        if (!TryParseUciMove(uciMove, out Point from, out Point to))
        {
            return;
        }

        analysisArrows.Add(new AnalysisArrow(from, to, color));
    }

    private void SetAnalysisTargetSquare(string uciMove)
    {
        if (!TryParseUciMove(uciMove, out _, out Point to))
        {
            analysisTargetSquare = null;
            return;
        }

        analysisTargetSquare = to;
    }

    private static bool TryParseUciMove(string? uciMove, out Point from, out Point to)
    {
        from = default;
        to = default;

        if (string.IsNullOrWhiteSpace(uciMove) || uciMove.Length < 4)
        {
            return false;
        }

        if (!TryParseUciSquare(uciMove[..2], out from) || !TryParseUciSquare(uciMove.Substring(2, 2), out to))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseUciSquare(string square, out Point point)
    {
        point = default;
        if (square.Length != 2)
        {
            return false;
        }

        char file = char.ToLowerInvariant(square[0]);
        char rank = square[1];
        if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
        {
            return false;
        }

        point = new Point(file - 'a', 8 - (rank - '0'));
        return true;
    }

    private static string FormatBestMoveLabel(MoveAnalysisResult moveAnalysis)
    {
        string? bestMoveUci = moveAnalysis.BeforeAnalysis.BestMoveUci;
        if (string.IsNullOrWhiteSpace(bestMoveUci))
        {
            return "(unknown)";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(moveAnalysis.Replay.FenBefore, out _)
            || !game.TryApplyUci(bestMoveUci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return bestMoveUci;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static string FormatSanAndUci(string san, string uci)
    {
        return string.Equals(san, uci, StringComparison.OrdinalIgnoreCase)
            ? san
            : $"{san} ({uci})";
    }
}
