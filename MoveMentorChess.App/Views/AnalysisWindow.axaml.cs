using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MoveMentorChess.App.ViewModels;
using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;
using MoveMentorChess.Opening;
using MoveMentorChess.Persistence;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.Views;

public partial class AnalysisWindow : Window
{
    private static readonly string[] KnownMistakeLabels =
    [
        "hanging_piece",
        "material_loss",
        "missed_tactic",
        "king_safety",
        "opening_principles",
        "endgame_technique",
        "piece_activity",
        "unclassified"
    ];

    private readonly ImportedGame? importedGame;
    private readonly Func<MoveAnalysisResult, Task>? navigateToMoveAsync;
    private readonly AnalysisWindowViewModel viewModel;
    private AnalysisRunControlsRenderer runControlsRenderer = null!;
    private AnalysisTimelineRenderer timelineRenderer = null!;
    private AnalysisDetailsFeedbackRenderer detailsFeedbackRenderer = null!;
    private AnalysisSnapshotRenderer snapshotRenderer = null!;
    private AnalysisSimilarMistakesRenderer similarMistakesRenderer = null!;

    public AnalysisWindow()
    {
        InitializeComponent();
        viewModel = new AnalysisWindowViewModel();
        DataContext = viewModel;
        InitializeRunControlsRenderer();
        InitializeDetailsFeedbackRenderer();
        InitializeSnapshotRenderer();
        InitializeSimilarMistakesRenderer();
        InitializeTimelineRenderer();
        InitializeResponsiveSnapshotSizing();
    }

    public GameAnalysisResult? CurrentResult => viewModel.CurrentResult;

    public AnalysisWindow(
        ImportedGame importedGame,
        IEngineAnalyzer? engineAnalyzer,
        Func<MoveAnalysisResult, Task> navigateToMoveAsync,
        Action<GameAnalysisProgress>? analysisProgress,
        PlayerSide initialSide,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult>? initialResultsBySide = null,
        IAnalysisWindowDataService? dataService = null)
    {
        this.importedGame = importedGame;
        this.navigateToMoveAsync = navigateToMoveAsync;
        viewModel = new AnalysisWindowViewModel(
            importedGame,
            engineAnalyzer,
            analysisProgress,
            initialSide,
            initialResultsBySide,
            dataService ?? new DefaultAnalysisWindowDataService(() => null));

        InitializeComponent();
        DataContext = viewModel;
        InitializeRunControlsRenderer();
        InitializeDetailsFeedbackRenderer();
        InitializeSnapshotRenderer();
        InitializeSimilarMistakesRenderer();
        InitializeTimelineRenderer();
        InitializeResponsiveSnapshotSizing();
        CorrectedLabelComboBox.ItemsSource = KnownMistakeLabels;
        CorrectedLabelComboBox.SelectedIndex = 0;
        SyncInteractionState();
        SyncFeedbackState();
        viewModel.ShowRunAnalysisPlaceholder();
        RenderDetailsPlaceholder();
        RefreshAdviceRuntimeState();

        if (viewModel.LoadWindowState() is AnalysisWindowState state)
        {
            viewModel.ApplyWindowState(state);
        }

        SideComboBox.SelectionChanged += (_, _) => TryLoadCachedResultForSelectedSide();
        QualityFilterComboBox.SelectionChanged += (_, _) => ApplyFilter();
        TryLoadCachedResultForSelectedSide();
    }

    private void InitializeTimelineRenderer()
    {
        timelineRenderer = new AnalysisTimelineRenderer(
            TimelineBandsGrid,
            TimelineMarkersGrid,
            TimelineSelectedTextBlock,
            TimelineSummaryTextBlock,
            SelectTimelineMistake);
    }

    private void InitializeRunControlsRenderer()
    {
        runControlsRenderer = new AnalysisRunControlsRenderer(
            AnalyzeButton,
            TestAdviceButton,
            SideComboBox,
            QualityFilterComboBox,
            ShowOnBoardButton);
    }

    private void InitializeDetailsFeedbackRenderer()
    {
        detailsFeedbackRenderer = new AnalysisDetailsFeedbackRenderer(
            DetailMoveTextBlock,
            DetailBestMoveTextBlock,
            DetailQualityTextBlock,
            DetailLossTextBlock,
            DetailEvalSwingTextBlock,
            DetailEvalInterpretationTextBlock,
            DetailContextTextBlock,
            DetailAdviceTextBlock,
            DetailWhyTextBlock,
            DetailTrainingHintTextBlock,
            DetailReviewActionTextBlock,
            ReviewStatusTextBlock,
            MarkReviewedButton,
            MarkReviewedNextButton,
            DetailTopCandidatesTextBlock,
            DetailChecklistTextBlock,
            DetailsTextBlock,
            CorrectFeedbackButton,
            WrongLabelFeedbackButton,
            NotUsefulFeedbackButton,
            TooGenericFeedbackButton,
            GoodExplanationFeedbackButton,
            SaveManualCorrectionButton,
            ManualCorrectionPanel,
            CorrectedLabelComboBox,
            CustomLabelTextBox,
            FeedbackCommentTextBox);
    }

    private void InitializeSnapshotRenderer()
    {
        snapshotRenderer = new AnalysisSnapshotRenderer(
            PositionSnapshotPanel,
            PositionSnapshotBoard,
            PositionSafetyBadgeBorder,
            PositionSafetyBadgeTextBlock,
            PositionThreatTextBlock,
            PositionBestIdeaTextBlock,
            PositionMistakeTextBlock,
            SnapshotPlayedButton,
            SnapshotBestButton,
            SnapshotThreatButton);
    }

    private void InitializeSimilarMistakesRenderer()
    {
        similarMistakesRenderer = new AnalysisSimilarMistakesRenderer(
            SimilarMistakesHintTextBlock,
            SimilarMistakesListBox,
            GetVisibleMistakeItems,
            SelectTimelineMistake);
    }

    private void InitializeResponsiveSnapshotSizing()
    {
        PositionSnapshotPanel.SizeChanged += (_, _) => UpdateSnapshotBoardSize();
        SizeChanged += (_, _) => UpdateSnapshotBoardSize();
        UpdateSnapshotBoardSize();
    }

    private void UpdateSnapshotBoardSize()
    {
        snapshotRenderer.UpdateBoardSize();
    }

    private async void TestAdviceButton_Click(object? sender, RoutedEventArgs e)
    {
        RefreshAdviceRuntimeState();
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        if (!status.IsReady)
        {
            viewModel.StatusText = status.InstallHint is null
                ? status.StatusText
                : $"{status.StatusText} {status.InstallHint}";
            return;
        }

        TestAdviceButton.IsEnabled = false;
        viewModel.StatusText = "Testing local advice runtime...";

        try
        {
            AdviceRuntimeSmokeTestResult result = await Task.Run(AdviceRuntimeSmokeTester.Run);
            viewModel.StatusText = result.Message;
        }
        finally
        {
            RefreshAdviceRuntimeState();
            TestAdviceButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AnalyzeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedSideOption is not AnalysisSideOption selectedSide)
        {
            return;
        }

        viewModel.BeginAnalysis(selectedSide.Side);
        SyncInteractionState();
        timelineRenderer.Clear("Analysis timeline will appear here.");
        RenderDetailsPlaceholder();
        SyncFeedbackState();

        try
        {
            AnalysisWindowRunOutcome outcome = await viewModel.AnalyzeAsync(selectedSide.Side);
            ApplyRunOutcome(outcome);
        }
        finally
        {
            viewModel.EndAnalysis();
            SyncInteractionState();
            SyncFeedbackState();
            RefreshAdviceRuntimeState();
        }
    }

    private void MistakesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        AnalysisWindowSelectedDetails? selectedDetails = viewModel.PrepareSelectedDetails();
        if (selectedDetails is null)
        {
            RenderDetailsPlaceholder();
            SyncInteractionState();
            return;
        }

        RenderSelectedDetails(selectedDetails);
        RefreshTimeline(GetVisibleMistakeItems());
        SyncInteractionState();
        SyncFeedbackState();

        if (selectedDetails.ShouldLoadExplanation)
        {
            _ = LoadExplanationAsync(selectedDetails);
        }
    }

    private async void MistakesListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        await ShowSelectedMistakeAsync();
    }

    private async void ShowOnBoardButton_Click(object? sender, RoutedEventArgs e)
    {
        await ShowSelectedMistakeAsync();
    }

    private void ApplyFilter()
    {
        int? selectedPly = viewModel.SelectedMistake is SelectedMistakeViewItem selectedItem
            ? selectedItem.LeadMove.Replay.Ply
            : null;

        if (viewModel.CurrentResult is null)
        {
            viewModel.SummaryText = "Choose a side and run the analysis.";
            viewModel.ClearVisibleMistakes();
            RefreshTimeline([]);
            viewModel.ShowRunAnalysisPlaceholder();
            RenderDetailsPlaceholder();
            SyncInteractionState();
            return;
        }

        AnalysisFilterResult filterResult = viewModel.ApplyFilter(viewModel.SelectedFilterOption, selectedPly);
        IReadOnlyList<SelectedMistakeViewItem> items = filterResult.Items;

        if (items.Count > 0)
        {
            RefreshTimeline(items);
            SyncInteractionState();
            SyncFeedbackState();
        }
        else
        {
            RefreshTimeline(items);
            viewModel.ShowNoFilterMatchesPlaceholder();
            RenderDetailsPlaceholder();
            SyncInteractionState();
        }
    }

    private void ApplyRunOutcome(AnalysisWindowRunOutcome outcome)
    {
        if (outcome.HasResult && outcome.Result is not null)
        {
            viewModel.ApplyRunOutcome(outcome);
            ApplyFilter();
        }
        else
        {
            viewModel.ApplyRunOutcome(outcome);
            RenderDetailsPlaceholder();
            SyncInteractionState();
        }
    }

    private void CorrectFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.Correct);

    private void WrongLabelFeedbackButton_Click(object? sender, RoutedEventArgs e)
    {
        detailsFeedbackRenderer.ShowManualCorrection(
            viewModel.SelectedMistake,
            KnownMistakeLabels);
    }

    private void NotUsefulFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.NotUseful);

    private void TooGenericFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.TooGeneric);

    private void GoodExplanationFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.GoodExplanation);

    private void SaveManualCorrectionButton_Click(object? sender, RoutedEventArgs e)
    {
        detailsFeedbackRenderer.TryReadManualCorrection(out string? correctedLabel, out string? comment);
        if (!viewModel.CanSaveManualCorrection(correctedLabel))
        {
            return;
        }

        RecordSelectedFeedback(AdviceFeedbackKind.WrongLabel, correctedLabel, comment);
        detailsFeedbackRenderer.HideManualCorrection();
    }

    private void RecordSelectedFeedback(AdviceFeedbackKind feedbackKind, string? correctedLabel = null, string? comment = null)
    {
        if (viewModel.SelectedMistake is not SelectedMistakeViewItem item || importedGame is null)
        {
            return;
        }

        string? statusText = viewModel.RecordFeedback(item, feedbackKind, correctedLabel, comment);
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            viewModel.StatusText = statusText;
        }
        UpdateDetails();
    }

    private async Task ShowSelectedMistakeAsync()
    {
        if (viewModel.SelectedMistake is not SelectedMistakeViewItem item || navigateToMoveAsync is null)
        {
            return;
        }

        await navigateToMoveAsync(item.LeadMove);
        Close();
    }

    private async Task LoadExplanationAsync(AnalysisWindowSelectedDetails pendingDetails)
    {
        AnalysisWindowSelectedDetails? selectedDetails = await viewModel.LoadGeneratedDetailsAsync(pendingDetails);
        if (selectedDetails is not null)
        {
            RenderSelectedDetails(selectedDetails);
        }
    }

    private void RenderDetailsPlaceholder()
    {
        detailsFeedbackRenderer.ShowPlaceholder(viewModel.DetailsPlaceholderText);
        snapshotRenderer.Reset();
        similarMistakesRenderer.Clear();
        TimelineSelectedTextBlock.Text = string.Empty;
    }

    private void RenderSelectedDetails(AnalysisWindowSelectedDetails selectedDetails)
    {
        detailsFeedbackRenderer.ShowDetails(selectedDetails.Details);
        RefreshReviewStatus(selectedDetails.Lead);
        snapshotRenderer.Show(selectedDetails.Lead, selectedDetails.Details.EffectiveLabel);
        similarMistakesRenderer.Refresh(selectedDetails.Lead, selectedDetails.Details.EffectiveLabel);
    }

    private void MarkReviewedButton_Click(object? sender, RoutedEventArgs e)
        => MarkSelectedReviewed(moveToNext: false);

    private void MarkReviewedNextButton_Click(object? sender, RoutedEventArgs e)
        => MarkSelectedReviewed(moveToNext: true);

    private void MarkSelectedReviewed(bool moveToNext)
    {
        AnalysisReviewActionResult reviewResult = viewModel.MarkSelectedReviewed(moveToNext);
        ApplyFilter();
        if (reviewResult.ShouldRenderAllReviewedPlaceholder)
        {
            viewModel.ShowAllReviewedPlaceholder();
            RenderDetailsPlaceholder();
        }
        else if (reviewResult.NextSelection is not null)
        {
            SyncInteractionState();
            MistakesListBox.ScrollIntoView(reviewResult.NextSelection);
        }
        else if (viewModel.SelectedMistake is not null)
        {
            RefreshReviewStatus(viewModel.SelectedMistake.LeadMove);
        }

        RefreshTimeline(GetVisibleMistakeItems());
    }

    private void RefreshReviewStatus(MoveAnalysisResult lead)
    {
        bool isReviewed = viewModel.IsReviewed(lead);
        detailsFeedbackRenderer.ShowReviewStatus(isReviewed);
    }

    private void SnapshotPlayedButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Played);

    private void SnapshotBestButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Best);

    private void SnapshotThreatButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Threat);

    private void SimilarMistakesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => similarMistakesRenderer.SelectCurrentLink();

    private void RefreshTimeline(IReadOnlyList<SelectedMistakeViewItem> visibleItems)
    {
        timelineRenderer.Render(
            viewModel.CurrentResult,
            visibleItems,
            viewModel.SelectedMistake,
            viewModel.ReviewedPlies);
    }

    private IReadOnlyList<SelectedMistakeViewItem> GetVisibleMistakeItems()
        => viewModel.VisibleMistakes;

    private void SelectTimelineMistake(SelectedMistakeViewItem item)
    {
        viewModel.SelectedMistake = item;
        SyncInteractionState();
        MistakesListBox.ScrollIntoView(item);
    }

    private void RefreshAdviceRuntimeState()
    {
        viewModel.RefreshAdviceRuntimeState();
    }

    private bool TryLoadCachedResultForSelectedSide()
    {
        AnalysisWindowRunOutcome outcome = viewModel.ResetAndTryLoadCachedSelectedSide();
        RefreshTimeline([]);
        RenderDetailsPlaceholder();
        SyncInteractionState();
        SyncFeedbackState();

        if (outcome.HasResult && outcome.Result is not null)
        {
            ApplyRunOutcome(outcome);
            return true;
        }

        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (importedGame is not null)
        {
            viewModel.StoreWindowState();
        }

        base.OnClosed(e);
    }

    private void SyncInteractionState()
    {
        runControlsRenderer.ApplyInteractionState(
            viewModel.CanRunAnalysis,
            viewModel.IsAnalysisRunning,
            viewModel.CanUseSelectedMistake);
    }

    private void SyncFeedbackState()
    {
        detailsFeedbackRenderer.SetFeedbackButtonsEnabled(viewModel.CanRecordFeedback);
    }
}
