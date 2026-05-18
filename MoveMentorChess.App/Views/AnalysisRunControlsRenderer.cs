using Avalonia.Controls;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisRunControlsRenderer(
    Button analyzeButton,
    Button testAdviceButton,
    ComboBox sideComboBox,
    ComboBox qualityFilterComboBox,
    Button showOnBoardButton)
{
    public void ApplyInteractionState(bool canRunAnalysis, bool isAnalysisRunning, bool canUseSelectedMistake)
    {
        analyzeButton.IsEnabled = canRunAnalysis;
        testAdviceButton.IsEnabled = !isAnalysisRunning;
        sideComboBox.IsEnabled = !isAnalysisRunning;
        qualityFilterComboBox.IsEnabled = !isAnalysisRunning;
        showOnBoardButton.IsEnabled = canUseSelectedMistake;
    }
}
