using Avalonia.Controls;
using Avalonia.Media;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisSnapshotRenderer(
    Border positionSnapshotPanel,
    ChessBoardView positionSnapshotBoard,
    Border positionSafetyBadgeBorder,
    TextBlock positionSafetyBadgeTextBlock,
    TextBlock positionThreatTextBlock,
    TextBlock positionBestIdeaTextBlock,
    TextBlock positionMistakeTextBlock,
    Button snapshotPlayedButton,
    Button snapshotBestButton,
    Button snapshotThreatButton)
{
    private MoveAnalysisResult? snapshotLead;
    private string snapshotLabel = "unclassified";
    private AnalysisSnapshotMode snapshotMode = AnalysisSnapshotMode.Played;

    public void UpdateBoardSize()
    {
        double panelWidth = positionSnapshotPanel.Bounds.Width;
        if (panelWidth <= 0)
        {
            return;
        }

        double availableWidth = Math.Max(0, panelWidth - positionSnapshotPanel.Padding.Left - positionSnapshotPanel.Padding.Right);
        double boardSize = Math.Clamp(availableWidth * 0.96, 280, 420);
        positionSnapshotBoard.Width = boardSize;
        positionSnapshotBoard.Height = boardSize;
    }

    public void Reset()
    {
        positionSnapshotBoard.Fen = null;
        positionSnapshotBoard.Arrows = [];
        positionSnapshotBoard.SelectedSquare = null;
        positionSnapshotBoard.PreviewTargetSquare = null;
        positionSafetyBadgeBorder.Background = Brush.Parse("#263A49");
        positionSafetyBadgeTextBlock.Text = string.Empty;
        positionThreatTextBlock.Text = string.Empty;
        positionBestIdeaTextBlock.Text = string.Empty;
        positionMistakeTextBlock.Text = string.Empty;
        snapshotLead = null;
        snapshotLabel = "unclassified";
        snapshotMode = AnalysisSnapshotMode.Played;
        UpdateModeButtons();
    }

    public void Show(MoveAnalysisResult lead, string label)
    {
        snapshotLead = lead;
        snapshotLabel = label;
        snapshotMode = AnalysisSnapshotMode.Played;
        Render();
    }

    public void SetMode(AnalysisSnapshotMode mode)
    {
        snapshotMode = mode;
        Render();
    }

    private void Render()
    {
        if (snapshotLead is not MoveAnalysisResult lead)
        {
            return;
        }

        positionSnapshotBoard.Fen = snapshotMode == AnalysisSnapshotMode.Best
            ? lead.Replay.FenBefore
            : lead.Replay.FenAfter;
        positionSnapshotBoard.RotateBoard = lead.Replay.Side == PlayerSide.Black;
        positionSnapshotBoard.SelectedSquare = snapshotMode == AnalysisSnapshotMode.Played ? lead.Replay.ToSquare : null;
        positionSnapshotBoard.PreviewTargetSquare = null;
        positionSnapshotBoard.Arrows = AnalysisSnapshotPresentation.BuildSnapshotArrows(lead, snapshotMode)
            .Select(arrow => new BoardArrowViewModel(arrow.FromSquare, arrow.ToSquare, Color.Parse(arrow.ColorHex)))
            .ToList();
        (string safetyText, string safetyBrush) = AnalysisSnapshotPresentation.BuildMovedPieceSafetyBadge(lead);
        positionSafetyBadgeTextBlock.Text = snapshotMode == AnalysisSnapshotMode.Played
            ? safetyText
            : snapshotMode == AnalysisSnapshotMode.Best
                ? "Best move view"
                : "Threat view";
        positionSafetyBadgeBorder.Background = Brush.Parse(snapshotMode == AnalysisSnapshotMode.Played ? safetyBrush : "#263A49");
        positionThreatTextBlock.Text = AnalysisSnapshotPresentation.BuildSnapshotThreatText(lead, snapshotLabel, snapshotMode);
        positionBestIdeaTextBlock.Text = AnalysisSnapshotPresentation.BuildBestMoveIdeaText(lead);
        positionMistakeTextBlock.Text = AnalysisSnapshotPresentation.BuildPlayerMistakeText(lead, snapshotLabel);
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        SetModeButtonState(snapshotPlayedButton, snapshotMode == AnalysisSnapshotMode.Played);
        SetModeButtonState(snapshotBestButton, snapshotMode == AnalysisSnapshotMode.Best);
        SetModeButtonState(snapshotThreatButton, snapshotMode == AnalysisSnapshotMode.Threat);
    }

    private static void SetModeButtonState(Button button, bool isActive)
    {
        button.Background = Brush.Parse(isActive ? "#2F6FB3" : "#263A49");
        button.Foreground = Brushes.White;
    }
}
