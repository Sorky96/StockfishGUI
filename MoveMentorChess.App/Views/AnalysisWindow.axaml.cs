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
    private static readonly EngineAnalysisOptions DefaultAnalysisOptions = new();
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
    private readonly IEngineAnalyzer? engineAnalyzer;
    private readonly Func<MoveAnalysisResult, Task>? navigateToMoveAsync;
    private readonly Action<GameAnalysisProgress>? analysisProgress;
    private readonly IAnalysisWindowDataService dataService;
    private readonly PlayerSide initialSide;
    private readonly Dictionary<PlayerSide, GameAnalysisResult> initialResultsBySide = [];
    private readonly AnalysisExplanationService explanationService = new();
    private readonly AnalysisSelectionState selectionState = new();
    private AnalysisWindowRunCoordinator runCoordinator = null!;
    private AnalysisRunControlsRenderer runControlsRenderer = null!;
    private AnalysisTimelineRenderer timelineRenderer = null!;
    private AnalysisDetailsFeedbackRenderer detailsFeedbackRenderer = null!;
    private AnalysisSnapshotRenderer snapshotRenderer = null!;

    public AnalysisWindow()
    {
        InitializeComponent();
        dataService = new DefaultAnalysisWindowDataService(() => null);
        InitializeRunCoordinator();
        InitializeRunControlsRenderer();
        InitializeDetailsFeedbackRenderer();
        InitializeSnapshotRenderer();
        InitializeTimelineRenderer();
        InitializeResponsiveSnapshotSizing();
    }

    public GameAnalysisResult? CurrentResult => selectionState.CurrentResult;

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
        this.engineAnalyzer = engineAnalyzer;
        this.navigateToMoveAsync = navigateToMoveAsync;
        this.analysisProgress = analysisProgress;
        this.initialSide = initialSide;
        this.dataService = dataService ?? new DefaultAnalysisWindowDataService(() => null);
        if (initialResultsBySide is not null)
        {
            foreach ((PlayerSide side, GameAnalysisResult result) in initialResultsBySide)
            {
                if (this.dataService.IsAnalysisForGame(result, importedGame))
                {
                    this.initialResultsBySide[side] = result;
                }
            }
        }

        InitializeComponent();
        InitializeRunCoordinator();
        InitializeRunControlsRenderer();
        InitializeDetailsFeedbackRenderer();
        InitializeSnapshotRenderer();
        InitializeTimelineRenderer();
        InitializeResponsiveSnapshotSizing();
        SideComboBox.ItemsSource = new[]
        {
            new SideOption(PlayerSide.White, "Analyze White"),
            new SideOption(PlayerSide.Black, "Analyze Black")
        };
        QualityFilterComboBox.ItemsSource = new[]
        {
            new AnalysisFilterOption("All highlights", null),
            new AnalysisFilterOption("Not reviewed", null, AnalysisReviewFilter.NotReviewed),
            new AnalysisFilterOption("Reviewed", null, AnalysisReviewFilter.Reviewed),
            new AnalysisFilterOption("Blunders only", MoveQualityBucket.Blunder),
            new AnalysisFilterOption("Mistakes only", MoveQualityBucket.Mistake),
            new AnalysisFilterOption("Inaccuracies only", MoveQualityBucket.Inaccuracy)
        };
        CorrectedLabelComboBox.ItemsSource = KnownMistakeLabels;
        CorrectedLabelComboBox.SelectedIndex = 0;
        SideComboBox.SelectedIndex = initialSide == PlayerSide.Black ? 1 : 0;
        QualityFilterComboBox.SelectedIndex = 0;
        SideComboBox.SelectionChanged += (_, _) => TryLoadCachedResultForSelectedSide();
        QualityFilterComboBox.SelectionChanged += (_, _) => ApplyFilter();
        runControlsRenderer.SetAnalysisIdle(engineAnalyzer is not null);
        runControlsRenderer.SetSelectionAvailable(false);
        detailsFeedbackRenderer.SetFeedbackButtonsEnabled(false);
        SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
        RefreshAdviceRuntimeState();

        if (this.dataService.TryGetWindowState(importedGame, out AnalysisWindowState? state) && state is not null)
        {
            SideComboBox.SelectedIndex = state.SelectedSide == PlayerSide.Black ? 1 : 0;
            QualityFilterComboBox.SelectedIndex = Math.Clamp(state.QualityFilterIndex, 0, QualityFilterComboBox.ItemCount - 1);
        }

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

    private void InitializeRunCoordinator()
    {
        runCoordinator = new AnalysisWindowRunCoordinator(
            dataService,
            initialResultsBySide,
            analysisProgress);
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
            StatusTextBlock.Text = status.InstallHint is null
                ? status.StatusText
                : $"{status.StatusText} {status.InstallHint}";
            return;
        }

        TestAdviceButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing local advice runtime...";

        try
        {
            AdviceRuntimeSmokeTestResult result = await Task.Run(AdviceRuntimeSmokeTester.Run);
            StatusTextBlock.Text = result.Message;
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
        if (SideComboBox.SelectedItem is not SideOption selectedSide)
        {
            return;
        }

        runControlsRenderer.SetAnalysisRunning();
        StatusTextBlock.Text = $"Analyzing imported game for {selectedSide.Side}...";
        SummaryTextBlock.Text = string.Empty;
        MistakesListBox.ItemsSource = null;
        timelineRenderer.Clear("Analysis timeline will appear here.");
        SetDetailsPlaceholder("The analysis engine is reviewing the imported game. This may take a moment.");
        detailsFeedbackRenderer.SetFeedbackButtonsEnabled(false);

        try
        {
            AnalysisWindowRunOutcome outcome = await runCoordinator.AnalyzeAsync(
                importedGame,
                engineAnalyzer,
                selectedSide.Side,
                DefaultAnalysisOptions);
            ApplyRunOutcome(outcome);
        }
        finally
        {
            runControlsRenderer.SetAnalysisIdle(engineAnalyzer is not null);
            RefreshAdviceRuntimeState();
        }
    }

    private void MistakesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item)
        {
            SetDetailsPlaceholder("Select a highlighted mistake to inspect details.");
            runControlsRenderer.SetSelectionAvailable(false);
            return;
        }

        AnalysisPreparedExplanation preparedExplanation = explanationService.Prepare(item.LeadMove);

        MoveAdviceFeedback? feedback = FindLatestFeedback(item.LeadMove);
        SetSelectedDetails(
            item.Mistake,
            item.LeadMove,
            selectionState.CurrentResult?.OpeningReview,
            preparedExplanation.Explanation,
            !preparedExplanation.IsCached,
            feedback);
        RefreshTimeline(GetVisibleMistakeItems());
        runControlsRenderer.SetSelectionAvailable(true);
        detailsFeedbackRenderer.SetFeedbackButtonsEnabled(true);

        if (!preparedExplanation.IsCached)
        {
            int requestId = explanationService.BeginRequest();
            _ = LoadExplanationAsync(item, item.LeadMove, preparedExplanation.Request, requestId);
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
        int? selectedPly = MistakesListBox.SelectedItem is SelectedMistakeViewItem selectedItem
            ? selectedItem.LeadMove.Replay.Ply
            : null;

        if (selectionState.CurrentResult is null)
        {
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            MistakesListBox.ItemsSource = null;
            RefreshTimeline([]);
            SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
            runControlsRenderer.SetSelectionAvailable(false);
            return;
        }

        AnalysisFilterResult filterResult = selectionState.BuildFilterResult(QualityFilterComboBox.SelectedItem as AnalysisFilterOption);
        IReadOnlyList<SelectedMistakeViewItem> items = filterResult.Items;
        MistakesListBox.ItemsSource = items;
        SummaryTextBlock.Text = filterResult.SummaryText;

        if (items.Count > 0)
        {
            MistakesListBox.SelectedItem = selectedPly is int ply
                ? items.FirstOrDefault(item => item.LeadMove.Replay.Ply == ply) ?? items[0]
                : items[0];
            RefreshTimeline(items);
        }
        else
        {
            RefreshTimeline(items);
            SetDetailsPlaceholder("No items match the current filter.");
            runControlsRenderer.SetSelectionAvailable(false);
        }
    }

    private void ApplyRunOutcome(AnalysisWindowRunOutcome outcome)
    {
        if (outcome.HasResult && outcome.Result is not null)
        {
            selectionState.SetCurrentResult(outcome.Result, outcome.IsCached);
            ApplyFilter();
        }
        else
        {
            selectionState.ClearCurrentResult();
            SummaryTextBlock.Text = string.Empty;
            SetDetailsPlaceholder(outcome.IsError ? "Analysis failed." : "Run analysis to inspect highlighted mistakes.");
        }

        StatusTextBlock.Text = outcome.StatusText;
    }

    private void CorrectFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.Correct);

    private void WrongLabelFeedbackButton_Click(object? sender, RoutedEventArgs e)
    {
        detailsFeedbackRenderer.ShowManualCorrection(
            MistakesListBox.SelectedItem as SelectedMistakeViewItem,
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
        if (!detailsFeedbackRenderer.TryReadManualCorrection(out string? correctedLabel, out string? comment))
        {
            StatusTextBlock.Text = "Choose or enter a corrected label first.";
            return;
        }

        RecordSelectedFeedback(AdviceFeedbackKind.WrongLabel, correctedLabel, comment);
        detailsFeedbackRenderer.HideManualCorrection();
    }

    private void RecordSelectedFeedback(AdviceFeedbackKind feedbackKind, string? correctedLabel = null, string? comment = null)
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item || importedGame is null)
        {
            return;
        }

        PlayerSide analyzedSide = selectionState.CurrentResult?.AnalyzedSide ?? initialSide;
        StatusTextBlock.Text = AnalysisFeedbackRecorder.Record(
            dataService,
            importedGame,
            analyzedSide,
            DefaultAnalysisOptions,
            item,
            feedbackKind,
            correctedLabel,
            comment);
        UpdateDetails();
    }

    private async Task ShowSelectedMistakeAsync()
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item || navigateToMoveAsync is null)
        {
            return;
        }

        await navigateToMoveAsync(item.LeadMove);
        Close();
    }

    private async Task LoadExplanationAsync(
        SelectedMistakeViewItem item,
        MoveAnalysisResult lead,
        AnalysisExplanationRequest request,
        int requestId)
    {
        if (importedGame is null)
        {
            return;
        }

        MoveExplanation? explanation = await explanationService.GenerateAndCacheAsync(
            importedGame,
            lead,
            selectionState.CurrentResult?.AnalyzedSide,
            request,
            requestId);
        if (explanation is null)
        {
            return;
        }

        if (!ReferenceEquals(MistakesListBox.SelectedItem, item))
        {
            return;
        }

        SetSelectedDetails(item.Mistake, lead, selectionState.CurrentResult?.OpeningReview, explanation, false, FindLatestFeedback(lead));
    }

    private void SetDetailsPlaceholder(string message)
    {
        detailsFeedbackRenderer.ShowPlaceholder(message);
        snapshotRenderer.Reset();
        SimilarMistakesHintTextBlock.Text = string.Empty;
        SimilarMistakesListBox.ItemsSource = null;
        TimelineSelectedTextBlock.Text = string.Empty;
    }

    private void SetSelectedDetails(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        OpeningPhaseReview? openingReview,
        MoveExplanation explanation,
        bool isLoading,
        MoveAdviceFeedback? feedback)
    {
        AnalysisSelectedDetailsPresentation details = AnalysisSelectedDetailsPresenter.Build(
            mistake,
            lead,
            openingReview,
            explanation,
            isLoading,
            feedback);
        detailsFeedbackRenderer.ShowDetails(details);
        RefreshReviewStatus(lead);
        snapshotRenderer.Show(lead, details.EffectiveLabel);
        RefreshSimilarMistakes(lead, details.EffectiveLabel);
    }

    private void MarkReviewedButton_Click(object? sender, RoutedEventArgs e)
        => MarkSelectedReviewed(moveToNext: false);

    private void MarkReviewedNextButton_Click(object? sender, RoutedEventArgs e)
        => MarkSelectedReviewed(moveToNext: true);

    private void MarkSelectedReviewed(bool moveToNext)
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item)
        {
            return;
        }

        int reviewedPly = item.LeadMove.Replay.Ply;
        selectionState.MarkReviewed(item.LeadMove);
        RefreshReviewStatus(item.LeadMove);
        ApplyFilter();
        if (moveToNext)
        {
            SelectNextUnreviewed(reviewedPly);
        }

        RefreshTimeline(GetVisibleMistakeItems());
    }

    private void SelectNextUnreviewed(int afterPly)
    {
        SelectedMistakeViewItem? next = GetVisibleMistakeItems()
            .Where(item => !selectionState.IsReviewed(item.LeadMove))
            .OrderBy(item => item.LeadMove.Replay.Ply <= afterPly)
            .ThenBy(item => item.LeadMove.Replay.Ply)
            .FirstOrDefault();

        if (next is null)
        {
            SetDetailsPlaceholder("All visible highlights are reviewed.");
            return;
        }

        MistakesListBox.SelectedItem = next;
        MistakesListBox.ScrollIntoView(next);
    }

    private void RefreshReviewStatus(MoveAnalysisResult lead)
    {
        bool isReviewed = selectionState.IsReviewed(lead);
        detailsFeedbackRenderer.ShowReviewStatus(isReviewed);
    }

    private void SnapshotPlayedButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Played);

    private void SnapshotBestButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Best);

    private void SnapshotThreatButton_Click(object? sender, RoutedEventArgs e)
        => snapshotRenderer.SetMode(AnalysisSnapshotMode.Threat);

    private void RefreshSimilarMistakes(MoveAnalysisResult lead, string label)
    {
        IReadOnlyList<SimilarMistakeLink> similar = AnalysisTimelinePresentation.BuildSimilarMistakeLinks(
            GetVisibleMistakeItems(),
            lead,
            label);

        SimilarMistakesListBox.ItemsSource = similar;
        SimilarMistakesHintTextBlock.Text = AnalysisTimelinePresentation.BuildSimilarMistakesHint(similar.Count, label);
    }

    private void SimilarMistakesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SimilarMistakesListBox.SelectedItem is not SimilarMistakeLink link)
        {
            return;
        }

        MistakesListBox.SelectedItem = link.Item;
        MistakesListBox.ScrollIntoView(link.Item);
        SimilarMistakesListBox.SelectedItem = null;
    }

    private void RefreshTimeline(IReadOnlyList<SelectedMistakeViewItem> visibleItems)
    {
        timelineRenderer.Render(
            selectionState.CurrentResult,
            visibleItems,
            MistakesListBox.SelectedItem as SelectedMistakeViewItem,
            selectionState.ReviewedPlies);
    }

    private IReadOnlyList<SelectedMistakeViewItem> GetVisibleMistakeItems()
        => MistakesListBox.ItemsSource is IEnumerable<SelectedMistakeViewItem> items ? items.ToList() : [];

    private void SelectTimelineMistake(SelectedMistakeViewItem item)
    {
        MistakesListBox.SelectedItem = item;
        MistakesListBox.ScrollIntoView(item);
    }

    private void RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = explanationService.RefreshRuntimeState();
        AdviceStatusTextBlock.Text = status.StatusText;
    }

    private MoveAdviceFeedback? FindLatestFeedback(MoveAnalysisResult lead)
    {
        if (importedGame is null)
        {
            return null;
        }

        return dataService.FindLatestFeedback(
            importedGame,
            selectionState.CurrentResult?.AnalyzedSide ?? initialSide,
            DefaultAnalysisOptions,
            lead);
    }

    private bool TryLoadCachedResultForSelectedSide()
    {
        MistakesListBox.ItemsSource = null;
        selectionState.ClearCurrentResult();
        RefreshTimeline([]);
        SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
        runControlsRenderer.SetSelectionAvailable(false);
        detailsFeedbackRenderer.SetFeedbackButtonsEnabled(false);
        explanationService.InvalidatePendingRequests();

        if (SideComboBox.SelectedItem is not SideOption selectedSide)
        {
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            return false;
        }

        AnalysisWindowRunOutcome outcome = runCoordinator.TryLoadCached(
            importedGame,
            selectedSide.Side,
            DefaultAnalysisOptions);
        if (outcome.HasResult && outcome.Result is not null)
        {
            ApplyRunOutcome(outcome);
            return true;
        }

        SummaryTextBlock.Text = outcome.StatusText;
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (importedGame is not null && SideComboBox.SelectedItem is SideOption selectedSide)
        {
            dataService.StoreWindowState(
                importedGame,
                new AnalysisWindowState(
                    selectedSide.Side,
                    QualityFilterComboBox.SelectedIndex,
                    1));
        }

        base.OnClosed(e);
    }

    private sealed record SideOption(PlayerSide Side, string Label)
    {
        public override string ToString() => Label;
    }

}
