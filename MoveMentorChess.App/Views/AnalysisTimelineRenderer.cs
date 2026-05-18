using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisTimelineRenderer(
    Grid timelineBandsGrid,
    UniformGrid timelineMarkersGrid,
    TextBlock timelineSelectedTextBlock,
    TextBlock timelineSummaryTextBlock,
    Action<SelectedMistakeViewItem> selectMistake)
{
    public void Clear(string message)
    {
        timelineBandsGrid.ColumnDefinitions.Clear();
        timelineBandsGrid.Children.Clear();
        timelineMarkersGrid.Children.Clear();
        timelineSummaryTextBlock.Text = message;
        timelineSelectedTextBlock.Text = string.Empty;
    }

    public void Render(
        GameAnalysisResult? result,
        IReadOnlyList<SelectedMistakeViewItem> visibleItems,
        SelectedMistakeViewItem? selectedItem,
        IReadOnlySet<int> reviewedPlies)
    {
        Clear(string.Empty);

        if (result is null || result.Replay.Count == 0)
        {
            timelineSummaryTextBlock.Text = "Run analysis to see game phases and mistake markers.";
            return;
        }

        List<PhaseSegment> segments = AnalysisTimelinePresentation.BuildPhaseSegments(result.Replay);
        RenderPhaseBands(segments);
        RenderMarkers(result, visibleItems, selectedItem, reviewedPlies);
        RenderSelectedText(selectedItem, reviewedPlies);
        RenderSummary(result, segments, reviewedPlies);
    }

    private void RenderPhaseBands(IReadOnlyList<PhaseSegment> segments)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            PhaseSegment segment = segments[i];
            timelineBandsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1, segment.PlyCount), GridUnitType.Star)));
            Border phaseBand = new()
            {
                Background = Brush.Parse(AnalysisTimelinePresentation.GetPhaseBrush(segment.Phase)),
                BorderBrush = Brush.Parse("#101820"),
                BorderThickness = new Avalonia.Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = AnalysisMistakePresentation.FormatPhase(segment.Phase),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(phaseBand, i);
            timelineBandsGrid.Children.Add(phaseBand);
        }
    }

    private void RenderMarkers(
        GameAnalysisResult result,
        IReadOnlyList<SelectedMistakeViewItem> visibleItems,
        SelectedMistakeViewItem? selectedItem,
        IReadOnlySet<int> reviewedPlies)
    {
        Dictionary<int, SelectedMistakeViewItem> markersByPly = visibleItems
            .GroupBy(item => item.LeadMove.Replay.Ply)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.Mistake.Quality)
                    .ThenByDescending(item => item.LeadMove.CentipawnLoss ?? 0)
                    .First());

        timelineMarkersGrid.Columns = Math.Max(1, result.Replay.Count);
        foreach (ReplayPly replay in result.Replay)
        {
            Border marker = CreateEmptyMarker();
            if (markersByPly.TryGetValue(replay.Ply, out SelectedMistakeViewItem? item))
            {
                ApplyMarkerState(marker, item, ReferenceEquals(selectedItem, item), reviewedPlies.Contains(item.LeadMove.Replay.Ply));
                marker.PointerPressed += (_, _) => selectMistake(item);
            }

            timelineMarkersGrid.Children.Add(marker);
        }
    }

    private static Border CreateEmptyMarker()
        => new()
        {
            Height = 12,
            Margin = new Avalonia.Thickness(1, 0),
            CornerRadius = new Avalonia.CornerRadius(2),
            Background = Brushes.Transparent
        };

    private static void ApplyMarkerState(Border marker, SelectedMistakeViewItem item, bool isSelected, bool isReviewed)
    {
        marker.Background = Brush.Parse(AnalysisTimelinePresentation.GetQualityBrush(item.Mistake.Quality));
        marker.BorderBrush = isSelected ? Brushes.White : isReviewed ? Brush.Parse("#9ED7A6") : Brush.Parse("#101820");
        marker.BorderThickness = new Avalonia.Thickness(isSelected ? 2 : 0);
        marker.Height = isSelected ? 22 : 12;
        marker.Margin = new Avalonia.Thickness(isSelected ? 0 : 1, 0);
        marker.Cursor = new Cursor(StandardCursorType.Hand);
        string reviewed = isReviewed ? " Reviewed." : string.Empty;
        ToolTip.SetTip(marker, $"{item.MoveRange}: {item.Mistake.Quality}, {item.LabelText}, {AnalysisMistakePresentation.BuildImpactText(item.LeadMove)}.{reviewed}");
    }

    private void RenderSelectedText(SelectedMistakeViewItem? selectedItem, IReadOnlySet<int> reviewedPlies)
    {
        if (selectedItem is null)
        {
            return;
        }

        string reviewed = reviewedPlies.Contains(selectedItem.LeadMove.Replay.Ply) ? " Reviewed." : string.Empty;
        timelineSelectedTextBlock.Text = $"You are here: {selectedItem.MoveRange} - {selectedItem.LabelText}, {AnalysisMistakePresentation.BuildImpactText(selectedItem.LeadMove)}.{reviewed}";
    }

    private void RenderSummary(GameAnalysisResult result, IEnumerable<PhaseSegment> segments, IReadOnlySet<int> reviewedPlies)
    {
        string phaseSummary = AnalysisTimelinePresentation.BuildPhaseSummary(segments);
        int reviewedCount = AnalysisTimelinePresentation.CountReviewedHighlights(result, reviewedPlies);
        int totalHighlights = result.HighlightedMistakes.Count;
        timelineSummaryTextBlock.Text = $"{phaseSummary}. Reviewed {reviewedCount}/{totalHighlights} highlights. Markers: red blunder, orange mistake, yellow inaccuracy.";
    }
}
