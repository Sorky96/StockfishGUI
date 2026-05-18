using MoveMentorChess.Analysis;
using MoveMentorChess.Engine;
using MoveMentorChess.Opening;
using MoveMentorChess.Presentation.Models;

namespace MoveMentorChess.App.ViewModels;

internal sealed class AnalysisWindowViewModel : ViewModelBase
{
    public static readonly EngineAnalysisOptions DefaultAnalysisOptions = new();

    private readonly ImportedGame? importedGame;
    private readonly IEngineAnalyzer? engineAnalyzer;
    private readonly Action<GameAnalysisProgress>? analysisProgress;
    private readonly IAnalysisWindowDataService dataService;
    private readonly PlayerSide initialSide;
    private readonly Dictionary<PlayerSide, GameAnalysisResult> initialResultsBySide = [];
    private readonly AnalysisSelectionState selectionState = new();
    private readonly AnalysisExplanationService explanationService = new();
    private readonly AnalysisWindowRunCoordinator runCoordinator;
    private string statusText = "Choose a side and run the analysis.";
    private string summaryText = string.Empty;
    private string adviceStatusText = string.Empty;
    private string detailsPlaceholderText = "Run analysis to inspect highlighted mistakes.";
    private IReadOnlyList<SelectedMistakeViewItem> visibleMistakes = [];
    private SelectedMistakeViewItem? selectedMistake;
    private AnalysisSideOption? selectedSideOption;
    private AnalysisFilterOption? selectedFilterOption;
    private bool isAnalysisRunning;

    public AnalysisWindowViewModel()
        : this(null, null, null, PlayerSide.White, null, new DefaultAnalysisWindowDataService(() => null))
    {
    }

    public AnalysisWindowViewModel(
        ImportedGame? importedGame,
        IEngineAnalyzer? engineAnalyzer,
        Action<GameAnalysisProgress>? analysisProgress,
        PlayerSide initialSide,
        IReadOnlyDictionary<PlayerSide, GameAnalysisResult>? initialResultsBySide,
        IAnalysisWindowDataService dataService)
    {
        this.importedGame = importedGame;
        this.engineAnalyzer = engineAnalyzer;
        this.analysisProgress = analysisProgress;
        this.initialSide = initialSide;
        this.dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        selectedSideOption = AnalysisWindowSetupService.GetSideOption(initialSide);
        selectedFilterOption = AnalysisWindowSetupService.FilterOptions[0];
        if (importedGame is not null && initialResultsBySide is not null)
        {
            foreach ((PlayerSide side, GameAnalysisResult result) in initialResultsBySide)
            {
                if (this.dataService.IsAnalysisForGame(result, importedGame))
                {
                    this.initialResultsBySide[side] = result;
                }
            }
        }

        runCoordinator = new AnalysisWindowRunCoordinator(
            this.dataService,
            this.initialResultsBySide,
            this.analysisProgress);
    }

    public GameAnalysisResult? CurrentResult => selectionState.CurrentResult;

    public bool CanAnalyze => engineAnalyzer is not null;

    public bool IsAnalysisRunning
    {
        get => isAnalysisRunning;
        private set
        {
            if (SetProperty(ref isAnalysisRunning, value))
            {
                OnPropertyChanged(nameof(CanRunAnalysis));
                OnPropertyChanged(nameof(CanUseSelectedMistake));
                OnPropertyChanged(nameof(CanRecordFeedback));
            }
        }
    }

    public bool CanRunAnalysis => CanAnalyze && !IsAnalysisRunning;

    public bool CanUseSelectedMistake => SelectedMistake is not null && !IsAnalysisRunning;

    public bool CanRecordFeedback => importedGame is not null && CanUseSelectedMistake;

    public PlayerSide InitialSide => initialSide;

    public PlayerSide ActiveSide => selectionState.CurrentResult?.AnalyzedSide ?? initialSide;

    public IReadOnlyList<AnalysisSideOption> SideOptions => AnalysisWindowSetupService.SideOptions;

    public IReadOnlyList<AnalysisFilterOption> FilterOptions => AnalysisWindowSetupService.FilterOptions;

    public AnalysisSideOption? SelectedSideOption
    {
        get => selectedSideOption;
        set
        {
            if (SetProperty(ref selectedSideOption, value))
            {
                OnPropertyChanged(nameof(SelectedSide));
            }
        }
    }

    public AnalysisFilterOption? SelectedFilterOption
    {
        get => selectedFilterOption;
        set => SetProperty(ref selectedFilterOption, value);
    }

    public PlayerSide SelectedSide => SelectedSideOption?.Side ?? initialSide;

    public OpeningPhaseReview? CurrentOpeningReview => selectionState.CurrentResult?.OpeningReview;

    public IReadOnlySet<int> ReviewedPlies => selectionState.ReviewedPlies;

    public string StatusText
    {
        get => statusText;
        set => SetProperty(ref statusText, value);
    }

    public string SummaryText
    {
        get => summaryText;
        set => SetProperty(ref summaryText, value);
    }

    public string AdviceStatusText
    {
        get => adviceStatusText;
        private set => SetProperty(ref adviceStatusText, value);
    }

    public string DetailsPlaceholderText
    {
        get => detailsPlaceholderText;
        private set => SetProperty(ref detailsPlaceholderText, value);
    }

    public IReadOnlyList<SelectedMistakeViewItem> VisibleMistakes
    {
        get => visibleMistakes;
        private set => SetProperty(ref visibleMistakes, value);
    }

    public SelectedMistakeViewItem? SelectedMistake
    {
        get => selectedMistake;
        set
        {
            if (SetProperty(ref selectedMistake, value))
            {
                OnPropertyChanged(nameof(CanUseSelectedMistake));
                OnPropertyChanged(nameof(CanRecordFeedback));
            }
        }
    }

    public AnalysisWindowState? LoadWindowState()
    {
        if (importedGame is null)
        {
            return null;
        }

        return dataService.TryGetWindowState(importedGame, out AnalysisWindowState? state)
            ? state
            : null;
    }

    public void ApplyWindowState(AnalysisWindowState state)
    {
        SelectedSideOption = AnalysisWindowSetupService.GetSideOption(state.SelectedSide);
        SelectedFilterOption = AnalysisWindowSetupService.GetFilterOption(state.QualityFilterIndex);
    }

    public void StoreWindowState()
        => StoreWindowState(SelectedSide, AnalysisWindowSetupService.GetFilterIndex(SelectedFilterOption));

    public void StoreWindowState(PlayerSide side, int qualityFilterIndex)
    {
        if (importedGame is null)
        {
            return;
        }

        dataService.StoreWindowState(
            importedGame,
            AnalysisWindowSetupService.CreateWindowState(side, qualityFilterIndex));
    }

    public Task<AnalysisWindowRunOutcome> AnalyzeAsync(PlayerSide side)
        => runCoordinator.AnalyzeAsync(importedGame, engineAnalyzer, side, DefaultAnalysisOptions);

    public void BeginAnalysis(PlayerSide side)
    {
        IsAnalysisRunning = true;
        StatusText = $"Analyzing imported game for {side}...";
        SummaryText = string.Empty;
        ClearVisibleMistakes();
        ShowAnalyzingPlaceholder();
    }

    public void EndAnalysis()
    {
        IsAnalysisRunning = false;
    }

    public AnalysisWindowRunOutcome TryLoadCached(PlayerSide side)
        => runCoordinator.TryLoadCached(importedGame, side, DefaultAnalysisOptions);

    public AnalysisWindowRunOutcome ResetAndTryLoadCachedSelectedSide()
    {
        ClearVisibleMistakes();
        ClearCurrentResult();
        ShowRunAnalysisPlaceholder();
        InvalidatePendingExplanations();

        AnalysisWindowRunOutcome outcome = TryLoadCached(SelectedSide);
        if (!outcome.HasResult)
        {
            SummaryText = outcome.StatusText;
        }

        return outcome;
    }

    public void ApplyRunOutcome(AnalysisWindowRunOutcome outcome)
    {
        StatusText = outcome.StatusText;
        if (outcome.HasResult && outcome.Result is not null)
        {
            selectionState.SetCurrentResult(outcome.Result, outcome.IsCached);
        }
        else
        {
            selectionState.ClearCurrentResult();
            SummaryText = string.Empty;
            if (outcome.IsError)
            {
                ShowAnalysisFailedPlaceholder();
            }
            else
            {
                ShowRunAnalysisPlaceholder();
            }
        }

        OnPropertyChanged(nameof(CurrentResult));
        OnPropertyChanged(nameof(ActiveSide));
        OnPropertyChanged(nameof(CurrentOpeningReview));
    }

    public void ClearCurrentResult()
    {
        selectionState.ClearCurrentResult();
        OnPropertyChanged(nameof(CurrentResult));
        OnPropertyChanged(nameof(ActiveSide));
        OnPropertyChanged(nameof(CurrentOpeningReview));
    }

    public AnalysisFilterResult ApplyFilter(AnalysisFilterOption? filter, int? selectedPly)
    {
        AnalysisFilterResult result = selectionState.BuildFilterResult(filter);
        VisibleMistakes = result.Items;
        SummaryText = result.SummaryText;
        SelectedMistake = result.Items.Count == 0
            ? null
            : selectedPly is int ply
                ? result.Items.FirstOrDefault(item => item.LeadMove.Replay.Ply == ply) ?? result.Items[0]
                : result.Items[0];
        return result;
    }

    public void ClearVisibleMistakes()
    {
        VisibleMistakes = [];
        SelectedMistake = null;
    }

    public void ShowRunAnalysisPlaceholder()
        => DetailsPlaceholderText = "Run analysis to inspect highlighted mistakes.";

    public void ShowAnalyzingPlaceholder()
        => DetailsPlaceholderText = "The analysis engine is reviewing the imported game. This may take a moment.";

    public void ShowSelectMistakePlaceholder()
        => DetailsPlaceholderText = "Select a highlighted mistake to inspect details.";

    public void ShowNoFilterMatchesPlaceholder()
        => DetailsPlaceholderText = "No items match the current filter.";

    public void ShowAnalysisFailedPlaceholder()
        => DetailsPlaceholderText = "Analysis failed.";

    public void ShowAllReviewedPlaceholder()
        => DetailsPlaceholderText = "All visible highlights are reviewed.";

    public void MarkReviewed(MoveAnalysisResult lead)
    {
        selectionState.MarkReviewed(lead);
    }

    public bool IsReviewed(MoveAnalysisResult lead)
        => selectionState.IsReviewed(lead);

    public AnalysisReviewActionResult MarkSelectedReviewed(bool moveToNext)
    {
        if (SelectedMistake is not SelectedMistakeViewItem item)
        {
            return new AnalysisReviewActionResult(null, ShouldRenderAllReviewedPlaceholder: false);
        }

        int reviewedPly = item.LeadMove.Replay.Ply;
        MarkReviewed(item.LeadMove);
        if (!moveToNext)
        {
            return new AnalysisReviewActionResult(null, ShouldRenderAllReviewedPlaceholder: false);
        }

        SelectedMistakeViewItem? next = VisibleMistakes
            .Where(item => !IsReviewed(item.LeadMove))
            .OrderBy(item => item.LeadMove.Replay.Ply <= reviewedPly)
            .ThenBy(item => item.LeadMove.Replay.Ply)
            .FirstOrDefault();
        if (next is null)
        {
            ShowAllReviewedPlaceholder();
            return new AnalysisReviewActionResult(null, ShouldRenderAllReviewedPlaceholder: true);
        }

        SelectedMistake = next;
        return new AnalysisReviewActionResult(next, ShouldRenderAllReviewedPlaceholder: false);
    }

    public AdviceRuntimeStatus RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = explanationService.RefreshRuntimeState();
        AdviceStatusText = status.StatusText;
        return status;
    }

    public AnalysisWindowSelectedDetails? PrepareSelectedDetails()
    {
        if (SelectedMistake is not SelectedMistakeViewItem item)
        {
            ShowSelectMistakePlaceholder();
            return null;
        }

        MoveAnalysisResult lead = item.LeadMove;
        AnalysisPreparedExplanation preparedExplanation = PrepareExplanation(lead);
        AnalysisSelectedDetailsPresentation details = BuildSelectedDetails(item, lead, preparedExplanation.Explanation, !preparedExplanation.IsCached);
        AnalysisExplanationRequest? request = preparedExplanation.IsCached ? null : preparedExplanation.Request;
        int requestId = preparedExplanation.IsCached ? 0 : BeginExplanationRequest();
        return new AnalysisWindowSelectedDetails(item, lead, details, request, requestId);
    }

    public async Task<AnalysisWindowSelectedDetails?> LoadGeneratedDetailsAsync(AnalysisWindowSelectedDetails pendingDetails)
    {
        if (pendingDetails.ExplanationRequest is null)
        {
            return null;
        }

        MoveExplanation? explanation = await GenerateAndCacheExplanationAsync(
            pendingDetails.Lead,
            pendingDetails.ExplanationRequest,
            pendingDetails.ExplanationRequestId);
        if (explanation is null || !ReferenceEquals(SelectedMistake, pendingDetails.Item))
        {
            return null;
        }

        AnalysisSelectedDetailsPresentation details = BuildSelectedDetails(
            pendingDetails.Item,
            pendingDetails.Lead,
            explanation,
            isLoading: false);
        return pendingDetails with { Details = details, ExplanationRequest = null, ExplanationRequestId = 0 };
    }

    private AnalysisSelectedDetailsPresentation BuildSelectedDetails(
        SelectedMistakeViewItem item,
        MoveAnalysisResult lead,
        MoveExplanation explanation,
        bool isLoading)
    {
        MoveAdviceFeedback? feedback = FindLatestFeedback(lead);
        return AnalysisSelectedDetailsPresenter.Build(
            item.Mistake,
            lead,
            CurrentOpeningReview,
            explanation,
            isLoading,
            feedback);
    }

    private AnalysisPreparedExplanation PrepareExplanation(MoveAnalysisResult lead)
        => explanationService.Prepare(lead);

    private int BeginExplanationRequest()
        => explanationService.BeginRequest();

    public void InvalidatePendingExplanations()
    {
        explanationService.InvalidatePendingRequests();
    }

    private Task<MoveExplanation?> GenerateAndCacheExplanationAsync(
        MoveAnalysisResult lead,
        AnalysisExplanationRequest request,
        int requestId)
    {
        if (importedGame is null)
        {
            return Task.FromResult<MoveExplanation?>(null);
        }

        return explanationService.GenerateAndCacheAsync(
            importedGame,
            lead,
            selectionState.CurrentResult?.AnalyzedSide,
            request,
            requestId);
    }

    private MoveAdviceFeedback? FindLatestFeedback(MoveAnalysisResult lead)
    {
        if (importedGame is null)
        {
            return null;
        }

        return dataService.FindLatestFeedback(
            importedGame,
            ActiveSide,
            DefaultAnalysisOptions,
            lead);
    }

    public string? RecordFeedback(
        SelectedMistakeViewItem item,
        AdviceFeedbackKind feedbackKind,
        string? correctedLabel = null,
        string? comment = null)
    {
        if (importedGame is null)
        {
            return null;
        }

        return AnalysisFeedbackRecorder.Record(
            dataService,
            importedGame,
            ActiveSide,
            DefaultAnalysisOptions,
            item,
            feedbackKind,
            correctedLabel,
            comment);
    }

    public bool CanSaveManualCorrection(string? correctedLabel)
    {
        if (!string.IsNullOrWhiteSpace(correctedLabel))
        {
            return true;
        }

        StatusText = "Choose or enter a corrected label first.";
        return false;
    }
}

internal sealed record AnalysisWindowSelectedDetails(
    SelectedMistakeViewItem Item,
    MoveAnalysisResult Lead,
    AnalysisSelectedDetailsPresentation Details,
    AnalysisExplanationRequest? ExplanationRequest,
    int ExplanationRequestId)
{
    public bool ShouldLoadExplanation => ExplanationRequest is not null;
}

internal sealed record AnalysisReviewActionResult(
    SelectedMistakeViewItem? NextSelection,
    bool ShouldRenderAllReviewedPlaceholder);
