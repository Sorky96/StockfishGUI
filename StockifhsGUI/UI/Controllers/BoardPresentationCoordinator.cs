using System;
using System.Drawing;
using System.Linq;

namespace StockifhsGUI;

internal sealed class BoardPresentationCoordinator
{
    private readonly IBoardPresentationHost host;

    public BoardPresentationCoordinator(IBoardPresentationHost host)
    {
        this.host = host;
    }

    public void RefreshEngineSuggestions()
    {
        if (host.Engine is null)
        {
            host.BestMoveArrows.Clear();
            host.SuggestionLabel.Text = host.MissingEngineMessage;
            UpdateEvaluationDisplay(null);
            host.UpdateExtendedControls();
            InvalidateBoardSurface();
            return;
        }

        host.Engine.SetPositionFen(host.GetCurrentFen());

        host.BestMoveArrows.Clear();
        string[] topMoves = host.Engine.GetTopMoves(3)
            .Where(host.IsSuggestionLegal)
            .ToArray();

        Color[] colors = { Color.Blue, Color.Green, Color.Orange };
        foreach (string move in topMoves)
        {
            Point from = new(move[0] - 'a', 8 - (move[1] - '0'));
            Point to = new(move[2] - 'a', 8 - (move[3] - '0'));
            int colorIndex = Math.Min(host.BestMoveArrows.Count, colors.Length - 1);
            host.BestMoveArrows.Add(new BoardArrow(from, to, colors[colorIndex]));
        }

        host.SuggestionLabel.Text = topMoves.Length == 0
            ? "Top moves: none"
            : "Top moves: " + string.Join(", ", topMoves);
        UpdateEvaluationDisplay(host.Engine.GetEvaluationSummary());
        host.UpdateExtendedControls();
        InvalidateBoardSurface();
    }

    public void UpdateEvaluationDisplay(EvaluationSummary? evaluation)
    {
        host.CurrentEvaluation = evaluation;

        if (evaluation is null)
        {
            host.EvaluationLabel.Text = "Evaluation: unavailable";
            host.EvaluationBarFill.Width = host.EvaluationBarBackground.ClientSize.Width / 2;
            host.EvaluationBarFill.BackColor = Color.Silver;
            return;
        }

        if (evaluation.MateIn is int mateIn)
        {
            int signedMate = host.WhiteToMove ? mateIn : -mateIn;
            bool whiteWinning = signedMate > 0;
            host.EvaluationLabel.Text = whiteWinning
                ? $"Evaluation: White mates in {Math.Abs(signedMate)}"
                : $"Evaluation: Black mates in {Math.Abs(signedMate)}";
            host.EvaluationBarFill.Width = whiteWinning ? host.EvaluationBarBackground.ClientSize.Width : 0;
            host.EvaluationBarFill.BackColor = whiteWinning ? Color.WhiteSmoke : Color.FromArgb(30, 30, 30);
            return;
        }

        int cp = evaluation.Centipawns ?? 0;
        int whitePerspectiveCp = host.WhiteToMove ? cp : -cp;
        double pawnAdvantage = whitePerspectiveCp / 100.0;
        double normalized = Math.Clamp((pawnAdvantage + 5.0) / 10.0, 0.0, 1.0);

        host.EvaluationBarFill.Width = Math.Max(0, (int)Math.Round(host.EvaluationBarBackground.ClientSize.Width * normalized));
        host.EvaluationBarFill.BackColor = whitePerspectiveCp >= 0 ? Color.WhiteSmoke : Color.FromArgb(30, 30, 30);

        if (Math.Abs(pawnAdvantage) < 0.15)
        {
            host.EvaluationLabel.Text = "Evaluation: even";
        }
        else if (pawnAdvantage > 0)
        {
            host.EvaluationLabel.Text = $"Evaluation: White +{pawnAdvantage:F1}";
        }
        else
        {
            host.EvaluationLabel.Text = $"Evaluation: Black +{Math.Abs(pawnAdvantage):F1}";
        }
    }

    public void InvalidateBoardSurface()
    {
        SyncBoardSurface();
        host.BoardSurface.Invalidate();
    }

    private void SyncBoardSurface()
    {
        host.BoardSurface.TileSize = host.BoardTileSize;
        host.BoardSurface.RotateBoard = host.RotateBoard;
        host.BoardSurface.Board = host.Board;
        host.BoardSurface.SelectedSquare = host.SelectedSquare;
        host.BoardSurface.AnalysisTargetSquare = host.AnalysisTargetSquare;
        host.BoardSurface.PreviewTargetSquare = host.PreviewTargetSquare;
        host.BoardSurface.AvailableMoves = host.AvailableMoves.ToList();
        host.BoardSurface.Arrows = host.BestMoveArrows.Concat(host.AnalysisArrows).ToList();
        host.BoardSurface.PieceImages = new Dictionary<string, Image>(host.PieceImages);
    }
}
