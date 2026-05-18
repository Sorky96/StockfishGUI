using Avalonia.Controls;
using MoveMentorChess.App.ViewModels;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.Views;

internal sealed class AnalysisDetailsFeedbackRenderer(
    TextBlock detailMoveTextBlock,
    TextBlock detailBestMoveTextBlock,
    TextBlock detailQualityTextBlock,
    TextBlock detailLossTextBlock,
    TextBlock detailEvalSwingTextBlock,
    TextBlock detailEvalInterpretationTextBlock,
    TextBlock detailContextTextBlock,
    TextBlock detailAdviceTextBlock,
    TextBlock detailWhyTextBlock,
    TextBlock detailTrainingHintTextBlock,
    TextBlock detailReviewActionTextBlock,
    TextBlock reviewStatusTextBlock,
    Button markReviewedButton,
    Button markReviewedNextButton,
    TextBlock detailTopCandidatesTextBlock,
    TextBlock detailChecklistTextBlock,
    TextBlock detailsTextBlock,
    Button correctFeedbackButton,
    Button wrongLabelFeedbackButton,
    Button notUsefulFeedbackButton,
    Button tooGenericFeedbackButton,
    Button goodExplanationFeedbackButton,
    Button saveManualCorrectionButton,
    Panel manualCorrectionPanel,
    ComboBox correctedLabelComboBox,
    TextBox customLabelTextBox,
    TextBox feedbackCommentTextBox)
{
    public void ShowPlaceholder(string message)
    {
        detailMoveTextBlock.Text = "No move selected";
        detailBestMoveTextBlock.Text = string.Empty;
        detailQualityTextBlock.Text = string.Empty;
        detailLossTextBlock.Text = string.Empty;
        detailEvalSwingTextBlock.Text = string.Empty;
        detailEvalInterpretationTextBlock.Text = string.Empty;
        detailContextTextBlock.Text = string.Empty;
        detailAdviceTextBlock.Text = message;
        detailWhyTextBlock.Text = string.Empty;
        detailTrainingHintTextBlock.Text = string.Empty;
        detailReviewActionTextBlock.Text = string.Empty;
        reviewStatusTextBlock.Text = string.Empty;
        markReviewedButton.IsEnabled = false;
        markReviewedNextButton.IsEnabled = false;
        detailTopCandidatesTextBlock.Text = string.Empty;
        detailChecklistTextBlock.Text = string.Empty;
        detailsTextBlock.Text = string.Empty;
        SetFeedbackButtonsEnabled(false);
    }

    public void ShowDetails(AnalysisSelectedDetailsPresentation details)
    {
        detailMoveTextBlock.Text = details.MoveText;
        detailBestMoveTextBlock.Text = details.BestMoveText;
        detailQualityTextBlock.Text = details.QualityText;
        detailLossTextBlock.Text = details.LossText;
        detailEvalSwingTextBlock.Text = details.EvalSwingText;
        detailEvalInterpretationTextBlock.Text = details.EvalInterpretationText;
        detailContextTextBlock.Text = details.ContextText;
        detailAdviceTextBlock.Text = details.AdviceText;
        detailWhyTextBlock.Text = details.WhyText;
        detailTrainingHintTextBlock.Text = details.TrainingHintText;
        detailReviewActionTextBlock.Text = details.ReviewActionText;
        detailTopCandidatesTextBlock.Text = details.TopCandidatesText;
        detailChecklistTextBlock.Text = details.ChecklistText;
        detailsTextBlock.Text = details.DetailsText;
    }

    public void ShowReviewStatus(bool isReviewed)
    {
        reviewStatusTextBlock.Text = isReviewed
            ? "Reviewed in this session."
            : "Not reviewed yet.";
        markReviewedButton.Content = isReviewed ? "Reviewed" : "Mark reviewed";
        markReviewedButton.IsEnabled = !isReviewed;
        markReviewedNextButton.IsEnabled = !isReviewed;
    }

    public void SetFeedbackButtonsEnabled(bool enabled)
    {
        correctFeedbackButton.IsEnabled = enabled;
        wrongLabelFeedbackButton.IsEnabled = enabled;
        notUsefulFeedbackButton.IsEnabled = enabled;
        tooGenericFeedbackButton.IsEnabled = enabled;
        goodExplanationFeedbackButton.IsEnabled = enabled;
        saveManualCorrectionButton.IsEnabled = enabled;
        if (!enabled)
        {
            manualCorrectionPanel.IsVisible = false;
        }
    }

    public void ShowManualCorrection(SelectedMistakeViewItem? item, IReadOnlyList<string> knownLabels)
    {
        if (item is not null)
        {
            correctedLabelComboBox.SelectedItem = knownLabels.Contains(item.RawLabel, StringComparer.Ordinal)
                ? item.RawLabel
                : "unclassified";
            customLabelTextBox.Text = string.Empty;
            feedbackCommentTextBox.Text = string.Empty;
        }

        manualCorrectionPanel.IsVisible = true;
    }

    public bool TryReadManualCorrection(out string? correctedLabel, out string? comment)
    {
        correctedLabel = AnalysisFeedbackService.NormalizeManualLabel(
            customLabelTextBox.Text,
            correctedLabelComboBox.SelectedItem?.ToString());
        comment = feedbackCommentTextBox.Text;
        return !string.IsNullOrWhiteSpace(correctedLabel);
    }

    public void HideManualCorrection()
    {
        manualCorrectionPanel.IsVisible = false;
    }
}
