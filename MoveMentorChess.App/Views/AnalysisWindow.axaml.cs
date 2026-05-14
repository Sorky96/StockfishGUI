using System.Text;
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
    private readonly PlayerSide initialSide;
    private readonly Dictionary<PlayerSide, GameAnalysisResult> initialResultsBySide = [];
    private readonly Dictionary<string, MoveExplanation> explanationCache = [];
    private IAdviceGenerator adviceGenerator = CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
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
    }

    public GameAnalysisResult? CurrentResult => currentResult;

    public AnalysisWindow(
        ImportedGame importedGame,
        IEngineAnalyzer? engineAnalyzer,
        Func<MoveAnalysisResult, Task> navigateToMoveAsync,
        Action<GameAnalysisProgress>? analysisProgress,
        PlayerSide initialSide,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult>? initialResultsBySide = null)
    {
        this.importedGame = importedGame;
        this.engineAnalyzer = engineAnalyzer;
        this.navigateToMoveAsync = navigateToMoveAsync;
        this.analysisProgress = analysisProgress;
        this.initialSide = initialSide;
        if (initialResultsBySide is not null)
        {
            foreach ((PlayerSide side, GameAnalysisResult result) in initialResultsBySide)
            {
                if (IsAnalysisForGame(result, importedGame))
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

        if (GameAnalysisCache.TryGetWindowState(importedGame, out AnalysisWindowState? state) && state is not null)
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

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (TryGetInitialResult(selectedSide.Side, out GameAnalysisResult? initialResult) && initialResult is not null)
        {
            currentResult = initialResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded saved analysis for {selectedSide.Side}.";
            return;
        }

        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded cached analysis for {selectedSide.Side}.";
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
            GameAnalysisService analysisService = new(
                engineAnalyzer,
                adviceGenerator: CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateBulkAnalysisGenerator()),
                openingTheory: CreateOpeningTheory());
            IProgress<GameAnalysisProgress>? progress = analysisProgress is null
                ? null
                : new Progress<GameAnalysisProgress>(analysisProgress);
            currentResult = await Task.Run(() => analysisService.AnalyzeGame(importedGame, selectedSide.Side, DefaultAnalysisOptions, progress));
            GameAnalysisCache.StoreResult(cacheKey, currentResult);
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

        LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
        ExplanationLevel level = settings.DefaultExplanationLevel;
        AdviceNarrationStyle narrationStyle = settings.NarrationStyle;
        string cacheKey = BuildExplanationCacheKey(item.LeadMove, level, narrationStyle);
        MoveExplanation explanation = item.LeadMove.Explanation
            ?? new MoveExplanation("Explanation is loading...", "Training hint is loading...");

        bool isCached = explanationCache.TryGetValue(cacheKey, out MoveExplanation? cachedExplanation);
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
            _ = LoadExplanationAsync(item, item.LeadMove, level, cacheKey, requestId);
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
                ReviewFilter.NotReviewed => visibleMistakes.Where(mistake => !reviewedPlies.Contains(GetLeadMove(mistake).Replay.Ply)),
                ReviewFilter.Reviewed => visibleMistakes.Where(mistake => reviewedPlies.Contains(GetLeadMove(mistake).Replay.Ply)),
                _ => visibleMistakes
            };
        }

        List<SelectedMistakeViewItem> items = visibleMistakes
            .Select(mistake => new SelectedMistakeViewItem(mistake, currentResult, reviewedPlies.Contains(GetLeadMove(mistake).Replay.Ply)))
            .ToList();
        MistakesListBox.ItemsSource = items;

        int blunders = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Blunder);
        int mistakes = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Inaccuracy);
        int reviewed = CountReviewedHighlights(currentResult);
        string cacheSuffix = currentResultIsCached ? " Loaded from cache." : string.Empty;
        string diagnosis = BuildSummaryDiagnosis(currentResult);
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

        MoveAnalysisResult lead = item.LeadMove;
        string gameFingerprint = GameFingerprint.Compute(importedGame.PgnText);
        GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(importedGame, currentResult?.AnalyzedSide ?? initialSide, DefaultAnalysisOptions);
        MoveAdviceFeedback feedback = new(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            gameFingerprint,
            currentResult?.AnalyzedSide ?? initialSide,
            key.Depth,
            key.MultiPv,
            key.MoveTimeMs,
            lead.Replay.Ply,
            lead.Replay.MoveNumber,
            lead.Replay.San,
            lead.Replay.Uci,
            lead.Replay.FenBefore,
            lead.Replay.FenAfter,
            lead.EvalBeforeCp,
            lead.EvalAfterCp,
            lead.BeforeAnalysis.BestMoveUci,
            item.RawLabel,
            lead.MistakeTag?.Confidence ?? item.Mistake.Tag?.Confidence,
            lead.MistakeTag?.Evidence ?? item.Mistake.Tag?.Evidence ?? [],
            item.Mistake.Quality,
            lead.CentipawnLoss,
            feedbackKind,
            correctedLabel,
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            "analysis-window");

        AnalysisStoreProvider.GetStore()?.SaveMoveAdviceFeedback(feedback);
        AdviceFeedbackEntry entry = new(
            DateTime.UtcNow,
            gameFingerprint,
            lead.Replay.Ply,
            feedbackKind,
            item.RawLabel,
            item.Mistake.Quality,
            lead.CentipawnLoss,
            UsedFallback: false,
            lead.Replay.San,
            lead.Replay.Uci,
            lead.BeforeAnalysis.BestMoveUci,
            "analysis-window");

        AdviceFeedbackLogger.CreateDefault().Record(entry);
        StatusTextBlock.Text = $"Feedback saved: {FormatFeedbackKind(feedbackKind)}.";
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
        string effectiveLabel = feedback?.CorrectedLabel ?? mistake.Tag?.Label ?? "unclassified";
        DetailMoveTextBlock.Text = BuildMoveRange(mistake);
        DetailBestMoveTextBlock.Text = FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);
        DetailQualityTextBlock.Text = $"{mistake.Quality} - {FormatMistakeLabel(effectiveLabel)}";
        DetailLossTextBlock.Text = $"Evaluation loss: {lead.CentipawnLoss?.ToString() ?? "n/a"} cp";
        DetailEvalSwingTextBlock.Text = BuildEvalSwingText(lead);
        DetailEvalInterpretationTextBlock.Text = BuildEvalInterpretation(lead);
        DetailContextTextBlock.Text = BuildPositionContextText(lead, effectiveLabel);
        DetailAdviceTextBlock.Text = TakeFirstSentences(SimplifyAdviceText(explanation.ShortText), 2);
        DetailWhyTextBlock.Text = BuildReadableWhyText(lead, explanation);
        DetailTrainingHintTextBlock.Text = TakeFirstSentences(SimplifyAdviceText(explanation.TrainingHint), 2);
        DetailReviewActionTextBlock.Text = BuildReviewActionText(lead, effectiveLabel);
        RefreshReviewStatus(lead);
        DetailTopCandidatesTextBlock.Text = BuildTopCandidateMovesText(lead);
        DetailChecklistTextBlock.Text = BuildBeforeMoveChecklistText(effectiveLabel);
        SetPositionSnapshot(lead, effectiveLabel);
        RefreshSimilarMistakes(lead, effectiveLabel);
        DetailsTextBlock.Text = BuildDetailsText(mistake, lead, openingReview, explanation, isLoading, feedback);
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
        PositionSnapshotBoard.Arrows = BuildSnapshotArrows(lead, snapshotMode);
        (string safetyText, string safetyBrush) = BuildMovedPieceSafetyBadge(lead);
        PositionSafetyBadgeTextBlock.Text = snapshotMode == SnapshotMode.Played
            ? safetyText
            : snapshotMode == SnapshotMode.Best
                ? "Best move view"
                : "Threat view";
        PositionSafetyBadgeBorder.Background = Brush.Parse(snapshotMode == SnapshotMode.Played ? safetyBrush : "#263A49");
        PositionThreatTextBlock.Text = BuildSnapshotThreatText(lead, snapshotLabel, snapshotMode);
        PositionBestIdeaTextBlock.Text = BuildBestMoveIdeaText(lead);
        PositionMistakeTextBlock.Text = BuildPlayerMistakeText(lead, snapshotLabel);
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
        List<SelectedMistakeViewItem> similarItems = GetVisibleMistakeItems()
            .Where(item => !ReferenceEquals(item.LeadMove, lead)
                && string.Equals(item.RawLabel, label, StringComparison.Ordinal))
            .OrderByDescending(item => item.LeadMove.CentipawnLoss ?? 0)
            .ThenBy(item => item.LeadMove.Replay.Ply)
            .Take(4)
            .ToList();

        List<SimilarMistakeLink> similar = similarItems
            .Select(item => new SimilarMistakeLink(
                item,
                BuildSimilarMistakeRole(item, lead, label)))
            .ToList();

        SimilarMistakesListBox.ItemsSource = similar;
        SimilarMistakesHintTextBlock.Text = similar.Count == 0
            ? "No other visible highlights share this diagnosis."
            : $"Other {FormatMistakeLabel(label).ToLowerInvariant()} moments. Click one to jump there.";
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

        List<PhaseSegment> segments = BuildPhaseSegments(currentResult.Replay);
        for (int i = 0; i < segments.Count; i++)
        {
            PhaseSegment segment = segments[i];
            TimelineBandsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1, segment.PlyCount), GridUnitType.Star)));
            Border phaseBand = new()
            {
                Background = Brush.Parse(GetPhaseBrush(segment.Phase)),
                BorderBrush = Brush.Parse("#101820"),
                BorderThickness = new Avalonia.Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = FormatPhase(segment.Phase),
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
                marker.Background = Brush.Parse(GetQualityBrush(item.Mistake.Quality));
                marker.BorderBrush = isSelected ? Brushes.White : reviewedPlies.Contains(item.LeadMove.Replay.Ply) ? Brush.Parse("#9ED7A6") : Brush.Parse("#101820");
                marker.BorderThickness = new Avalonia.Thickness(isSelected ? 2 : 0);
                marker.Height = isSelected ? 22 : 12;
                marker.Margin = new Avalonia.Thickness(isSelected ? 0 : 1, 0);
                marker.Cursor = new Cursor(StandardCursorType.Hand);
                string reviewed = reviewedPlies.Contains(item.LeadMove.Replay.Ply) ? " Reviewed." : string.Empty;
                ToolTip.SetTip(marker, $"{item.MoveRange}: {item.Mistake.Quality}, {item.LabelText}, {BuildImpactText(item.LeadMove)}.{reviewed}");
                marker.PointerPressed += (_, _) => SelectTimelineMistake(item);
            }

            TimelineMarkersGrid.Children.Add(marker);
        }

        if (MistakesListBox.SelectedItem is SelectedMistakeViewItem selected)
        {
            string reviewed = reviewedPlies.Contains(selected.LeadMove.Replay.Ply) ? " Reviewed." : string.Empty;
            TimelineSelectedTextBlock.Text = $"You are here: {selected.MoveRange} - {selected.LabelText}, {BuildImpactText(selected.LeadMove)}.{reviewed}";
        }

        string phaseSummary = string.Join(", ", segments.Select(segment => $"{FormatPhase(segment.Phase)} {segment.PlyCount} ply"));
        int reviewedCount = currentResult is null ? 0 : CountReviewedHighlights(currentResult);
        int totalHighlights = currentResult?.HighlightedMistakes.Count ?? 0;
        TimelineSummaryTextBlock.Text = $"{phaseSummary}. Reviewed {reviewedCount}/{totalHighlights} highlights. Markers: red blunder, orange mistake, yellow inaccuracy.";
    }

    private int CountReviewedHighlights(GameAnalysisResult result)
        => result.HighlightedMistakes.Count(mistake => reviewedPlies.Contains(GetLeadMove(mistake).Replay.Ply));

    private IReadOnlyList<SelectedMistakeViewItem> GetVisibleMistakeItems()
        => MistakesListBox.ItemsSource is IEnumerable<SelectedMistakeViewItem> items ? items.ToList() : [];

    private void SelectTimelineMistake(SelectedMistakeViewItem item)
    {
        MistakesListBox.SelectedItem = item;
        MistakesListBox.ScrollIntoView(item);
    }

    private static List<PhaseSegment> BuildPhaseSegments(IReadOnlyList<ReplayPly> replay)
    {
        List<PhaseSegment> segments = [];
        foreach (ReplayPly ply in replay)
        {
            if (segments.Count == 0 || segments[^1].Phase != ply.Phase)
            {
                segments.Add(new PhaseSegment(ply.Phase, 1));
            }
            else
            {
                PhaseSegment last = segments[^1];
                segments[^1] = last with { PlyCount = last.PlyCount + 1 };
            }
        }

        return segments;
    }

    private static string BuildSummaryDiagnosis(GameAnalysisResult result)
    {
        if (result.HighlightedMistakes.Count == 0)
        {
            return "No recurring problem pattern found.";
        }

        var dominant = result.HighlightedMistakes
            .Select(mistake => new
            {
                Label = mistake.Tag?.Label ?? GetLeadMove(mistake).MistakeTag?.Label ?? "unclassified",
                Lead = GetLeadMove(mistake)
            })
            .GroupBy(item => item.Label)
            .Select(group => new
            {
                Label = group.Key,
                Count = group.Count(),
                AverageLoss = group.Average(item => item.Lead.CentipawnLoss ?? 0)
            })
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.AverageLoss)
            .First();

        MoveAnalysisResult mostExpensive = result.HighlightedMistakes
            .Select(GetLeadMove)
            .OrderByDescending(move => move.CentipawnLoss ?? 0)
            .First();

        string moveLabel = $"{mostExpensive.Replay.MoveNumber}{(mostExpensive.Replay.Side == PlayerSide.White ? "." : "...")} {mostExpensive.Replay.San}";
        return $"Biggest pattern: {FormatMistakeLabel(dominant.Label)}, {dominant.Count} times, average loss {dominant.AverageLoss:0} cp. Costliest moment: {moveLabel}.";
    }

    private static MoveAnalysisResult GetLeadMove(SelectedMistake mistake)
        => mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();

    private static string BuildEvalSwingText(MoveAnalysisResult lead)
    {
        string before = FormatPawnScore(lead.EvalBeforeCp, lead.BestMateIn);
        string after = FormatPawnScore(lead.EvalAfterCp, lead.PlayedMateIn);
        string swing = lead.EvalBeforeCp is int beforeCp && lead.EvalAfterCp is int afterCp
            ? $", swing {FormatSignedPawns(afterCp - beforeCp)}"
            : string.Empty;
        return $"{before} -> {after}{swing}";
    }

    private static string BuildEvalInterpretation(MoveAnalysisResult lead)
    {
        string mateInterpretation = BuildMateInterpretation(lead);
        if (!string.IsNullOrWhiteSpace(mateInterpretation))
        {
            return mateInterpretation;
        }

        if (lead.EvalBeforeCp is not int before || lead.EvalAfterCp is not int after)
        {
            return "The engine score is mate-based or unavailable, so use the candidate moves below as the main guide.";
        }

        string category = $"Position changed from {DescribeEvaluation(before)} to {DescribeEvaluation(after)}.";
        int swing = after - before;
        if (before > 120 && after < 40)
        {
            return $"{category} Gives back a large part of the advantage.";
        }

        if (before > -80 && after < -180)
        {
            return $"{category} Moves into a clearly worse position.";
        }

        if (Math.Abs(swing) >= 150)
        {
            return $"{category} The swing is tactically significant, so check the opponent's forcing replies.";
        }

        return $"{category} Makes the practical version of the position worse without an immediate collapse.";
    }

    private static string BuildMateInterpretation(MoveAnalysisResult lead)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"{FormatSideName(Opponent(lead.Replay.Side))} has a forced mate. Treat this as an urgent king-safety failure, not just an evaluation swing.";
        }

        if (lead.PlayedMateIn is > 0)
        {
            return $"{FormatSideName(lead.Replay.Side)} still has a forced mate, but compare the candidate line to see whether it was the fastest or cleanest route.";
        }

        if (lead.BestMateIn is > 0)
        {
            return $"{FormatSideName(lead.Replay.Side)} had a forced mate available. The played move let that concrete winning line slip.";
        }

        return string.Empty;
    }

    private static string BuildPositionContextText(MoveAnalysisResult lead, string label)
        => $"Phase: {FormatPhase(lead.Replay.Phase)}\nMotif: {FormatMistakeLabel(label)}\nThreat after move: {BuildThreatText(label)}\nMissed idea: {BuildMissedIdeaText(lead)}";

    private static string BuildThreatText(string label)
    {
        return label switch
        {
            "hanging_piece" or "material_loss" => "the opponent can win material or keep a loose piece under pressure",
            "missed_tactic" => "the opponent may have a forcing reply",
            "king_safety" => "king safety and forcing checks become more important",
            "opening_principles" => "development or central control falls behind",
            "endgame_technique" => "the technical conversion becomes harder",
            "piece_activity" => "pieces lose coordination or active squares",
            _ => "the opponent gets an easier plan"
        };
    }

    private static string BuildSnapshotThreatText(MoveAnalysisResult lead, string label, SnapshotMode mode)
    {
        if (mode == SnapshotMode.Best)
        {
            return "Best-move view: compare the green arrow with the move you played.";
        }

        if (mode == SnapshotMode.Threat)
        {
            EngineLine? threatLine = lead.AfterAnalysis.Lines.FirstOrDefault();
            string threatMove = FormatMoveFromFen(lead.Replay.FenAfter, threatLine?.MoveUci);
            return threatLine is null
                ? BuildThreatText(label)
                : $"After the played move, the opponent's key reply is {threatMove}.";
        }

        return BuildThreatText(label);
    }

    private static string BuildMissedIdeaText(MoveAnalysisResult lead)
        => FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);

    private static string BuildSimilarMistakeRole(SelectedMistakeViewItem item, MoveAnalysisResult currentLead, string currentLabel)
    {
        if ((item.LeadMove.CentipawnLoss ?? 0) > (currentLead.CentipawnLoss ?? 0))
        {
            return "More costly";
        }

        if (item.LeadMove.Replay.Phase != currentLead.Replay.Phase)
        {
            return $"{FormatPhase(item.LeadMove.Replay.Phase)} version";
        }

        if (item.LeadMove.Replay.Ply > currentLead.Replay.Ply)
        {
            return "Later example";
        }

        return string.Equals(item.RawLabel, currentLabel, StringComparison.Ordinal)
            ? "Same motif"
            : "Related";
    }

    private static string BuildReviewActionText(MoveAnalysisResult lead, string label)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"Next drill: review 3 {FormatPhase(lead.Replay.Phase)} positions where a natural move allows a forced mate.";
        }

        return label switch
        {
            "hanging_piece" or "material_loss" => $"Review 3 {FormatPhase(lead.Replay.Phase)} positions where {lead.Replay.San} leaves material loose after a forcing reply.",
            "missed_tactic" => $"Next drill: from move {lead.Replay.MoveNumber}, list checks, captures, and threats before choosing a quiet move.",
            "king_safety" => $"Review 3 positions like move {lead.Replay.MoveNumber} where a move opens a file, diagonal, or forcing check near the king.",
            "opening_principles" => $"Review this opening moment: compare development, king safety, and center control before playing {lead.Replay.San}.",
            "endgame_technique" => $"Next drill: replay move {lead.Replay.MoveNumber} and verify king activity, pawn races, and trades before simplifying.",
            "piece_activity" => $"Review action: before {lead.Replay.San}, identify the least active piece and one improving move.",
            _ => $"Next drill: before move {lead.Replay.MoveNumber}, name the opponent's forcing reply to your candidate."
        };
    }

    private static string BuildTopCandidateMovesText(MoveAnalysisResult lead)
    {
        if (lead.BeforeAnalysis.Lines.Count == 0)
        {
            return "No engine candidate lines are available for this position.";
        }

        StringBuilder builder = new();
        foreach ((EngineLine line, int index) in lead.BeforeAnalysis.Lines.Take(2).Select((line, index) => (line, index)))
        {
            string moveLabel = FormatMoveFromFen(lead.Replay.FenBefore, line.MoveUci);
            string score = FormatPawnScore(line.Centipawns, line.MateIn);
            string note = index == 0
                ? $"best: {BuildCandidateCoachNote(lead, line, isBest: true)}"
                : BuildCandidateCoachNote(lead, line, isBest: false);
            builder.AppendLine($"{index + 1}. {moveLabel} ({score}) - {note}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildCandidateCoachNote(MoveAnalysisResult lead, EngineLine line, bool isBest)
    {
        if (line.MateIn is not null)
        {
            return isBest ? "forces the most concrete result" : "playable, but less forcing";
        }

        int? bestScore = lead.BeforeAnalysis.Lines.FirstOrDefault()?.Centipawns;
        int? scoreGap = bestScore is int best && line.Centipawns is int cp ? Math.Abs(best - cp) : null;
        string move = line.MoveUci;
        if (move.Length >= 4)
        {
            string from = move[..2];
            string to = move[2..4];
            if (PositionInspector.CountPieceMobility(lead.Replay.FenBefore, from, lead.Replay.Side) is int beforeMobility
                && TryGetMobilityAfterMove(lead.Replay.FenBefore, move, lead.Replay.Side, to, out int afterMobility)
                && afterMobility > beforeMobility)
            {
                return isBest ? "because it improves piece activity" : "playable and improves activity, but less direct";
            }
        }

        if (lead.Replay.Phase == GamePhase.Opening)
        {
            return isBest ? "because it keeps development and central control on track" : "playable, but less direct for development";
        }

        if (scoreGap is >= 80)
        {
            return "playable, but gives up practical value compared with the best move";
        }

        return isBest ? "because it keeps the cleanest version of the position" : "playable, but slightly less precise";
    }

    private static bool TryGetMobilityAfterMove(string fenBefore, string uciMove, PlayerSide side, string toSquare, out int mobility)
    {
        mobility = 0;
        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null
            || PositionInspector.CountPieceMobility(appliedMove.FenAfter, toSquare, side) is not int afterMobility)
        {
            return false;
        }

        mobility = afterMobility;
        return true;
    }

    private static IReadOnlyList<BoardArrowViewModel> BuildSnapshotArrows(MoveAnalysisResult lead, SnapshotMode mode)
    {
        List<BoardArrowViewModel> arrows = [];

        if (mode == SnapshotMode.Played)
        {
            arrows.Add(new BoardArrowViewModel(lead.Replay.FromSquare, lead.Replay.ToSquare, Color.Parse("#D9822B")));
        }

        if (mode is SnapshotMode.Played or SnapshotMode.Best
            && TryBuildMoveArrow(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci, Color.Parse("#56C271"), out BoardArrowViewModel bestArrow))
        {
            arrows.Add(bestArrow);
        }

        EngineLine? threatLine = lead.AfterAnalysis.Lines.FirstOrDefault();
        if (mode == SnapshotMode.Threat
            && TryBuildMoveArrow(lead.Replay.FenAfter, threatLine?.MoveUci, Color.Parse("#D84A4A"), out BoardArrowViewModel threatArrow))
        {
            arrows.Add(threatArrow);
        }

        return arrows;
    }

    private static bool TryBuildMoveArrow(string fenBefore, string? uciMove, Color color, out BoardArrowViewModel arrow)
    {
        arrow = new BoardArrowViewModel("a1", "a1", color);
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return false;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return false;
        }

        arrow = new BoardArrowViewModel(appliedMove.FromSquare, appliedMove.ToSquare, color);
        return true;
    }

    private static string BuildPositionSnapshotText(MoveAnalysisResult lead, string label)
    {
        string material = lead.MaterialDeltaCp == 0
            ? "Material: balanced"
            : $"Material: {FormatSignedPawns(lead.MaterialDeltaCp)}";
        string kingSquare = PositionInspector.GetKingSquare(lead.Replay.FenAfter, lead.Replay.Side) ?? "unknown";

        return $"{material}\nKing: {kingSquare}\nMain risk: {BuildThreatText(label)}";
    }

    private static string BuildBestMoveIdeaText(MoveAnalysisResult lead)
    {
        string bestMove = FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);
        EngineLine? bestLine = lead.BeforeAnalysis.Lines.FirstOrDefault();
        string note = bestLine is null
            ? "keeps the cleaner position"
            : BuildCandidateCoachNote(lead, bestLine, isBest: true);
        return $"{bestMove}: {note}.";
    }

    private static string BuildPlayerMistakeText(MoveAnalysisResult lead, string label)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return $"{FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} allowed a forced mate.";
        }

        return label switch
        {
            "material_loss" => $"{FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} left material vulnerable.",
            "hanging_piece" => $"{FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} left a piece loose.",
            _ => $"{FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)} created a {FormatMistakeLabel(label).ToLowerInvariant()} problem."
        };
    }

    private static (string Text, string Brush) BuildMovedPieceSafetyBadge(MoveAnalysisResult lead)
    {
        PositionInspector.SquareSafetySummary? safety = PositionInspector.AnalyzeSquareSafety(
            lead.Replay.FenAfter,
            lead.Replay.ToSquare,
            lead.Replay.Side);

        if (safety is null)
        {
            return ("Moved piece status unknown", "#657386");
        }

        if (safety.Value.IsHanging || safety.Value.IsFreeToTake)
        {
            return ("Moved piece hanging", "#B93838");
        }

        if (safety.Value.LikelyLosesExchange || safety.Value.Attackers > safety.Value.Defenders)
        {
            return ("Moved piece under pressure", "#D9822B");
        }

        return ("Moved piece safe", "#1F7A55");
    }

    private static string BuildBeforeMoveChecklistText(string label)
    {
        string thirdQuestion = label switch
        {
            "king_safety" => "3. Does either king get a new attacking line?",
            "opening_principles" => "3. Am I developing a piece and fighting for the center?",
            "endgame_technique" => "3. After trades, do I keep an active king or passed pawn?",
            _ => "3. What does my move change about king safety?"
        };

        return $"1. Is anything hanging?\n2. Does the opponent have a forcing move?\n{thirdQuestion}";
    }

    private static string BuildDetailsText(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        OpeningPhaseReview? openingReview,
        MoveExplanation explanation,
        bool isLoading,
        MoveAdviceFeedback? feedback)
    {
        StringBuilder builder = new();
        string effectiveLabel = feedback?.CorrectedLabel ?? mistake.Tag?.Label ?? "unclassified";
        builder.AppendLine("Move facts:");
        builder.AppendLine($"Quality: {mistake.Quality}");
        builder.AppendLine($"Label: {FormatMistakeLabel(effectiveLabel)}");
        if (feedback is not null)
        {
            builder.AppendLine($"Original label: {FormatMistakeLabel(feedback.OriginalLabel ?? "unclassified")}");
            builder.AppendLine($"Manual feedback: {feedback.FeedbackKind}");
            if (!string.IsNullOrWhiteSpace(feedback.CorrectedLabel))
            {
                builder.AppendLine($"Manual/effective label: {FormatMistakeLabel(feedback.CorrectedLabel)}");
            }

            if (!string.IsNullOrWhiteSpace(feedback.Comment))
            {
                builder.AppendLine($"Manual comment: {feedback.Comment}");
            }
        }
        builder.AppendLine($"Confidence: {(mistake.Tag?.Confidence ?? 0):0.00}");
        builder.AppendLine($"Phase: {lead.Replay.Phase}");
        builder.AppendLine($"Played move: {FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)}");
        builder.AppendLine($"Best move: {FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci)}");
        builder.AppendLine($"Eval before: {FormatScore(lead.EvalBeforeCp, lead.BestMateIn)}");
        builder.AppendLine($"Eval after: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
        builder.AppendLine($"Centipawn loss: {(lead.CentipawnLoss?.ToString() ?? "n/a")}");
        builder.AppendLine($"Material delta: {lead.MaterialDeltaCp}");

        if (openingReview is not null && lead.Replay.Phase == GamePhase.Opening)
        {
            builder.AppendLine();
            builder.AppendLine("Opening review:");
            builder.AppendLine($"Branch: {openingReview.Branch.BranchLabel}");

            if (openingReview.TheoryExit?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is marked as the theory exit: {openingReview.TheoryExit.Trigger}");
            }

            if (openingReview.FirstSignificantMistake?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is the first significant opening mistake: {openingReview.FirstSignificantMistake.Trigger}");
            }
        }

        if (isLoading)
        {
            builder.AppendLine();
            builder.AppendLine("Local advice model is generating a richer explanation in the background...");
        }

        if (mistake.Tag?.Evidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Evidence:");
            foreach (string evidence in mistake.Tag.Evidence)
            {
                builder.AppendLine($"- {evidence}");
            }
        }

        if (lead.BeforeAnalysis.Lines.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Engine candidates:");
            builder.AppendLine(FormatEngineCandidates(lead.Replay.FenBefore, lead.BeforeAnalysis.Lines));
        }

        EngineLine? playedContinuation = lead.AfterAnalysis.Lines.FirstOrDefault();
        if (playedContinuation is not null && playedContinuation.Pv.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Likely continuation after played move:");
            builder.AppendLine($"Score: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
            builder.AppendLine(FormatPrincipalVariation(lead.Replay.FenAfter, playedContinuation.Pv, maxHalfMoves: 8));
        }

        builder.AppendLine();
        builder.AppendLine("Board navigation:");
        builder.AppendLine("Use 'Show On Board' to jump to this position in the main window.");

        return builder.ToString().TrimEnd();
    }

    private static string BuildReadableWhyText(MoveAnalysisResult lead, MoveExplanation explanation)
    {
        string detailed = SimplifyAdviceText(explanation.DetailedText);
        if (!string.IsNullOrWhiteSpace(detailed))
        {
            return TakeFirstSentences(detailed, 2);
        }

        string played = FormatSanAndUci(lead.Replay.San, lead.Replay.Uci);
        string best = FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci);
        return lead.CentipawnLoss is int loss
            ? $"{played} gave the opponent a better version of the position. {best} keeps more pressure and avoids the {loss} cp drop."
            : $"{played} made the position harder to play. {best} is the cleaner engine recommendation.";
    }

    private static string SimplifyAdviceText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string result = text.Trim();
        string[] prefixes =
        [
            "Candidate-move check: ",
            "Practical view: ",
            "Calculation note: ",
            "What: ",
            "Speed drill: ",
            "Coach recap: ",
            "Here is the practical idea: ",
            "Levy-style drill: ",
            "Okay, tiny chess crisis, very fixable: ",
            "Stream recap: ",
            "Next-game challenge: "
        ];

        bool changed;
        do
        {
            changed = false;
            foreach (string prefix in prefixes)
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result[prefix.Length..].TrimStart();
                    changed = true;
                }
            }
        }
        while (changed);

        return result;
    }

    private static string TakeFirstSentences(string text, int sentenceCount)
    {
        if (string.IsNullOrWhiteSpace(text) || sentenceCount <= 0)
        {
            return string.Empty;
        }

        int found = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?')
            {
                found++;
                if (found >= sentenceCount)
                {
                    return text[..(i + 1)].Trim();
                }
            }
        }

        return text.Trim();
    }

    private void RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        adviceGenerator = CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
        AdviceStatusTextBlock.Text = status.StatusText;
    }

    private MoveAdviceFeedback? FindLatestFeedback(MoveAnalysisResult lead)
    {
        if (importedGame is null || currentResult is null)
        {
            return null;
        }

        GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(importedGame, currentResult.AnalyzedSide, DefaultAnalysisOptions);
        return AnalysisStoreProvider.GetStore()
            ?.ListMoveAdviceFeedback(limit: 2000)
            .Where(feedback =>
                feedback.GameFingerprint == key.GameFingerprint
                && feedback.AnalyzedSide == key.Side
                && feedback.Depth == key.Depth
                && feedback.MultiPv == key.MultiPv
                && feedback.MoveTimeMs == key.MoveTimeMs
                && feedback.Ply == lead.Replay.Ply)
            .OrderByDescending(feedback => feedback.TimestampUtc)
            .ThenByDescending(feedback => feedback.FeedbackId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? NormalizeManualLabel(string? customLabel, string? selectedLabel)
    {
        string candidate = string.IsNullOrWhiteSpace(customLabel) ? selectedLabel ?? string.Empty : customLabel;
        candidate = candidate.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static IAdviceGenerator CreateSettingsBackedAdviceGenerator(IAdviceGenerator inner)
        => new SettingsBackedAdviceGenerator(inner);

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

        if (TryGetInitialResult(selectedSide.Side, out GameAnalysisResult? initialResult) && initialResult is not null)
        {
            currentResult = initialResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded saved analysis for {selectedSide.Side}.";
            return true;
        }

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded cached analysis for {selectedSide.Side}.";
            return true;
        }

        SummaryTextBlock.Text = $"No cached analysis for {selectedSide.Side}. Run analysis to generate it.";
        return false;
    }

    private bool TryGetInitialResult(PlayerSide side, out GameAnalysisResult? result)
    {
        result = null;
        if (importedGame is null
            || !initialResultsBySide.TryGetValue(side, out GameAnalysisResult? candidate)
            || !IsAnalysisForGame(candidate, importedGame))
        {
            return false;
        }

        result = candidate;
        return true;
    }

    private static bool IsAnalysisForGame(GameAnalysisResult result, ImportedGame game)
    {
        return string.Equals(
            GameFingerprint.Compute(result.Game.PgnText),
            GameFingerprint.Compute(game.PgnText),
            StringComparison.Ordinal);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (importedGame is not null && SideComboBox.SelectedItem is SideOption selectedSide)
        {
            GameAnalysisCache.StoreWindowState(
                importedGame,
                new AnalysisWindowState(
                    selectedSide.Side,
                    QualityFilterComboBox.SelectedIndex,
                    1));
        }

        base.OnClosed(e);
    }

    private static string BuildMoveRange(SelectedMistake mistake)
    {
        MoveAnalysisResult first = mistake.Moves.First();
        MoveAnalysisResult last = mistake.Moves.Last();
        string firstMove = $"{first.Replay.MoveNumber}{(first.Replay.Side == PlayerSide.White ? "." : "...")} {first.Replay.San}";
        if (mistake.Moves.Count == 1)
        {
            return firstMove;
        }

        string lastMove = $"{last.Replay.MoveNumber}{(last.Replay.Side == PlayerSide.White ? "." : "...")} {last.Replay.San}";
        return $"{firstMove} -> {lastMove}";
    }

    private static string FormatScore(int? centipawns, int? mateIn)
        => mateIn is int mate ? $"mate {mate}" : centipawns is int cp ? $"{cp} cp" : "n/a";

    private static string FormatPawnScore(int? centipawns, int? mateIn)
        => mateIn is int mate ? $"mate {mate}" : centipawns is int cp ? FormatSignedPawns(cp) : "n/a";

    private static string FormatSignedPawns(int centipawns)
    {
        double pawns = centipawns / 100.0;
        return pawns > 0 ? $"+{pawns:0.0}" : $"{pawns:0.0}";
    }

    private static string DescribeEvaluation(int centipawns)
    {
        return centipawns switch
        {
            >= 250 => "winning",
            >= 100 => "clearly better",
            >= 40 => "slightly better",
            > -40 => "roughly equal",
            > -100 => "slightly worse",
            > -250 => "worse",
            _ => "lost"
        };
    }

    private static string FormatPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "opening",
            GamePhase.Middlegame => "middlegame",
            GamePhase.Endgame => "endgame",
            _ => phase.ToString()
        };
    }

    private static PlayerSide Opponent(PlayerSide side)
        => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;

    private static string FormatSideName(PlayerSide side)
        => side == PlayerSide.White ? "White" : "Black";

    private static string GetPhaseBrush(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "#1F7A55",
            GamePhase.Middlegame => "#2F6FB3",
            GamePhase.Endgame => "#8F3F9F",
            _ => "#657386"
        };
    }

    private static string GetQualityBrush(MoveQualityBucket quality)
    {
        return quality switch
        {
            MoveQualityBucket.Blunder => "#D84A4A",
            MoveQualityBucket.Mistake => "#D9822B",
            MoveQualityBucket.Inaccuracy => "#D7B338",
            _ => "#657386"
        };
    }

    private static string FormatMoveFromFen(string fenBefore, string? uciMove)
    {
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return "(unknown)";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return uciMove;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static string FormatSanAndUci(string san, string uci)
        => string.Equals(san, uci, StringComparison.OrdinalIgnoreCase) ? san : $"{san} ({uci})";

    private static string FormatEngineCandidates(string fenBefore, IReadOnlyList<EngineLine> lines)
    {
        StringBuilder builder = new();

        for (int i = 0; i < lines.Count; i++)
        {
            EngineLine line = lines[i];
            string moveLabel = FormatMoveFromFen(fenBefore, line.MoveUci);
            string pv = FormatPrincipalVariation(fenBefore, line.Pv, maxHalfMoves: 8);
            builder.AppendLine($"{i + 1}. {moveLabel} | {FormatScore(line.Centipawns, line.MateIn)}");
            if (!string.IsNullOrWhiteSpace(pv))
            {
                builder.AppendLine($"   PV: {pv}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatPrincipalVariation(string fenBefore, IReadOnlyList<string> pv, int maxHalfMoves)
    {
        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _))
        {
            return string.Join(' ', pv.Take(maxHalfMoves));
        }

        List<string> formattedMoves = new(Math.Min(pv.Count, maxHalfMoves));
        foreach (string uciMove in pv.Take(maxHalfMoves))
        {
            if (!game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _) || appliedMove is null)
            {
                formattedMoves.Add(uciMove);
                continue;
            }

            formattedMoves.Add($"{appliedMove.MoveNumber}{(appliedMove.WhiteMoved ? "." : "...")} {appliedMove.San}");
        }

        if (pv.Count > maxHalfMoves)
        {
            formattedMoves.Add("...");
        }

        return string.Join(" ", formattedMoves);
    }

    private static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose piece",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material loss",
            "piece_activity" => "Passive pieces",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.Replace('_', ' ').ToLowerInvariant())
        };
    }

    private static OpeningTheoryQueryService? CreateOpeningTheory()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        return store is null ? null : OpeningTheorySourceResolver.Create(store);
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

    private enum SnapshotMode
    {
        Played,
        Best,
        Threat
    }

    private sealed record SimilarMistakeLink(SelectedMistakeViewItem Item, string RoleText)
    {
        public string MoveRange => Item.MoveRange;

        public string MetaText => Item.MetaText;
    }

    private sealed class SelectedMistakeViewItem
    {
        public SelectedMistakeViewItem(SelectedMistake mistake, GameAnalysisResult analysisResult, bool isReviewed)
        {
            Mistake = mistake;
            LeadMove = mistake.Moves
                .OrderByDescending(move => move.Quality)
                .ThenByDescending(move => move.CentipawnLoss ?? 0)
                .First();
            MoveRange = BuildMoveRange(Mistake);
            RawLabel = Mistake.Tag?.Label ?? LeadMove.MistakeTag?.Label ?? "unclassified";
            LabelText = FormatMistakeLabel(RawLabel);
            LabelBrush = GetMistakeLabelBrush(RawLabel);
            LabelForeground = GetMistakeLabelForeground(RawLabel);
            MetaText = $"{Mistake.Quality} | {BuildImpactText(LeadMove)} | {FormatPhase(LeadMove.Replay.Phase)}";
            (PriorityText, PriorityReason, PriorityBrush) = BuildPriorityInfo(Mistake, LeadMove, RawLabel, analysisResult);
            ReviewStatusText = isReviewed ? "Reviewed" : string.Empty;
            ReviewStatusBrush = isReviewed ? "#9ED7A6" : "#657386";
        }

        public SelectedMistake Mistake { get; }

        public MoveAnalysisResult LeadMove { get; }

        public string MoveRange { get; }

        public string LabelText { get; }

        public string RawLabel { get; }

        public string LabelBrush { get; }

        public string LabelForeground { get; }

        public string MetaText { get; }

        public string PriorityText { get; }

        public string PriorityReason { get; }

        public string PriorityBrush { get; }

        public string ReviewStatusText { get; }

        public string ReviewStatusBrush { get; }

        public override string ToString()
        {
            return $"{MoveRange} | {Mistake.Quality} | {LabelText} | {BuildImpactText(LeadMove)}";
        }
    }

    private static string BuildImpactText(MoveAnalysisResult lead)
    {
        if (lead.PlayedMateIn is < 0)
        {
            return "forced mate allowed";
        }

        if (lead.BestMateIn is > 0 && lead.PlayedMateIn is null)
        {
            return "winning tactic missed";
        }

        if (lead.BestMateIn is > 0 && lead.PlayedMateIn is > 0)
        {
            return "mate route changed";
        }

        return $"evaluation loss {lead.CentipawnLoss?.ToString() ?? "n/a"} cp";
    }

    private static (string Text, string Reason, string Brush) BuildPriorityInfo(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        string label,
        GameAnalysisResult analysisResult)
    {
        MoveAnalysisResult costliest = analysisResult.HighlightedMistakes
            .Select(GetLeadMove)
            .OrderByDescending(move => move.CentipawnLoss ?? 0)
            .First();
        if (costliest.Replay.Ply == lead.Replay.Ply)
        {
            return ("Costliest", "Start here: this was the largest evaluation loss in the game.", "#8F3F9F");
        }

        if (analysisResult.OpeningReview?.TheoryExit?.Ply == lead.Replay.Ply
            || analysisResult.OpeningReview?.FirstSignificantMistake?.Ply == lead.Replay.Ply)
        {
            return ("Opening turning point", "This move changed the direction of the opening phase.", "#1F7A55");
        }

        int recurringCount = analysisResult.HighlightedMistakes.Count(item =>
            string.Equals(item.Tag?.Label ?? GetLeadMove(item).MistakeTag?.Label ?? "unclassified", label, StringComparison.Ordinal));
        if (recurringCount >= 2)
        {
            return ("Recurring pattern", $"{FormatMistakeLabel(label)} appears {recurringCount} times in this analysis.", "#2F6FB3");
        }

        if (mistake.Quality == MoveQualityBucket.Blunder || (lead.CentipawnLoss ?? 0) >= 150)
        {
            return ("Review first", "High-impact move: review it before smaller inaccuracies.", "#B93838");
        }

        return ("Review later", "Useful, but lower priority than the main turning points.", "#657386");
    }

    private static string GetMistakeLabelBrush(string label)
    {
        return label switch
        {
            "hanging_piece" => "#B93838",
            "material_loss" => "#8F3F9F",
            "missed_tactic" => "#C56A19",
            "opening_principles" => "#1F7A55",
            "king_safety" => "#B88A10",
            "endgame_technique" => "#2F6FB3",
            "piece_activity" => "#4D6B2E",
            _ => "#657386"
        };
    }

    private static string GetMistakeLabelForeground(string label)
    {
        return label switch
        {
            "king_safety" => "#111827",
            _ => "White"
        };
    }

    private static string FormatFeedbackKind(AdviceFeedbackKind kind)
    {
        return kind switch
        {
            AdviceFeedbackKind.Correct => "Helpful",
            AdviceFeedbackKind.WrongLabel => "Wrong diagnosis",
            AdviceFeedbackKind.NotUseful => "Not useful",
            AdviceFeedbackKind.TooGeneric => "Needs clearer explanation",
            AdviceFeedbackKind.GoodExplanation => "Good explanation",
            _ => kind.ToString()
        };
    }

    private static string BuildExplanationCacheKey(
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        AdviceNarrationStyle narrationStyle)
        => $"{lead.Replay.Ply}:{lead.Replay.Uci}:{explanationLevel}:{narrationStyle}";

    private sealed record PhaseSegment(GamePhase Phase, int PlyCount);

    private sealed class SettingsBackedAdviceGenerator(IAdviceGenerator inner) : IAdviceGenerator
    {
        public MoveExplanation Generate(
            ReplayPly replay,
            MoveQualityBucket quality,
            MistakeTag? tag,
            string? bestMoveUci,
            int? centipawnLoss,
            ExplanationLevel level = ExplanationLevel.Intermediate,
            AdviceGenerationContext? context = null)
        {
            LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
            AdviceGenerationContext enrichedContext = context is null
                ? new AdviceGenerationContext("avalonia-analysis-window", null, NarrationStyle: settings.NarrationStyle)
                : context with { NarrationStyle = settings.NarrationStyle };

            MoveExplanation explanation = inner.Generate(
                replay,
                quality,
                tag,
                bestMoveUci,
                centipawnLoss,
                settings.DefaultExplanationLevel,
                enrichedContext);

            return ApplyNarrationStyle(explanation, settings.NarrationStyle);
        }

        private static MoveExplanation ApplyNarrationStyle(MoveExplanation explanation, AdviceNarrationStyle style)
        {
            return style switch
            {
                AdviceNarrationStyle.LevyRozman => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Here is the practical idea: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Coach recap: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Levy-style drill: ")
                },
                AdviceNarrationStyle.HikaruNakamura => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Candidate-move check: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Calculation note: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Speed drill: ")
                },
                AdviceNarrationStyle.BotezLive => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Okay, tiny chess crisis, very fixable: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Stream recap: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Next-game challenge: ")
                },
                AdviceNarrationStyle.WittyAlien => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Alien coach says the pony wandered into danger: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Free-candy scanner report: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Do not grab free candy rule: ")
                },
                _ => explanation
            };
        }

        private static string AddPrefix(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(text)
                || text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            return $"{prefix}{text}";
        }
    }
}
