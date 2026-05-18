using Avalonia.Controls;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisSimilarMistakesRenderer(
    TextBlock hintTextBlock,
    ListBox listBox,
    Func<IReadOnlyList<SelectedMistakeViewItem>> getVisibleItems,
    Action<SelectedMistakeViewItem> selectMistake)
{
    public void Clear()
    {
        hintTextBlock.Text = string.Empty;
        listBox.ItemsSource = null;
    }

    public void Refresh(MoveAnalysisResult lead, string label)
    {
        IReadOnlyList<SimilarMistakeLink> similar = AnalysisTimelinePresentation.BuildSimilarMistakeLinks(
            getVisibleItems(),
            lead,
            label);

        listBox.ItemsSource = similar;
        hintTextBlock.Text = AnalysisTimelinePresentation.BuildSimilarMistakesHint(similar.Count, label);
    }

    public void SelectCurrentLink()
    {
        if (listBox.SelectedItem is not SimilarMistakeLink link)
        {
            return;
        }

        selectMistake(link.Item);
        listBox.SelectedItem = null;
    }
}
