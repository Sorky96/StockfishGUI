using System.Drawing;

namespace StockifhsGUI;

internal sealed class AnalysisNavigationCoordinator
{
    private readonly IAnalysisNavigationHost host;

    public AnalysisNavigationCoordinator(IAnalysisNavigationHost host)
    {
        this.host = host;
    }

    public void NavigateToMistake(MoveAnalysisResult moveAnalysis)
    {
        if (host.ImportedSession.Game is null || host.ImportedSession.Moves.Count == 0)
        {
            return;
        }

        int targetIndex = moveAnalysis.Replay.Ply - 1;
        if (targetIndex < 0 || targetIndex >= host.ImportedSession.Moves.Count)
        {
            return;
        }

        host.ReplayImportedMovesThrough(targetIndex);

        host.AnalysisArrows.Clear();
        host.AnalysisTargetSquare = null;
        AddAnalysisArrow(moveAnalysis.Replay.Uci, Color.Crimson);
        if (!string.IsNullOrWhiteSpace(moveAnalysis.BeforeAnalysis.BestMoveUci))
        {
            AddAnalysisArrow(moveAnalysis.BeforeAnalysis.BestMoveUci, Color.DeepSkyBlue);
        }

        host.AnalysisTargetSquare = ChessMoveDisplayHelper.TryParseUciMove(moveAnalysis.Replay.Uci, out _, out Point to)
            ? to
            : null;

        host.SetSuggestionText(
            $"Analysis focus: {ChessMoveDisplayHelper.FormatSanAndUci(moveAnalysis.Replay.San, moveAnalysis.Replay.Uci)} | best {ChessMoveDisplayHelper.FormatBestMoveLabel(moveAnalysis)}");
        host.InvalidateBoardSurface();
    }

    private void AddAnalysisArrow(string uciMove, Color color)
    {
        if (!ChessMoveDisplayHelper.TryParseUciMove(uciMove, out Point from, out Point to))
        {
            return;
        }

        host.AnalysisArrows.Add(new BoardArrow(from, to, color));
    }
}
