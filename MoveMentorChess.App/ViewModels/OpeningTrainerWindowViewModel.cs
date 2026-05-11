using System.Collections.ObjectModel;
using Avalonia.Media;
using MoveMentorChess.Persistence;
using MoveMentorChess.Training;

namespace MoveMentorChess.App.ViewModels;

public sealed class OpeningTrainerWindowViewModel : ViewModelBase
{
    private const int SelectionPageIndex = 0;
    private const int OverviewPageIndex = 1;
    private const int StudyPageIndex = 2;
    private const int ResultsPageIndex = 3;
    private const int TotalPages = 4;

    private readonly OpeningTrainerWorkspaceService workspaceService;
    private readonly HashSet<string> studyAvailableTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OpeningTrainingAttemptResult> currentSessionAttempts = [];
    private readonly HashSet<string> completedNextActionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> scheduledActionIdsBySource = new(StringComparer.OrdinalIgnoreCase);
    private OpeningLineCatalogItem? selectedOpening;
    private TrainingRecommendationCard? todayRecommendation;
    private TrainingPriorityItem? selectedPriority;
    private TrainingNextAction? selectedNextAction;
    private OpeningTrainingAnswerOption? selectedAnswerOption;
    private TrainingSessionOutcomeSummary? outcomeSummary;
    private PlayerOpeningPlan? playerOpeningPlan;
    private SpecialTrainingModeDefinition? selectedSpecialMode;
    private OpeningTrainerOverview? overview;
    private OpeningTrainingSession? guidedSession;
    private string? studySelectedSquare;
    private string? studyPreviewTargetSquare;
    private int currentPageIndex = SelectionPageIndex;
    private int currentStepIndex;
    private int completedSteps;
    private int correctAnswers;
    private int playableAnswers;
    private int wrongAttempts;
    private int transposedAnswers;
    private int hintUseCount;
    private int currentHintIndex;
    private DateTime? studyStartedUtc;
    private DateTime? firstMoveUtc;
    private string? currentStartSource;
    private string? currentRecommendationId;
    private bool studyAbandonedTracked;
    private bool sessionResultSaved;
    private string filterText = string.Empty;
    private string playerKey = string.Empty;
    private RepertoireSide selectedSide = RepertoireSide.Both;
    private OpeningTrainingStrictness selectedStrictness = OpeningTrainingStrictness.BookFlexible;
    private string previewFen = new ChessGame().GetFen();
    private string summaryText = "Choose an opening to load theory overview.";
    private string opponentSummary = "Opponent prep data will appear here.";
    private string coverageText = "Coverage is unavailable.";
    private string coverageExplanation = "Pick an opening to see what is covered and what still needs work.";
    private string currentPrompt = "Guided study is idle.";
    private string currentWhy = string.Empty;
    private string currentHintText = "Hints will appear here when you ask for one.";
    private string currentHintLevel = "No hint used";
    private string moveInput = string.Empty;
    private string resultText = string.Empty;
    private string resultHeadline = "Finish a guided study session to see results.";
    private string resultRecommendation = "Your next review recommendations will appear here.";
    private bool isAdvancedOptionsExpanded;

    public OpeningTrainerWindowViewModel()
        : this(AnalysisStoreProvider.GetStore() ?? throw new InvalidOperationException("Local analysis store is unavailable."))
    {
    }

    public OpeningTrainerWindowViewModel(IAnalysisStore analysisStore)
    {
        workspaceService = new OpeningTrainerWorkspaceService(analysisStore);
        RefreshCommand = new RelayCommand(RefreshOpenings);
        GoToOverviewCommand = new RelayCommand(OpenOverviewPage, () => SelectedOpening is not null && overview is not null);
        GoToSelectionCommand = new RelayCommand(() => SetPage(SelectionPageIndex));
        StartRecommendedStudyCommand = new RelayCommand(StartRecommendedStudy, () => TodayRecommendation is not null);
        StartGuidedStudyCommand = new RelayCommand(StartGuidedStudy, () => SelectedOpening is not null && overview is not null);
        StartPriorityStudyCommand = new RelayCommand(StartPriorityStudy, () => SelectedPriority is not null && SelectedOpening is not null && overview is not null);
        StartSpecialModeCommand = new RelayCommand(StartSpecialMode, () => SelectedSpecialMode is not null && SelectedOpening is not null && overview is not null);
        ShowHintCommand = new RelayCommand(ShowNextHint, () => CurrentPosition is not null);
        EvaluateMoveCommand = new RelayCommand(EvaluateMove, CanEvaluateCurrentAnswer);
        NextStepCommand = new RelayCommand(MoveNext, () => guidedSession is not null && currentStepIndex < guidedSession.Positions.Count);
        PreviousStepCommand = new RelayCommand(MovePrevious, () => guidedSession is not null && currentStepIndex > 0);
        RestartStudyCommand = new RelayCommand(RestartStudy, () => SelectedOpening is not null && overview is not null);
        ExecuteNextActionCommand = new RelayCommand(ExecuteSelectedNextAction, () => SelectedNextAction is not null);

        RefreshOpenings();
        workspaceService.TrackTelemetry(OpeningTrainingTelemetryEvents.OpeningTrainerOpened, PlayerKey);
    }

    public ObservableCollection<OpeningLineCatalogItem> OpeningItems { get; } = [];

    public ObservableCollection<string> MainLineItems { get; } = [];

    public ObservableCollection<string> BranchItems { get; } = [];

    public ObservableCollection<TrainingPriorityItem> PriorityItems { get; } = [];

    public ObservableCollection<string> WeakPositionItems { get; } = [];

    public ObservableCollection<string> ResultItems { get; } = [];

    public ObservableCollection<TrainingNextAction> NextActionItems { get; } = [];

    public ObservableCollection<OpeningTrainingAnswerOption> AnswerOptionItems { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> TodayPlanItems { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> WeeklyPlanItems { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> LongTermGapItems { get; } = [];

    public ObservableCollection<SpecialTrainingModeDefinition> SpecialTrainingModes { get; } = [];

    public IReadOnlyList<RepertoireSide> AvailableSides { get; } = Enum.GetValues<RepertoireSide>();

    public IReadOnlyList<OpeningTrainingStrictness> AvailableStrictnessOptions { get; } = Enum.GetValues<OpeningTrainingStrictness>();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand GoToOverviewCommand { get; }

    public RelayCommand GoToSelectionCommand { get; }

    public RelayCommand StartRecommendedStudyCommand { get; }

    public RelayCommand StartGuidedStudyCommand { get; }

    public RelayCommand StartPriorityStudyCommand { get; }

    public RelayCommand StartSpecialModeCommand { get; }

    public RelayCommand ShowHintCommand { get; }

    public RelayCommand EvaluateMoveCommand { get; }

    public RelayCommand NextStepCommand { get; }

    public RelayCommand PreviousStepCommand { get; }

    public RelayCommand RestartStudyCommand { get; }

    public RelayCommand ExecuteNextActionCommand { get; }

    public string FilterText
    {
        get => filterText;
        set => SetProperty(ref filterText, value);
    }

    public string PlayerKey
    {
        get => playerKey;
        set
        {
            if (SetProperty(ref playerKey, value))
            {
                RefreshTodayRecommendation();
                LoadOverview();
            }
        }
    }

    public RepertoireSide SelectedSide
    {
        get => selectedSide;
        set
        {
            if (SetProperty(ref selectedSide, value))
            {
                RefreshOpenings();
            }
        }
    }

    public OpeningTrainingStrictness SelectedStrictness
    {
        get => selectedStrictness;
        set => SetProperty(ref selectedStrictness, value);
    }

    public OpeningLineCatalogItem? SelectedOpening
    {
        get => selectedOpening;
        set
        {
            if (SetProperty(ref selectedOpening, value))
            {
                LoadOverview();
                RaiseNavigationStateChanged();
            }
        }
    }

    public TrainingRecommendationCard? TodayRecommendation
    {
        get => todayRecommendation;
        private set => SetProperty(ref todayRecommendation, value);
    }

    public bool HasTodayRecommendation => TodayRecommendation is not null;

    public string TodayRecommendationOpening => TodayRecommendation?.OpeningLine.DisplayName ?? "No recommendation available";

    public string TodayRecommendationMeta => TodayRecommendation is null
        ? "Import opening theory to enable recommendations."
        : $"{TodayRecommendation.OpeningLine.RepertoireSide} | {TodayRecommendation.Difficulty} | about {TodayRecommendation.EstimatedDurationMinutes} min";

    public string TodayRecommendationReason => TodayRecommendation?.Reason ?? "The trainer needs at least one available opening line.";

    public string TodayRecommendationAction => TodayRecommendation?.RecommendedAction ?? "Start guided study";

    public string TodayLessonOpening => TodayRecommendation?.OpeningLine.DisplayName ?? "Import openings to get today's lesson";

    public string TodayLessonSideText => TodayRecommendation is null
        ? "No active theory"
        : TodayRecommendation.OpeningLine.RepertoireSide switch
        {
            RepertoireSide.White => "White repertoire",
            RepertoireSide.Black => "Black repertoire",
            _ => "Both sides"
        };

    public string TodayLessonDurationText => TodayRecommendation is null
        ? "Duration appears after import"
        : $"About {TodayRecommendation.EstimatedDurationMinutes} min";

    public string TodayLessonMoveCountText => TodayRecommendation is null
        ? "No positions to train"
        : TodayRecommendation.OpeningLine.BookBranchCount > 0
            ? $"{TodayRecommendation.OpeningLine.BookBranchCount} positions / branches"
            : $"{Math.Max(1, TodayRecommendation.OpeningLine.BookGameCount)} theory games";

    public string TodayLessonReason => TodayRecommendation?.Reason ?? "Import opening theory to get today's lesson.";

    public string TodayLessonButtonText => HasTodayLesson ? "Start Training" : "No Lesson Available";

    public bool HasTodayLesson => TodayRecommendation is not null;

    public bool IsAdvancedOptionsExpanded
    {
        get => isAdvancedOptionsExpanded;
        set => SetProperty(ref isAdvancedOptionsExpanded, value);
    }

    public PlayerOpeningPlan? PlayerOpeningPlan
    {
        get => playerOpeningPlan;
        private set => SetProperty(ref playerOpeningPlan, value);
    }

    public string PlayerOpeningPlanTitle => PlayerOpeningPlan is null
        ? "Opening rhythm"
        : $"{PlayerOpeningPlan.DisplayName} opening rhythm";

    public string PlayerOpeningPlanSummary => PlayerOpeningPlan?.Summary ?? "Opening rhythm will appear after loading local theory.";

    public string PlayerOpeningProgressText => PlayerOpeningPlan is null
        ? "No player progress loaded."
        : $"Sessions {PlayerOpeningPlan.Progress.SessionCount} | Attempts {PlayerOpeningPlan.Progress.AttemptCount} | Accuracy {PlayerOpeningPlan.Progress.AccuracyPercent:0.#}%";

    public SpecialTrainingModeDefinition? SelectedSpecialMode
    {
        get => selectedSpecialMode;
        set
        {
            if (SetProperty(ref selectedSpecialMode, value))
            {
                StartSpecialModeCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedSpecialModeDescription));
                OnPropertyChanged(nameof(SelectedSpecialModeButtonText));
            }
        }
    }

    public string SelectedSpecialModeDescription => SelectedSpecialMode?.Description ?? "Choose a special mode to start a focused preset.";

    public string SelectedSpecialModeButtonText => SelectedSpecialMode?.CommandLabel ?? "Start special mode";

    public TrainingPriorityItem? SelectedPriority
    {
        get => selectedPriority;
        set
        {
            if (SetProperty(ref selectedPriority, value))
            {
                StartPriorityStudyCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedPriorityActionText));
            }
        }
    }

    public bool HasPriorityItems => PriorityItems.Count > 0;

    public string PriorityItemsPlaceholder => HasPriorityItems
        ? string.Empty
        : "No ranked priorities are available for this opening yet.";

    public string SelectedPriorityActionText => SelectedPriority is null
        ? "Select a priority to train it."
        : SelectedPriority.Action switch
        {
            TrainingPriorityAction.RepairThisPosition => "Repair selected position",
            TrainingPriorityAction.ReviewOpponentReply => "Review selected reply",
            _ => "Train selected branch"
        };

    public string PreviewFen
    {
        get => previewFen;
        private set => SetProperty(ref previewFen, value);
    }

    public IReadOnlyList<BoardArrowViewModel> PreviewArrows { get; private set; } = [];

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string OpponentSummary
    {
        get => opponentSummary;
        private set => SetProperty(ref opponentSummary, value);
    }

    public string CoverageText
    {
        get => coverageText;
        private set => SetProperty(ref coverageText, value);
    }

    public string CoverageExplanation
    {
        get => coverageExplanation;
        private set => SetProperty(ref coverageExplanation, value);
    }

    public string CurrentPrompt
    {
        get => currentPrompt;
        private set => SetProperty(ref currentPrompt, value);
    }

    public string CurrentWhy
    {
        get => currentWhy;
        private set => SetProperty(ref currentWhy, value);
    }

    public string CurrentHintText
    {
        get => currentHintText;
        private set => SetProperty(ref currentHintText, value);
    }

    public string CurrentHintLevel
    {
        get => currentHintLevel;
        private set => SetProperty(ref currentHintLevel, value);
    }

    public string MoveInput
    {
        get => moveInput;
        set
        {
            if (SetProperty(ref moveInput, value))
            {
                EvaluateMoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public OpeningTrainingAnswerOption? SelectedAnswerOption
    {
        get => selectedAnswerOption;
        set
        {
            if (SetProperty(ref selectedAnswerOption, value))
            {
                EvaluateMoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasAnswerOptions => AnswerOptionItems.Count > 0;

    public string ResultText
    {
        get => resultText;
        private set => SetProperty(ref resultText, value);
    }

    public string ResultHeadline
    {
        get => resultHeadline;
        private set => SetProperty(ref resultHeadline, value);
    }

    public string ResultRecommendation
    {
        get => resultRecommendation;
        private set => SetProperty(ref resultRecommendation, value);
    }

    public TrainingSessionOutcomeSummary? OutcomeSummary
    {
        get => outcomeSummary;
        private set => SetProperty(ref outcomeSummary, value);
    }

    public TrainingNextAction? SelectedNextAction
    {
        get => selectedNextAction;
        set
        {
            if (SetProperty(ref selectedNextAction, value))
            {
                ExecuteNextActionCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedNextActionButtonText));
            }
        }
    }

    public bool HasNextActions => NextActionItems.Count > 0;

    public string NextActionsPlaceholder => HasNextActions
        ? string.Empty
        : "Finish a session to unlock the next action plan.";

    public string SelectedNextActionButtonText => SelectedNextAction?.CommandLabel ?? "Select next action";

    public string? StudySelectedSquare => studySelectedSquare;

    public string? StudyPreviewTargetSquare
    {
        get => studyPreviewTargetSquare;
        private set => SetProperty(ref studyPreviewTargetSquare, value);
    }

    public IReadOnlyList<string> StudyAvailableMoveSquares => studyAvailableTargets.ToList();

    public string StudyBoardHint => CurrentPosition is null
        ? "Start a guided study to use the board."
        : studySelectedSquare is null
            ? "Click one of your pieces on the board, then click the target square to submit the move."
            : $"Selected {studySelectedSquare}. Click a highlighted target square to play the move.";

    public string StudyInputModeText => CurrentPosition is null
        ? "Board input is idle."
        : CurrentPosition.AnswerKind == OpeningTrainingAnswerKind.Move
            ? "Board input is active. The trainer accepts the main book move and, depending on strictness, also good alternatives from theory."
            : "Choose the answer option that best explains the position.";

    public string StageTitle => currentPageIndex switch
    {
        SelectionPageIndex => "Step 1 of 4: Choose an opening",
        OverviewPageIndex => "Step 2 of 4: Review the structure",
        StudyPageIndex => "Step 3 of 4: Guided study",
        ResultsPageIndex => "Step 4 of 4: Results and next review",
        _ => "Opening Trainer"
    };

    public string StageDescription => currentPageIndex switch
    {
        SelectionPageIndex => "Start with today's recommendation, or browse all openings when you want a specific line.",
        OverviewPageIndex => "Inspect the main line, common opponent branches, and current coverage before you begin.",
        StudyPageIndex => "Play through the book moves step by step and get immediate feedback.",
        ResultsPageIndex => "See how stable your line is and what should come back in review.",
        _ => string.Empty
    };

    public double StageProgressPercent => (currentPageIndex + 1d) / TotalPages * 100d;

    public bool IsSelectionPageVisible => currentPageIndex == SelectionPageIndex;

    public bool IsOverviewPageVisible => currentPageIndex == OverviewPageIndex;

    public bool IsStudyPageVisible => currentPageIndex == StudyPageIndex;

    public bool IsResultsPageVisible => currentPageIndex == ResultsPageIndex;

    public bool HasSelectedOpening => SelectedOpening is not null;

    public string SelectedOpeningName => SelectedOpening?.DisplayName ?? "No opening selected";

    public string SelectedOpeningSideText => SelectedOpening is null
        ? "Choose an item from the list"
        : $"{SelectedOpening.RepertoireSide} repertoire";

    public string WeakPositionsPlaceholder => WeakPositionItems.Count == 0
        ? "No weak positions are saved for this opening yet."
        : string.Empty;

    public bool ShowWeakPositionsPlaceholder => WeakPositionItems.Count == 0;

    public double StudyProgressPercent => guidedSession is null || guidedSession.Positions.Count == 0
        ? 0
        : Math.Round((double)Math.Min(currentStepIndex + 1, guidedSession.Positions.Count) / guidedSession.Positions.Count * 100d, 1);

    public string StudyProgressText => guidedSession is null
        ? "Guided study has not started."
        : $"Position {Math.Min(currentStepIndex + 1, guidedSession.Positions.Count)}/{guidedSession.Positions.Count}";

    public string ResultsSummaryText => guidedSession is null
        ? "No session data yet."
        : $"Completed {completedSteps}/{guidedSession.Positions.Count} positions | Correct {correctAnswers} | Playable {playableAnswers} | Wrong attempts {wrongAttempts} | Hints used {hintUseCount}";

    public string TranspositionSummaryText => transposedAnswers == 0
        ? "No transpositions were used in the last run."
        : $"Transposed to known positions {transposedAnswers} time(s).";

    private OpeningTrainingPosition? CurrentPosition => guidedSession is not null && currentStepIndex >= 0 && currentStepIndex < guidedSession.Positions.Count
        ? guidedSession.Positions[currentStepIndex]
        : null;

    private void RefreshOpenings()
    {
        IReadOnlyList<OpeningLineCatalogItem> items = workspaceService.ListOpeningLines(FilterText, SelectedSide, 120);
        ReplaceItems(OpeningItems, items);
        RefreshTodayRecommendation();
        if (items.Count > 0)
        {
            OpeningLineCatalogItem? recommendedLine = TodayRecommendation?.OpeningLine;
            SelectedOpening = recommendedLine is not null && items.Contains(recommendedLine)
                ? recommendedLine
                : items[0];
        }
        else
        {
            SelectedOpening = null;
            overview = null;
            ReplaceItems(MainLineItems, []);
            ReplaceItems(BranchItems, []);
            ReplaceItems(PriorityItems, []);
            SelectedPriority = null;
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "No openings matched the current filter.";
            OpponentSummary = "Opponent prep data is unavailable.";
            CoverageText = "Coverage is unavailable.";
            CoverageExplanation = "Try another filter or repertoire side.";
            OnPropertyChanged(nameof(WeakPositionsPlaceholder));
            OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
            RaisePriorityStateChanged();
        }
    }

    private void RefreshTodayRecommendation()
    {
        TodayRecommendation = workspaceService.GetRecommendationForToday(PlayerKey, SelectedSide, 120);
        if (TodayRecommendation is not null)
        {
            workspaceService.TrackTelemetry(
                OpeningTrainingTelemetryEvents.OpeningRecommendationShown,
                PlayerKey,
                TodayRecommendation.OpeningLine,
                recommendationId: TodayRecommendation.OpeningLine.LineKey.Value,
                properties: new Dictionary<string, string>
                {
                    ["reason_code"] = TodayRecommendation.ReasonCode.ToString(),
                    ["recommendation_type"] = TodayRecommendation.RecommendationType.ToString()
                });
        }

        PlayerOpeningPlan = workspaceService.GetPlayerOpeningPlan(PlayerKey, SelectedSide, 120);
        ReplaceItems(SpecialTrainingModes, workspaceService.ListSpecialTrainingModes());
        SelectedSpecialMode ??= SpecialTrainingModes.FirstOrDefault();
        ReplaceItems(TodayPlanItems, PlayerOpeningPlan.Today);
        ReplaceItems(WeeklyPlanItems, PlayerOpeningPlan.ThisWeek);
        ReplaceItems(LongTermGapItems, PlayerOpeningPlan.LongTermGaps);
        OnPropertyChanged(nameof(HasTodayRecommendation));
        OnPropertyChanged(nameof(TodayRecommendationOpening));
        OnPropertyChanged(nameof(TodayRecommendationMeta));
        OnPropertyChanged(nameof(TodayRecommendationReason));
        OnPropertyChanged(nameof(TodayRecommendationAction));
        OnPropertyChanged(nameof(TodayLessonOpening));
        OnPropertyChanged(nameof(TodayLessonSideText));
        OnPropertyChanged(nameof(TodayLessonDurationText));
        OnPropertyChanged(nameof(TodayLessonMoveCountText));
        OnPropertyChanged(nameof(TodayLessonReason));
        OnPropertyChanged(nameof(TodayLessonButtonText));
        OnPropertyChanged(nameof(HasTodayLesson));
        OnPropertyChanged(nameof(PlayerOpeningPlanTitle));
        OnPropertyChanged(nameof(PlayerOpeningPlanSummary));
        OnPropertyChanged(nameof(PlayerOpeningProgressText));
        OnPropertyChanged(nameof(SelectedSpecialModeDescription));
        OnPropertyChanged(nameof(SelectedSpecialModeButtonText));
        StartRecommendedStudyCommand.RaiseCanExecuteChanged();
        StartSpecialModeCommand.RaiseCanExecuteChanged();
    }

    private void StartRecommendedStudy()
    {
        if (TodayRecommendation is null)
        {
            return;
        }

        SelectedOpening = TodayRecommendation.OpeningLine;
        StartGuidedStudy(null, "today_recommendation", TodayRecommendation.OpeningLine.LineKey.Value);
    }

    private void StartPriorityStudy()
    {
        if (SelectedPriority is null)
        {
            return;
        }

        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.OverviewRecommendationSelected,
            PlayerKey,
            SelectedOpening,
            recommendationId: SelectedPriority.Id,
            properties: new Dictionary<string, string>
            {
                ["action"] = SelectedPriority.Action.ToString(),
                ["reason_code"] = SelectedPriority.ReasonCode.ToString()
            });
        StartGuidedStudy(null, "overview_priority", SelectedPriority.Id, BuildSessionTarget(SelectedPriority));
    }

    private void StartSpecialMode()
    {
        if (SelectedSpecialMode is null)
        {
            return;
        }

        StartGuidedStudy(SelectedSpecialMode, $"special_mode:{SelectedSpecialMode.Kind}", null);
    }

    private void OpenOverviewPage()
    {
        if (overview is null)
        {
            return;
        }

        SetPage(OverviewPageIndex);
    }

    private void LoadOverview()
    {
        if (SelectedOpening is null || !workspaceService.TryGetOverview(SelectedOpening, PlayerKey, out OpeningTrainerOverview? loadedOverview) || loadedOverview is null)
        {
            overview = null;
            ReplaceItems(MainLineItems, []);
            ReplaceItems(BranchItems, []);
            ReplaceItems(PriorityItems, []);
            SelectedPriority = null;
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "Could not load opening overview.";
            OpponentSummary = "Opponent prep data is unavailable.";
            CoverageText = "Coverage is unavailable.";
            CoverageExplanation = "The selected opening does not have enough local theory data yet.";
            OnPropertyChanged(nameof(WeakPositionsPlaceholder));
            OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
            RaisePriorityStateChanged();
            return;
        }

        overview = loadedOverview;
        PreviewFen = SelectedOpening.RootFen;
        PreviewArrows = [];
        OnPropertyChanged(nameof(PreviewArrows));
        SummaryText = $"{SelectedOpening.DisplayName}{Environment.NewLine}Main line moves: {overview.MainLine.Count}";
        OpponentSummary = overview.OpponentReplyProfile.Summary;
        CoverageText = $"Coverage: {overview.Coverage.CoveragePercent:0.#}% | Covered {overview.Coverage.CoveredBranches}/{overview.Coverage.TotalBookBranches} | Weak {overview.Coverage.WeakBranches}";
        CoverageExplanation = overview.Coverage.CoveragePercent <= 0.1
            ? "You do not have saved review progress for this opening yet, so coverage starts at zero."
            : $"Stable branches: {overview.Coverage.StableBranches}. Unseen common branches: {overview.Coverage.UnseenCommonBranches}.";
        ReplaceItems(MainLineItems, overview.MainLine.Select(move =>
            $"{move.MoveNumber}. {move.San} {(string.IsNullOrWhiteSpace(move.Idea?.ShortExplanation) ? string.Empty : $"| {move.Idea!.ShortExplanation}")}").ToList());
        ReplaceItems(BranchItems, overview.CommonBranches.Select(branch =>
            $"{branch.OpponentMove} | freq {branch.Frequency} | {branch.SourceSummary}").ToList());
        ReplaceItems(PriorityItems, overview.Priorities);
        SelectedPriority = PriorityItems.FirstOrDefault();
        ReplaceItems(WeakPositionItems, overview.WeakPositions.Select(position =>
            $"{position.OpeningName} | {position.Instruction}").ToList());
        ResultText = string.Empty;
        OnPropertyChanged(nameof(WeakPositionsPlaceholder));
        OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
        RaisePriorityStateChanged();
    }

    private void StartGuidedStudy()
        => StartGuidedStudy(null, "manual", null);

    private void StartGuidedStudy(SpecialTrainingModeDefinition? specialMode, string startSource, string? recommendationId)
        => StartGuidedStudy(specialMode, startSource, recommendationId, null);

    private void StartGuidedStudy(
        SpecialTrainingModeDefinition? specialMode,
        string startSource,
        string? recommendationId,
        OpeningTrainingSessionTarget? target)
    {
        if (SelectedOpening is null || overview is null)
        {
            return;
        }

        guidedSession = workspaceService.BuildGuidedStudySession(SelectedOpening, overview, PlayerKey, SelectedStrictness, specialMode, target);
        studyStartedUtc = DateTime.UtcNow;
        firstMoveUtc = null;
        currentStartSource = startSource;
        currentRecommendationId = startSource is "today_recommendation" or "overview_priority"
            ? recommendationId
            : null;
        studyAbandonedTracked = false;
        sessionResultSaved = false;
        currentSessionAttempts.Clear();
        completedNextActionIds.Clear();
        scheduledActionIdsBySource.Clear();
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.OpeningTrainingStarted,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            specialMode: specialMode?.Kind,
            properties: new Dictionary<string, string>
            {
                ["start_source"] = startSource,
                ["position_count"] = guidedSession.Positions.Count.ToString(),
                ["target_fallback"] = (target is not null && !guidedSession.Positions.Any(position => IsTargetedPosition(position, target))).ToString().ToLowerInvariant()
            });
        if (specialMode is not null)
        {
            workspaceService.TrackTelemetry(
                OpeningTrainingTelemetryEvents.SpecialModeStarted,
                PlayerKey,
                SelectedOpening,
                guidedSession,
                specialMode: specialMode.Kind,
                properties: new Dictionary<string, string>
                {
                    ["time_limit_minutes"] = specialMode.TimeLimitMinutes.ToString(),
                    ["max_positions"] = specialMode.MaxPositions.ToString()
                });
        }
        currentStepIndex = 0;
        MoveInput = string.Empty;
        ResultText = string.Empty;
        ResetResults();
        ClearStudySelection();
        LoadCurrentStep();
        SetPage(StudyPageIndex);
    }

    private static OpeningTrainingSessionTarget? BuildSessionTarget(TrainingPriorityItem? priority)
    {
        return priority is null
            ? null
            : new OpeningTrainingSessionTarget(
                priority.Id,
                priority.Action,
                priority.LineKey,
                priority.BranchKey,
                priority.PositionKey,
                priority.MoveSan,
                priority.MoveUci);
    }

    private static bool IsTargetedPosition(OpeningTrainingPosition position, OpeningTrainingSessionTarget target)
    {
        return target.Action switch
        {
            TrainingPriorityAction.RepairThisPosition => target.PositionKey.HasValue
                && position.OpeningPositionKey.Equals(target.PositionKey.Value),
            TrainingPriorityAction.TrainThisBranch or TrainingPriorityAction.ReviewOpponentReply =>
                (target.BranchKey.HasValue
                    && position.OpeningBranchKey.HasValue
                    && position.OpeningBranchKey.Value.Equals(target.BranchKey.Value))
                || (!string.IsNullOrWhiteSpace(target.OpponentMoveUci)
                    && position.CandidateMoves.Any(option => string.Equals(option.Uci, target.OpponentMoveUci, StringComparison.OrdinalIgnoreCase)))
                || (!string.IsNullOrWhiteSpace(target.OpponentMove)
                    && position.CandidateMoves.Any(option => string.Equals(option.DisplayText, target.OpponentMove, StringComparison.OrdinalIgnoreCase))),
            _ => false
        };
    }

    private void RestartStudy()
    {
        StartGuidedStudy();
    }

    private void EvaluateMove()
    {
        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null)
        {
            return;
        }

        firstMoveUtc ??= DateTime.UtcNow;
        string submittedAnswer = position.AnswerKind == OpeningTrainingAnswerKind.Move
            ? MoveInput
            : SelectedAnswerOption?.Id ?? string.Empty;
        OpeningTrainingAttemptResult result = workspaceService.Evaluate(position, submittedAnswer);
        currentSessionAttempts.Add(result);
        string statusText = result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
            ? "TransposedToKnownPosition"
            : result.Score.ToString();
        ResultText = $"{statusText}: {result.ShortExplanation}";
        CurrentWhy = result.RecoverySuggestion
            ?? result.WhyThisMove?.ShortExplanation
            ?? position.BetterMoveReason
            ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(result.RecoverySuggestion))
        {
            CurrentHintLevel = result.NextHintLevel.HasValue
                ? $"Next hint: {result.NextHintLevel.Value}"
                : "Recovery";
            CurrentHintText = result.RecoverySuggestion;
        }
        AddResultLine(result);

        if (result.Score == OpeningTrainingScore.Wrong)
        {
            wrongAttempts++;
            ClearStudySelection();
            RaiseResultsStateChanged();
            return;
        }

        completedSteps++;
        if (result.Score == OpeningTrainingScore.Correct)
        {
            correctAnswers++;
        }
        else
        {
            playableAnswers++;
        }

        if (result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition)
        {
            transposedAnswers++;
        }

        guidedSession = workspaceService.RebuildContinuationAfterAcceptedMove(
            guidedSession!,
            currentStepIndex,
            position,
            result);
        MoveInput = string.Empty;
        SelectedAnswerOption = null;
        ClearStudySelection();
        MoveNext();
        RaiseResultsStateChanged();
    }

    private bool CanEvaluateCurrentAnswer()
    {
        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null)
        {
            return false;
        }

        return position.AnswerKind == OpeningTrainingAnswerKind.Move
            ? !string.IsNullOrWhiteSpace(MoveInput)
            : SelectedAnswerOption is not null;
    }

    public void HandleStudyBoardSquarePressed(string squareName)
    {
        if (!IsStudyPageVisible || string.IsNullOrWhiteSpace(squareName))
        {
            return;
        }

        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null)
        {
            return;
        }

        if (position.AnswerKind != OpeningTrainingAnswerKind.Move)
        {
            return;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(position.Fen, out _))
        {
            return;
        }

        if (studySelectedSquare is null)
        {
            TrySelectStudySourceSquare(game, squareName);
            return;
        }

        if (string.Equals(studySelectedSquare, squareName, StringComparison.OrdinalIgnoreCase))
        {
            ClearStudySelection();
            return;
        }

        List<LegalMoveInfo> matchingMoves = game.GetLegalMoves()
            .Where(move => string.Equals(move.FromSquare, studySelectedSquare, StringComparison.OrdinalIgnoreCase)
                && string.Equals(move.ToSquare, squareName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingMoves.Count == 0)
        {
            ClearStudySelection();
            TrySelectStudySourceSquare(game, squareName);
            return;
        }

        string? uci = SelectStudyMoveToApply(matchingMoves, position);
        if (string.IsNullOrWhiteSpace(uci))
        {
            ResultText = "This move could not be resolved from the board selection.";
            ClearStudySelection();
            return;
        }

        StudyPreviewTargetSquare = squareName;
        MoveInput = uci;
        EvaluateMove();
    }

    private void MoveNext()
    {
        if (guidedSession is null)
        {
            return;
        }

        if (currentStepIndex < guidedSession.Positions.Count - 1)
        {
            currentStepIndex++;
            LoadCurrentStep();
        }
        else
        {
            CompleteStudy();
        }

        RaiseStudyNavigationStateChanged();
    }

    private void MovePrevious()
    {
        if (guidedSession is null)
        {
            return;
        }

        if (currentStepIndex > 0)
        {
            currentStepIndex--;
            LoadCurrentStep();
        }

        RaiseStudyNavigationStateChanged();
    }

    private void LoadCurrentStep()
    {
        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null)
        {
            CurrentPrompt = "Guided study is idle.";
            CurrentWhy = string.Empty;
            PreviewArrows = [];
            ReplaceItems(AnswerOptionItems, []);
            SelectedAnswerOption = null;
            ResetCurrentHint();
            ClearStudySelection();
            OnPropertyChanged(nameof(PreviewArrows));
            OnPropertyChanged(nameof(HasAnswerOptions));
            RaiseStudyNavigationStateChanged();
            return;
        }

        PreviewFen = position.Fen;
        CurrentPrompt = $"Step {currentStepIndex + 1}/{guidedSession!.Positions.Count}: {position.Prompt}";
        CurrentWhy = position.BetterMoveReason ?? position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)?.Idea?.ShortExplanation ?? string.Empty;
        PreviewArrows = [];
        ReplaceItems(AnswerOptionItems, position.AnswerOptions ?? []);
        SelectedAnswerOption = AnswerOptionItems.FirstOrDefault();
        ResetCurrentHint();
        ClearStudySelection();
        OnPropertyChanged(nameof(PreviewArrows));
        OnPropertyChanged(nameof(HasAnswerOptions));
        RaiseStudyNavigationStateChanged();
    }

    private void CompleteStudy()
    {
        int positionCount = guidedSession?.Positions.Count ?? 0;
        OutcomeSummary = BuildOutcomeSummary(positionCount);
        OpeningTrainingSessionResult? savedSessionResult = null;
        if (guidedSession is not null && !sessionResultSaved)
        {
            sessionResultSaved = true;
            savedSessionResult = workspaceService.SaveSessionResult(
                guidedSession,
                currentSessionAttempts,
                OpeningTrainingSessionOutcome.Completed,
                currentStartSource,
                currentRecommendationId,
                hintUseCount,
                GetTimeToFirstMoveSeconds(),
                null,
                completedNextActionIds.ToList());
            RefreshTodayRecommendation();
            LoadOverview();
        }

        ResultHeadline = guidedSession is null
            ? "Guided study finished."
            : $"Finished {guidedSession.Positions.Count} guided positions for {SelectedOpeningName}.";
        ResultRecommendation = wrongAttempts > 0
            ? "Repeat this line soon. Wrong attempts suggest at least one branch still needs reinforcement."
            : playableAnswers > 0 || transposedAnswers > 0
                ? "The line is mostly stable. Review again after a short break to make the moves automatic."
                : "This line looks stable. You can move on to another branch or opening.";
        ReplaceItems(NextActionItems, workspaceService.BuildNextActions(OutcomeSummary));
        scheduledActionIdsBySource.Clear();
        if (savedSessionResult is not null)
        {
            IReadOnlyList<OpeningTrainingScheduledAction> scheduledActions = workspaceService.SaveScheduledActions(savedSessionResult, NextActionItems.ToList());
            foreach (OpeningTrainingScheduledAction action in scheduledActions)
            {
                if (!string.IsNullOrWhiteSpace(action.SourceActionId))
                {
                    scheduledActionIdsBySource[action.SourceActionId] = action.Id;
                }
            }
        }

        SelectedNextAction = NextActionItems.FirstOrDefault();
        RaiseNextActionStateChanged();
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedSessionCompleted,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: new Dictionary<string, string>
            {
                ["start_source"] = currentStartSource ?? "unknown",
                ["completed_steps"] = completedSteps.ToString(),
                ["wrong_attempts"] = wrongAttempts.ToString(),
                ["hint_count"] = hintUseCount.ToString(),
                ["time_to_first_move_seconds"] = GetTimeToFirstMoveSeconds().ToString()
            });
        SetPage(ResultsPageIndex);
    }

    private void ResetResults()
    {
        completedSteps = 0;
        correctAnswers = 0;
        playableAnswers = 0;
        wrongAttempts = 0;
        transposedAnswers = 0;
        hintUseCount = 0;
        currentHintIndex = 0;
        ResultHeadline = "Guided study in progress.";
        ResultRecommendation = "Finish the run to get a follow-up recommendation.";
        ReplaceItems(ResultItems, []);
        ReplaceItems(NextActionItems, []);
        SelectedNextAction = null;
        OutcomeSummary = null;
        ResetCurrentHint();
        RaiseNextActionStateChanged();
        RaiseResultsStateChanged();
    }

    private void AddResultLine(OpeningTrainingAttemptResult result)
    {
        string label = result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
            ? "Transposed"
            : result.Score.ToString();
        ResultItems.Insert(0, $"{label} | {result.SubmittedMoveText} | {result.ShortExplanation}");
    }

    private void SetPage(int pageIndex)
    {
        TrackAbandonmentIfLeavingStudy(pageIndex);
        if (!SetProperty(ref currentPageIndex, pageIndex, nameof(currentPageIndex)))
        {
            return;
        }

        OnPropertyChanged(nameof(StageTitle));
        OnPropertyChanged(nameof(StageDescription));
        OnPropertyChanged(nameof(StageProgressPercent));
        OnPropertyChanged(nameof(IsSelectionPageVisible));
        OnPropertyChanged(nameof(IsOverviewPageVisible));
        OnPropertyChanged(nameof(IsStudyPageVisible));
        OnPropertyChanged(nameof(IsResultsPageVisible));
    }

    private void RaiseNavigationStateChanged()
    {
        GoToOverviewCommand.RaiseCanExecuteChanged();
        StartRecommendedStudyCommand.RaiseCanExecuteChanged();
        StartGuidedStudyCommand.RaiseCanExecuteChanged();
        StartPriorityStudyCommand.RaiseCanExecuteChanged();
        StartSpecialModeCommand.RaiseCanExecuteChanged();
        RestartStudyCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedOpening));
        OnPropertyChanged(nameof(SelectedOpeningName));
        OnPropertyChanged(nameof(SelectedOpeningSideText));
    }

    private void RaiseStudyNavigationStateChanged()
    {
        ShowHintCommand.RaiseCanExecuteChanged();
        EvaluateMoveCommand.RaiseCanExecuteChanged();
        NextStepCommand.RaiseCanExecuteChanged();
        PreviousStepCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(StudyProgressPercent));
        OnPropertyChanged(nameof(StudyProgressText));
        OnPropertyChanged(nameof(StudySelectedSquare));
        OnPropertyChanged(nameof(StudyAvailableMoveSquares));
        OnPropertyChanged(nameof(StudyBoardHint));
        OnPropertyChanged(nameof(StudyInputModeText));
        OnPropertyChanged(nameof(CurrentHintText));
        OnPropertyChanged(nameof(CurrentHintLevel));
        OnPropertyChanged(nameof(HasAnswerOptions));
    }

    private void RaiseResultsStateChanged()
    {
        OnPropertyChanged(nameof(ResultsSummaryText));
        OnPropertyChanged(nameof(TranspositionSummaryText));
        OnPropertyChanged(nameof(OutcomeSummary));
    }

    private TrainingSessionOutcomeSummary BuildOutcomeSummary(int positionCount)
    {
        double completion = positionCount == 0
            ? 0
            : Math.Round((double)completedSteps / positionCount * 100d, 1);
        int accepted = correctAnswers + playableAnswers;
        double accuracy = completedSteps == 0
            ? 0
            : Math.Round((double)accepted / completedSteps * 100d, 1);

        string headline = wrongAttempts > 0
            ? "Needs reinforcement"
            : playableAnswers > 0 || hintUseCount > 0
                ? "Almost stable"
                : "Stable line";

        return new TrainingSessionOutcomeSummary(
            headline,
            positionCount,
            completedSteps,
            correctAnswers,
            playableAnswers,
            wrongAttempts,
            hintUseCount,
            completion,
            accuracy);
    }

    private void ExecuteSelectedNextAction()
    {
        if (SelectedNextAction is null)
        {
            return;
        }

        TrainingNextAction action = SelectedNextAction;
        completedNextActionIds.Add(action.Id);
        if (scheduledActionIdsBySource.TryGetValue(action.Id, out string? scheduledActionId)
            && action.Kind == TrainingNextActionKind.RepeatNow)
        {
            workspaceService.MarkScheduledActionCompleted(PlayerKey, scheduledActionId, DateTime.UtcNow);
        }

        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.ResultsNextActionClicked,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: new Dictionary<string, string>
            {
                ["next_action_id"] = action.Id,
                ["next_action_kind"] = action.Kind.ToString()
            });

        switch (action.Kind)
        {
            case TrainingNextActionKind.RepeatNow:
                RestartStudy();
                break;
            case TrainingNextActionKind.RepeatAfterBreak:
                ResultText = action.DelayMinutes > 0
                    ? $"Scheduled. This review will be due in about {action.DelayMinutes} minute(s)."
                    : "Scheduled for a later review.";
                RefreshTodayRecommendation();
                break;
            case TrainingNextActionKind.RepairWeakBranches:
                SetPage(OverviewPageIndex);
                break;
            case TrainingNextActionKind.BrowseAnotherOpening:
            case TrainingNextActionKind.ReturnTomorrow:
                SetPage(SelectionPageIndex);
                break;
        }
    }

    private int GetTimeToFirstMoveSeconds()
    {
        return studyStartedUtc.HasValue && firstMoveUtc.HasValue
            ? Math.Max(0, (int)Math.Round((firstMoveUtc.Value - studyStartedUtc.Value).TotalSeconds))
            : 0;
    }

    private void TrackAbandonmentIfLeavingStudy(int nextPageIndex)
    {
        if (studyAbandonedTracked
            || currentPageIndex != StudyPageIndex
            || nextPageIndex == StudyPageIndex
            || nextPageIndex == ResultsPageIndex
            || guidedSession is null
            || sessionResultSaved
            || completedSteps >= guidedSession.Positions.Count)
        {
            return;
        }

        studyAbandonedTracked = true;
        sessionResultSaved = true;
        DateTime abandonedUtc = DateTime.UtcNow;
        workspaceService.SaveSessionResult(
            guidedSession,
            currentSessionAttempts,
            OpeningTrainingSessionOutcome.Abandoned,
            currentStartSource,
            currentRecommendationId,
            hintUseCount,
            GetTimeToFirstMoveSeconds(),
            abandonedUtc,
            completedNextActionIds.ToList());
        RefreshTodayRecommendation();
        LoadOverview();
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedSessionAbandoned,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: new Dictionary<string, string>
            {
                ["start_source"] = currentStartSource ?? "unknown",
                ["completed_steps"] = completedSteps.ToString(),
                ["position_count"] = guidedSession.Positions.Count.ToString()
            });
    }

    private void RaiseNextActionStateChanged()
    {
        OnPropertyChanged(nameof(HasNextActions));
        OnPropertyChanged(nameof(NextActionsPlaceholder));
        OnPropertyChanged(nameof(SelectedNextActionButtonText));
        ExecuteNextActionCommand.RaiseCanExecuteChanged();
    }

    private void ShowNextHint()
    {
        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null)
        {
            return;
        }

        IReadOnlyList<TrainingCoachHint> hints = workspaceService.BuildCoachHints(position);
        if (hints.Count == 0)
        {
            CurrentHintLevel = "No hint available";
            CurrentHintText = "This position does not have a coaching hint yet.";
            return;
        }

        TrainingCoachHint hint = hints[Math.Min(currentHintIndex, hints.Count - 1)];
        CurrentHintLevel = $"{hint.Level}: {hint.Title}";
        CurrentHintText = hint.Text;
        if (hint.Level >= TrainingCoachHintLevel.Structure)
        {
            PreviewArrows = BuildArrows(position);
            OnPropertyChanged(nameof(PreviewArrows));
        }

        if (currentHintIndex < hints.Count - 1)
        {
            currentHintIndex++;
        }

        hintUseCount++;
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedHintUsed,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            properties: new Dictionary<string, string>
            {
                ["hint_level"] = hint.Level.ToString(),
                ["position_id"] = position.PositionId
            });
        RaiseResultsStateChanged();
    }

    private void ResetCurrentHint()
    {
        currentHintIndex = 0;
        CurrentHintLevel = "No hint used";
        CurrentHintText = "Hints will appear here when you ask for one.";
    }

    private void RaisePriorityStateChanged()
    {
        OnPropertyChanged(nameof(HasPriorityItems));
        OnPropertyChanged(nameof(PriorityItemsPlaceholder));
        OnPropertyChanged(nameof(SelectedPriorityActionText));
        StartPriorityStudyCommand.RaiseCanExecuteChanged();
    }

    private static IReadOnlyList<BoardArrowViewModel> BuildArrows(OpeningTrainingPosition position)
    {
        OpeningTrainingMoveOption? expected = position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)
            ?? position.CandidateMoves.FirstOrDefault();
        if (expected is null || string.IsNullOrWhiteSpace(expected.Uci))
        {
            return [];
        }

        ChessGame game = new();
        if (!game.TryLoadFen(position.Fen, out _)
            || !game.TryApplyUci(expected.Uci, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return [];
        }

        return [new BoardArrowViewModel(appliedMove.FromSquare, appliedMove.ToSquare, Color.Parse("#2146FF"))];
    }

    private static void ReplaceItems<T>(ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        collection.Clear();
        foreach (T item in items)
        {
            collection.Add(item);
        }
    }

    private void TrySelectStudySourceSquare(ChessGame game, string squareName)
    {
        if (!TryGetPieceAt(game.GetFen(), squareName, out string? piece) || string.IsNullOrEmpty(piece))
        {
            return;
        }

        bool isWhitePiece = char.IsUpper(piece[0]);
        if (isWhitePiece != game.WhiteToMove)
        {
            return;
        }

        List<LegalMoveInfo> movesForPiece = game.GetLegalMoves()
            .Where(move => string.Equals(move.FromSquare, squareName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (movesForPiece.Count == 0)
        {
            return;
        }

        studySelectedSquare = squareName;
        studyAvailableTargets.Clear();
        foreach (LegalMoveInfo move in movesForPiece)
        {
            studyAvailableTargets.Add(move.ToSquare);
        }

        StudyPreviewTargetSquare = null;
        RaiseStudyNavigationStateChanged();
    }

    private void ClearStudySelection()
    {
        studySelectedSquare = null;
        studyAvailableTargets.Clear();
        StudyPreviewTargetSquare = null;
        OnPropertyChanged(nameof(StudySelectedSquare));
        OnPropertyChanged(nameof(StudyAvailableMoveSquares));
        OnPropertyChanged(nameof(StudyBoardHint));
    }

    private static string? SelectStudyMoveToApply(
        IReadOnlyList<LegalMoveInfo> matchingMoves,
        OpeningTrainingPosition position)
    {
        if (matchingMoves.Count == 0)
        {
            return null;
        }

        if (matchingMoves.Count == 1)
        {
            return matchingMoves[0].Uci;
        }

        string? matchingBookMove = matchingMoves
            .Select(move => move.Uci)
            .FirstOrDefault(uci => position.CandidateMoves.Any(option =>
                !string.IsNullOrWhiteSpace(option.Uci)
                && string.Equals(option.Uci, uci, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(matchingBookMove))
        {
            return matchingBookMove;
        }

        string queenPromotion = position.SideToMove == PlayerSide.White ? "Q" : "q";
        LegalMoveInfo? queenMove = matchingMoves
            .FirstOrDefault(move => string.Equals(move.PromotionPiece, queenPromotion, StringComparison.Ordinal));
        return queenMove?.Uci ?? matchingMoves[0].Uci;
    }

    private static bool TryGetPieceAt(string fen, string squareName, out string? piece)
    {
        piece = null;
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null
            || !TryParseSquare(squareName, out (int X, int Y) square))
        {
            return false;
        }

        piece = position.Board[square.X, square.Y];
        return !string.IsNullOrWhiteSpace(piece);
    }

    private static bool TryParseSquare(string squareName, out (int X, int Y) square)
    {
        square = default;
        if (string.IsNullOrWhiteSpace(squareName) || squareName.Length != 2)
        {
            return false;
        }

        char file = char.ToLowerInvariant(squareName[0]);
        char rank = squareName[1];
        if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
        {
            return false;
        }

        square = (file - 'a', 8 - (rank - '0'));
        return true;
    }
}
