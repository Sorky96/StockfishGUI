using Avalonia.Controls;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisRunControlsRenderer(
    Button analyzeButton,
    Button testAdviceButton,
    ComboBox sideComboBox,
    ComboBox qualityFilterComboBox,
    Button showOnBoardButton)
{
    public void SetAnalysisRunning()
    {
        analyzeButton.IsEnabled = false;
        testAdviceButton.IsEnabled = false;
        sideComboBox.IsEnabled = false;
        qualityFilterComboBox.IsEnabled = false;
        showOnBoardButton.IsEnabled = false;
    }

    public void SetAnalysisIdle(bool canAnalyze)
    {
        analyzeButton.IsEnabled = canAnalyze;
        testAdviceButton.IsEnabled = true;
        sideComboBox.IsEnabled = true;
        qualityFilterComboBox.IsEnabled = true;
    }

    public void SetSelectionAvailable(bool hasSelection)
    {
        showOnBoardButton.IsEnabled = hasSelection;
    }
}
