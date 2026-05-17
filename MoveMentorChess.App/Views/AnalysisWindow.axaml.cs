using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly Dictionary<string, MoveExplanation> explanationCache = [];
    private IAdviceGenerator adviceGenerator = new SettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
    private GameAnalysisResult? currentResult;
    private bool currentResultIsCached;
    private int explanationRequestId;
    private MoveAnalysisResult? snapshotLead;
    private string snapshotLabel = "unclassified";
    private SnapshotMode snapshotMode = SnapshotMode.Played;
    private readonly HashSet<int> reviewedPlies = [];

    public AnalysisWindow()
    {
        InitializeComponent();
        InitializeResponsiveSnapshotSizing();
        dataService = new DefaultAnalysisWindowDataService();
    }

    public GameAnalysisResult? CurrentResult => currentResult;

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
        this.dataService = dataService ?? new DefaultAnalysisWindowDataService();
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
        InitializeResponsiveSnapshotSizing();
        SideComboBox.ItemsSource = new[]
        {
            new SideOption(PlayerSide.White, "Analyze White"),
            new SideOption(PlayerSide.Black, "Analyze Black")
        };
        QualityFilterComboBox.ItemsSource = new[]
        {
            new FilterOption("All highlights", null),
            new FilterOption("Not reviewed", null, ReviewFilter.NotReviewed),
            new FilterOption("Reviewed", null, ReviewFilter.Reviewed),
            new FilterOption("Blunders only", MoveQualityBucket.Blunder),
            new FilterOption("Mistakes only", MoveQualityBucket.Mistake),
            new FilterOption("Inaccuracies only", MoveQualityBucket.Inaccuracy)
        };
        CorrectedLabelComboBox.ItemsSource = KnownMistakeLabels;
        CorrectedLabelComboBox.SelectedIndex = 0;
        SideComboBox.SelectedIndex = initialSide == PlayerSide.Black ? 1 : 0;
        QualityFilterComboBox.SelectedIndex = 0;
        SideComboBox.SelectionChanged += (_, _) => TryLoadCachedResultForSelectedSide();
        QualityFilterComboBox.SelectionChanged += (_, _) => ApplyFilter();
        AnalyzeButton.IsEnabled = engineAnalyzer is not null;
        ShowOnBoardButton.IsEnabled = false;
        SetFeedbackButtonsEnabled(false);
        SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
        RefreshAdviceRuntimeState();

        if (this.dataService.TryGetWindowState(importedGame, out AnalysisWindowState? state) && state is not null)
        {
            SideComboBox.SelectedIndex = state.SelectedSide == PlayerSide.Black ? 1 : 0;
            QualityFilterComboBox.SelectedIndex = Math.Clamp(state.QualityFilterIndex, 0, QualityFilterComboBox.ItemCount - 1);
        }

        TryLoadCachedResultForSelectedSide();
    }

    private void InitializeResponsiveSnapshotSizing()
    {
        PositionSnapshotPanel.SizeChanged += (_, _) => UpdateSnapshotBoardSize();
        SizeChanged += (_, _) => UpdateSnapshotBoardSize();
        UpdateSnapshotBoardSize();
    }

    private void UpdateSnapshotBoardSize()
    {
        double panelWidth = PositionSnapshotPanel.Bounds.Width;
        if (panelWidth <= 0)
        {
            return;
        }

        double availableWidth = Math.Max(0, panelWidth - PositionSnapshotPanel.Padding.Left - PositionSnapshotPanel.Padding.Right);
        double boardSize = Math.Clamp(availableWidth * 0.96, 280, 420);
        PositionSnapshotBoard.Width = boardSize;
        PositionSnapshotBoard.Height = boardSize;
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

        if (importedGame is null || engineAnalyzer is null)
        {
            StatusTextBlock.Text = "Analysis window is missing required game context.";
            return;
        }

        if (dataService.TryLoadExistingResult(
            importedGame,
            selectedSide.Side,
            DefaultAnalysisOptions,
            initialResultsBySide,
            out GameAnalysisResult? cachedResult,
            out GameAnalysisCacheKey cacheKey,
            out string cacheStatus)
            && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = cacheStatus;
            return;
        }

        AnalyzeButton.IsEnabled = false;
        TestAdviceButton.IsEnabled = false;
        SideComboBox.IsEnabled = false;
        QualityFilterComboBox.IsEnabled = false;
        StatusTextBlock.Text = $"Analyzing imported game for {selectedSide.Side}...";
        SummaryTextBlock.Text = string.Empty;
        MistakesListBox.ItemsSource = null;
        TimelineBandsGrid.Children.Clear();
        TimelineBandsGrid.ColumnDefinitions.Clear();
        TimelineMarkersGrid.Children.Clear();
        TimelineSummaryTextBlock.Text = "Analysis timeline will appear here.";
        SetDetailsPlaceholder("The analysis engine is reviewing the imported game. This may take a moment.");
        ShowOnBoardButton.IsEnabled = false;
        SetFeedbackButtonsEnabled(false);

        try
        {
            currentResult = await AnalysisRunService.AnalyzeImportedGameAsync(
                engineAnalyzer,
                importedGame,
                selectedSide.Side,
                DefaultAnalysisOptions,
                analysisProgress,
                dataService.CreateOpeningTheory());
            dataService.StoreResult(cacheKey, currentResult);
            currentResultIsCached = false;
            ApplyFilter();
            StatusTextBlock.Text = $"Analysis finished for {selectedSide.Side}.";
        }
        catch (Exception ex)
        {
            currentResult = null;
            currentResultIsCached = false;
            SummaryTextBlock.Text = string.Empty;
            SetDetailsPlaceholder("Analysis failed.");
            StatusTextBlock.Text = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            AnalyzeButton.IsEnabled = engineAnalyzer is not null;
            TestAdviceButton.IsEnabled = true;
            SideComboBox.IsEnabled = true;
            QualityFilterComboBox.IsEnabled = true;
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
            ShowOnBoardButton.IsEnabled = false;
            SetFeedbackButtonsEnabled(false);
            return;
        }

        AnalysisExplanationRequest explanationRequest = AnalysisExplanationRuntime.CreateRequest(item.LeadMove);
        MoveExplanation explanation = item.LeadMove.Explanation
            ?? new MoveExplanation("Explanation is loading...", "Training hint is loading...");

        bool isCached = explanationCache.TryGetValue(explanationRequest.CacheKey, out MoveExplanation? cachedExplanation);
        if (isCached && cachedExplanation is not null)
        {
            explanation = cachedExplanation;
        }

        MoveAdviceFeedback? feedback = FindLatestFeedback(item.LeadMove);
        SetSelectedDetails(item.Mistake, item.LeadMove, currentResult?.OpeningReview, explanation, !isCached, feedback);
        RefreshTimeline(GetVisibleMistakeItems());
        ShowOnBoardButton.IsEnabled = true;
        SetFeedbackButtonsEnabled(true);

        if (!isCached)
        {
            int requestId = ++explanationRequestId;
            _ = LoadExplanationAsync(item, item.LeadMove, explanationRequest.Level, explanationRequest.CacheKey, requestId);
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

        if (currentResult is null)
        {
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            MistakesListBox.ItemsSource = null;
            RefreshTimeline([]);
            SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
            ShowOnBoardButton.IsEnabled = false;
            SetFeedbackButtonsEnabled(false);
            return;
        }

        IEnumerable<SelectedMistake> visibleMistakes = currentResult.HighlightedMistakes;
        if (QualityFilterComboBox.SelectedItem is FilterOption filter && filter.QualityFilter is not null)
        {
            visibleMistakes = visibleMistakes.Where(mistake => mistake.Quality == filter.QualityFilter.Value);
        }

        if (QualityFilterComboBox.SelectedItem is FilterOption reviewFilter)
        {
            visibleMistakes = reviewFilter.ReviewFilter switch
            {
                ReviewFilter.NotReviewed => visibleMistakes.Where(mistake => !reviewedPlies.Contains(AnalysisMistakePresentation.GetLeadMove(mistake).Replay.Ply)),
                ReviewFilter.Reviewed => visibleMistakes.Where(mistake => reviewedPlies.Contains(AnalysisMistakePresentation.GetLeadMove(mistake).Replay.Ply)),
                _ => visibleMistakes
            };
        }

        List<SelectedMistakeViewItem> items = visibleMistakes
            .Select(mistake => new SelectedMistakeViewItem(mistake, currentResult, reviewedPlies.Contains(AnalysisMistakePresentation.GetLeadMove(mistake).Replay.Ply)))
            .ToList();
        MistakesListBox.ItemsSource = items;

        int blunders = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Blunder);
        int mistakes = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Inaccuracy);
        int reviewed = CountReviewedHighlights(currentResult);
        string cacheSuffix = currentResultIsCached ? " Loaded from cache." : string.Empty;
        string diagnosis = AnalysisTimelinePresentation.BuildSummaryDiagnosis(currentResult);
        SummaryTextBlock.Text = $"Showing {items.Count} highlights for {currentResult.AnalyzedSide}: {blunders} blunders, {mistakes} mistakes, {inaccuracies} inaccuracies. Reviewed {reviewed}/{currentResult.HighlightedMistakes.Count} highlights. {diagnosis}{cacheSuffix}";

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
            ShowOnBoardButton.IsEnabled = false;
            SetFeedbackButtonsEnabled(false);
        }
    }

    private void CorrectFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.Correct);

    private void WrongLabelFeedbackButton_Click(object? sender, RoutedEventArgs e)
    {
        if (MistakesListBox.SelectedItem is SelectedMistakeViewItem item)
        {
            CorrectedLabelComboBox.SelectedItem = KnownMistakeLabels.Contains(item.RawLabel, StringComparer.Ordinal)
                ? item.RawLabel
                : "unclassified";
            CustomLabelTextBox.Text = string.Empty;
            FeedbackCommentTextBox.Text = string.Empty;
        }

        ManualCorrectionPanel.IsVisible = true;
    }

    private void NotUsefulFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.NotUseful);

    private void TooGenericFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.TooGeneric);

    private void GoodExplanationFeedbackButton_Click(object? sender, RoutedEventArgs e)
        => RecordSelectedFeedback(AdviceFeedbackKind.GoodExplanation);

    private void SaveManualCorrectionButton_Click(object? sender, RoutedEventArgs e)
    {
        string? correctedLabel = NormalizeManualLabel(
            CustomLabelTextBox.Text,
            CorrectedLabelComboBox.SelectedItem?.ToString());
        if (string.IsNullOrWhiteSpace(correctedLabel))
        {
            StatusTextBlock.Text = "Choose or enter a corrected label first.";
            return;
        }

        RecordSelectedFeedback(AdviceFeedbackKind.WrongLabel, correctedLabel, FeedbackCommentTextBox.Text);
        ManualCorrectionPanel.IsVisible = false;
    }

    private void RecordSelectedFeedback(AdviceFeedbackKind feedbackKind, string? correctedLabel = null, string? comment = null)
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item || importedGame is null)
        {
            return;
        }

        PlayerSide analyzedSide = currentResult?.AnalyzedSide ?? initialSide;
        MoveAdviceFeedback feedback = AnalysisFeedbackService.CreateFeedback(
            importedGame,
            analyzedSide,
            DefaultAnalysisOptions,
            item,
            feedbackKind,
            correctedLabel,
            comment);

        dataService.SaveMoveAdviceFeedback(feedback);
        AdviceFeedbackEntry entry = AnalysisFeedbackService.CreateFeedbackLogEntry(feedback, item);
        AdviceFeedbackLogger.CreateDefault().Record(entry);
        StatusTextBlock.Text = $"Feedback saved: {AnalysisFeedbackService.FormatFeedbackKind(feedbackKind)}.";
        UpdateDetails();
    }

    private void SetFeedbackButtonsEnabled(bool enabled)
    {
        CorrectFeedbackButton.IsEnabled = enabled;
        WrongLabelFeedbackButton.IsEnabled = enabled;
        NotUsefulFeedbackButton.IsEnabled = enabled;
        TooGenericFeedbackButton.IsEnabled = enabled;
        GoodExplanationFeedbackButton.IsEnabled = enabled;
        SaveManualCorrectionButton.IsEnabled = enabled;
        if (!enabled)
        {
            ManualCorrectionPanel.IsVisible = false;
        }
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
        ExplanationLevel explanationLevel,
        string cacheKey,
        int requestId)
    {
        if (importedGame is null)
        {
            return;
        }

        MoveExplanation explanation;
        try
        {
            explanation = await Task.Run(() => adviceGenerator.Generate(
                lead.Replay,
                lead.Quality,
                lead.MistakeTag,
                lead.BeforeAnalysis.BestMoveUci,
                lead.CentipawnLoss,
                explanationLevel,
                new AdviceGenerationContext(
                    "avalonia-analysis-window",
                    GameFingerprint.Compute(importedGame.PgnText),
                    currentResult?.AnalyzedSide,
                    NarrationStyle: LlamaGpuSettingsStore.Load().NarrationStyle)));
        }
        catch (Exception ex)
        {
            explanation = new MoveExplanation(
                "Local advice generation failed.",
                "Use the engine lines and training hint for now.",
                ex.Message);
        }

        if (requestId != explanationRequestId)
        {
            return;
        }

        explanationCache[cacheKey] = explanation;

        if (!ReferenceEquals(MistakesListBox.SelectedItem, item))
        {
            return;
        }

        SetSelectedDetails(item.Mistake, lead, currentResult?.OpeningReview, explanation, false, FindLatestFeedback(lead));
    }

    private void SetDetailsPlaceholder(string message)
    {
        DetailMoveTextBlock.Text = "No move selected";
        DetailBestMoveTextBlock.Text = string.Empty;
        DetailQualityTextBlock.Text = string.Empty;
        DetailLossTextBlock.Text = string.Empty;
        DetailEvalSwingTextBlock.Text = string.Empty;
        DetailEvalInterpretationTextBlock.Text = string.Empty;
        DetailContextTextBlock.Text = string.Empty;
        DetailAdviceTextBlock.Text = message;
        DetailWhyTextBlock.Text = string.Empty;
        DetailTrainingHintTextBlock.Text = string.Empty;
        DetailReviewActionTextBlock.Text = string.Empty;
        ReviewStatusTextBlock.Text = string.Empty;
        MarkReviewedButton.IsEnabled = false;
        MarkReviewedNextButton.IsEnabled = false;
        DetailTopCandidatesTextBlock.Text = string.Empty;
        DetailChecklistTextBlock.Text = string.Empty;
        PositionSnapshotBoard.Fen = null;
        PositionSnapshotBoard.Arrows = [];
        PositionSnapshotBoard.SelectedSquare = null;
        PositionSnapshotBoard.PreviewTargetSquare = null;
        PositionSafetyBadgeBorder.Background = Brush.Parse("#263A49");
        PositionSafetyBadgeTextBlock.Text = string.Empty;
        PositionThreatTextBlock.Text = string.Empty;
        PositionBestIdeaTextBlock.Text = string.Empty;
        PositionMistakeTextBlock.Text = string.Empty;
        snapshotLead = null;
        snapshotLabel = "unclassified";
        snapshotMode = SnapshotMode.Played;
        UpdateSnapshotModeButtons();
        SimilarMistakesHintTextBlock.Text = string.Empty;
        SimilarMistakesListBox.ItemsSource = null;
        TimelineSelectedTextBlock.Text = string.Empty;
        DetailsTextBlock.Text = string.Empty;
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
        DetailMoveTextBlock.Text = details.MoveText;
        DetailBestMoveTextBlock.Text = details.BestMoveText;
        DetailQualityTextBlock.Text = details.QualityText;
        DetailLossTextBlock.Text = details.LossText;
        DetailEvalSwingTextBlock.Text = details.EvalSwingText;
        DetailEvalInterpretationTextBlock.Text = details.EvalInterpretationText;
        DetailContextTextBlock.Text = details.ContextText;
        DetailAdviceTextBlock.Text = details.AdviceText;
        DetailWhyTextBlock.Text = details.WhyText;
        DetailTrainingHintTextBlock.Text = details.TrainingHintText;
        DetailReviewActionTextBlock.Text = details.ReviewActionText;
        RefreshReviewStatus(lead);
        DetailTopCandidatesTextBlock.Text = details.TopCandidatesText;
        DetailChecklistTextBlock.Text = details.ChecklistText;
        SetPositionSnapshot(lead, details.EffectiveLabel);
        RefreshSimilarMistakes(lead, details.EffectiveLabel);
        DetailsTextBlock.Text = details.DetailsText;
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
        reviewedPlies.Add(item.LeadMove.Replay.Ply);
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
            .Where(item => !reviewedPlies.Contains(item.LeadMove.Replay.Ply))
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
        bool isReviewed = reviewedPlies.Contains(lead.Replay.Ply);
        ReviewStatusTextBlock.Text = isReviewed
            ? "Reviewed in this session."
            : "Not reviewed yet.";
        MarkReviewedButton.Content = isReviewed ? "Reviewed" : "Mark reviewed";
        MarkReviewedButton.IsEnabled = !isReviewed;
        MarkReviewedNextButton.IsEnabled = !isReviewed;
    }

    private void SetPositionSnapshot(MoveAnalysisResult lead, string label)
    {
        snapshotLead = lead;
        snapshotLabel = label;
        snapshotMode = SnapshotMode.Played;
        RenderPositionSnapshot();
    }

    private void SnapshotPlayedButton_Click(object? sender, RoutedEventArgs e)
        => SetSnapshotMode(SnapshotMode.Played);

    private void SnapshotBestButton_Click(object? sender, RoutedEventArgs e)
        => SetSnapshotMode(SnapshotMode.Best);

    private void SnapshotThreatButton_Click(object? sender, RoutedEventArgs e)
        => SetSnapshotMode(SnapshotMode.Threat);

    private void SetSnapshotMode(SnapshotMode mode)
    {
        snapshotMode = mode;
        RenderPositionSnapshot();
    }

    private void RenderPositionSnapshot()
    {
        if (snapshotLead is not MoveAnalysisResult lead)
        {
            return;
        }

        PositionSnapshotBoard.Fen = snapshotMode == SnapshotMode.Best
            ? lead.Replay.FenBefore
            : lead.Replay.FenAfter;
        PositionSnapshotBoard.RotateBoard = lead.Replay.Side == PlayerSide.Black;
        PositionSnapshotBoard.SelectedSquare = snapshotMode == SnapshotMode.Played ? lead.Replay.ToSquare : null;
        PositionSnapshotBoard.PreviewTargetSquare = null;
        PositionSnapshotBoard.Arrows = AnalysisSnapshotPresentation.BuildSnapshotArrows(lead, snapshotMode);
        (string safetyText, string safetyBrush) = AnalysisSnapshotPresentation.BuildMovedPieceSafetyBadge(lead);
        PositionSafetyBadgeTextBlock.Text = snapshotMode == SnapshotMode.Played
            ? safetyText
            : snapshotMode == SnapshotMode.Best
                ? "Best move view"
                : "Threat view";
        PositionSafetyBadgeBorder.Background = Brush.Parse(snapshotMode == SnapshotMode.Played ? safetyBrush : "#263A49");
        PositionThreatTextBlock.Text = AnalysisSnapshotPresentation.BuildSnapshotThreatText(lead, snapshotLabel, snapshotMode);
        PositionBestIdeaTextBlock.Text = AnalysisSnapshotPresentation.BuildBestMoveIdeaText(lead);
        PositionMistakeTextBlock.Text = AnalysisSnapshotPresentation.BuildPlayerMistakeText(lead, snapshotLabel);
        UpdateSnapshotModeButtons();
    }

    private void UpdateSnapshotModeButtons()
    {
        SetSnapshotModeButtonState(SnapshotPlayedButton, snapshotMode == SnapshotMode.Played);
        SetSnapshotModeButtonState(SnapshotBestButton, snapshotMode == SnapshotMode.Best);
        SetSnapshotModeButtonState(SnapshotThreatButton, snapshotMode == SnapshotMode.Threat);
    }

    private static void SetSnapshotModeButtonState(Button button, bool isActive)
    {
        button.Background = Brush.Parse(isActive ? "#2F6FB3" : "#263A49");
        button.Foreground = Brushes.White;
    }

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
        TimelineBandsGrid.ColumnDefinitions.Clear();
        TimelineBandsGrid.Children.Clear();
        TimelineMarkersGrid.Children.Clear();
        TimelineSummaryTextBlock.Text = string.Empty;
        TimelineSelectedTextBlock.Text = string.Empty;

        if (currentResult is null || currentResult.Replay.Count == 0)
        {
            TimelineSummaryTextBlock.Text = "Run analysis to see game phases and mistake markers.";
            return;
        }

        List<PhaseSegment> segments = AnalysisTimelinePresentation.BuildPhaseSegments(currentResult.Replay);
        for (int i = 0; i < segments.Count; i++)
        {
            PhaseSegment segment = segments[i];
            TimelineBandsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1, segment.PlyCount), GridUnitType.Star)));
            Border phaseBand = new()
            {
                Background = Brush.Parse(AnalysisTimelinePresentation.GetPhaseBrush(segment.Phase)),
                BorderBrush = Brush.Parse("#101820"),
                BorderThickness = new Avalonia.Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = AnalysisMistakePresentation.FormatPhase(segment.Phase),
                    FontSize = 11,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(phaseBand, i);
            TimelineBandsGrid.Children.Add(phaseBand);
        }

        Dictionary<int, SelectedMistakeViewItem> markersByPly = visibleItems
            .GroupBy(item => item.LeadMove.Replay.Ply)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.Mistake.Quality)
                    .ThenByDescending(item => item.LeadMove.CentipawnLoss ?? 0)
                    .First());

        TimelineMarkersGrid.Columns = Math.Max(1, currentResult.Replay.Count);
        foreach (ReplayPly replay in currentResult.Replay)
        {
            Border marker = new()
            {
                Height = 12,
                Margin = new Avalonia.Thickness(1, 0),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = Brushes.Transparent
            };

            if (markersByPly.TryGetValue(replay.Ply, out SelectedMistakeViewItem? item))
            {
                bool isSelected = ReferenceEquals(MistakesListBox.SelectedItem, item);
                marker.Background = Brush.Parse(AnalysisTimelinePresentation.GetQualityBrush(item.Mistake.Quality));
                marker.BorderBrush = isSelected ? Brushes.White : reviewedPlies.Contains(item.LeadMove.Replay.Ply) ? Brush.Parse("#9ED7A6") : Brush.Parse("#101820");
                marker.BorderThickness = new Avalonia.Thickness(isSelected ? 2 : 0);
                marker.Height = isSelected ? 22 : 12;
                marker.Margin = new Avalonia.Thickness(isSelected ? 0 : 1, 0);
                marker.Cursor = new Cursor(StandardCursorType.Hand);
                string reviewed = reviewedPlies.Contains(item.LeadMove.Replay.Ply) ? " Reviewed." : string.Empty;
                ToolTip.SetTip(marker, $"{item.MoveRange}: {item.Mistake.Quality}, {item.LabelText}, {AnalysisMistakePresentation.BuildImpactText(item.LeadMove)}.{reviewed}");
                marker.PointerPressed += (_, _) => SelectTimelineMistake(item);
            }

            TimelineMarkersGrid.Children.Add(marker);
        }

        if (MistakesListBox.SelectedItem is SelectedMistakeViewItem selected)
        {
            string reviewed = reviewedPlies.Contains(selected.LeadMove.Replay.Ply) ? " Reviewed." : string.Empty;
            TimelineSelectedTextBlock.Text = $"You are here: {selected.MoveRange} - {selected.LabelText}, {AnalysisMistakePresentation.BuildImpactText(selected.LeadMove)}.{reviewed}";
        }

        string phaseSummary = AnalysisTimelinePresentation.BuildPhaseSummary(segments);
        int reviewedCount = currentResult is null ? 0 : CountReviewedHighlights(currentResult);
        int totalHighlights = currentResult?.HighlightedMistakes.Count ?? 0;
        TimelineSummaryTextBlock.Text = $"{phaseSummary}. Reviewed {reviewedCount}/{totalHighlights} highlights. Markers: red blunder, orange mistake, yellow inaccuracy.";
    }

    private int CountReviewedHighlights(GameAnalysisResult result)
        => AnalysisTimelinePresentation.CountReviewedHighlights(result, reviewedPlies);

    private IReadOnlyList<SelectedMistakeViewItem> GetVisibleMistakeItems()
        => MistakesListBox.ItemsSource is IEnumerable<SelectedMistakeViewItem> items ? items.ToList() : [];

    private void SelectTimelineMistake(SelectedMistakeViewItem item)
    {
        MistakesListBox.SelectedItem = item;
        MistakesListBox.ScrollIntoView(item);
    }

    private void RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        adviceGenerator = new SettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
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
            currentResult?.AnalyzedSide ?? initialSide,
            DefaultAnalysisOptions,
            lead);
    }

    private static string? NormalizeManualLabel(string? customLabel, string? selectedLabel)
        => AnalysisFeedbackService.NormalizeManualLabel(customLabel, selectedLabel);

    private bool TryLoadCachedResultForSelectedSide()
    {
        MistakesListBox.ItemsSource = null;
        currentResult = null;
        currentResultIsCached = false;
        RefreshTimeline([]);
        SetDetailsPlaceholder("Run analysis to inspect highlighted mistakes.");
        ShowOnBoardButton.IsEnabled = false;
        SetFeedbackButtonsEnabled(false);
        explanationRequestId++;

        if (importedGame is null || SideComboBox.SelectedItem is not SideOption selectedSide)
        {
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            return false;
        }

        if (dataService.TryLoadExistingResult(
            importedGame,
            selectedSide.Side,
            DefaultAnalysisOptions,
            initialResultsBySide,
            out GameAnalysisResult? cachedResult,
            out _,
            out string cacheStatus)
            && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = cacheStatus;
            return true;
        }

        SummaryTextBlock.Text = cacheStatus;
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

    private sealed record FilterOption(string Label, MoveQualityBucket? QualityFilter, ReviewFilter ReviewFilter = ReviewFilter.All)
    {
        public override string ToString() => Label;
    }

    private enum ReviewFilter
    {
        All,
        NotReviewed,
        Reviewed
    }

}
