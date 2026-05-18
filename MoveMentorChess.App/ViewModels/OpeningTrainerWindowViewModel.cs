using System.Collections.ObjectModel;
using Avalonia.Media;
using static MoveMentorChess.App.ViewModels.OpeningTrainerPresentationText;
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
    private readonly OpeningStudyFeedbackAnimator studyFeedbackAnimator = new();
    private readonly OpeningTrainerTelemetryAdapter telemetryAdapter = new();
    private readonly HashSet<string> studyAvailableTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OpeningTrainingAttemptResult> currentSessionAttempts = [];
    private readonly HashSet<string> completedNextActionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> scheduledActionIdsBySource = new(StringComparer.OrdinalIgnoreCase);
    private OpeningLineCatalogItem? selectedOpening;
    private TrainingRecommendationCard? todayRecommendation;
    private TrainingPriorityItem? selectedPriority;
    private TrainingNextAction? selectedNextAction;
    private TrainingNextActionCardViewModel? selectedSecondaryNextAction;
    private OpeningTrainingAnswerOption? selectedAnswerOption;
    private OpeningTrainingIntensityChoice? selectedIntensityChoice;
    private TrainingSessionOutcomeSummary? outcomeSummary;
    private TrainingResultLearningPlan? learningPlan;
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
    private string advancedPlayerKey = string.Empty;
    private OpeningTrainingProfileChoice? selectedProfileChoice;
    private RepertoireSide selectedSide = RepertoireSide.Both;
    private OpeningTrainingStrictness selectedStrictness = OpeningTrainingStrictness.BookFlexible;
    private string previewFen = new ChessGame().GetFen();
    private string summaryText = "Choose an opening to preview the plan.";
    private string opponentSummary = "Common replies will appear here.";
    private string coverageText = "No practice history yet.";
    private string coverageExplanation = "Pick an opening to see what is ready and what needs another pass.";
    private string currentPrompt = "Practice is idle.";
    private string currentWhy = string.Empty;
    private string currentHintText = "Hints will appear here when you ask for one.";
    private string currentHintLevel = "No hint used";
    private string moveInput = string.Empty;
    private string resultText = string.Empty;
    private string studyFeedbackText = string.Empty;
    private IBrush studyFeedbackBrush = Brushes.Transparent;
    private IBrush studyFeedbackBorderBrush = Brushes.Transparent;
    private double studyFeedbackOpacity;
    private string resultHeadline = "Finish practice to see your review plan.";
    private string resultRecommendation = "Your next review suggestion will appear here.";
    private bool isStudyReferenceVisible;
    private bool canRevealStudyReference;
    private bool isAdvancedOptionsExpanded;
    private bool overviewOpenedFromTodayRecommendation;

    public OpeningTrainerWindowViewModel(IAnalysisStore analysisStore)
        : this(new OpeningTrainerWorkspaceService(analysisStore))
    {
    }

    public OpeningTrainerWindowViewModel(OpeningTrainerWorkspaceService workspaceService)
    {
        this.workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        RefreshCommand = new RelayCommand(RefreshOpenings);
        GoToOverviewCommand = new RelayCommand(OpenOverviewPage, () => SelectedOpening is not null && overview is not null);
        GoToSelectionCommand = new RelayCommand(() => SetPage(SelectionPageIndex));
        StartRecommendedStudyCommand = new RelayCommand(StartRecommendedStudy, () => TodayRecommendation is not null);
        StartRecommendedPracticeNowCommand = new RelayCommand(StartRecommendedPracticeNow, () => TodayRecommendation is not null);
        StartGuidedStudyCommand = new RelayCommand(StartGuidedStudy, () => SelectedOpening is not null && overview is not null);
        StartPriorityStudyCommand = new RelayCommand(StartPriorityStudy, () => SelectedPriority is not null && SelectedOpening is not null && overview is not null);
        StartSpecialModeCommand = new RelayCommand(StartSpecialMode, () => SelectedSpecialMode is not null && SelectedOpening is not null && overview is not null);
        ShowHintCommand = new RelayCommand(ShowNextHint, () => CurrentPosition is not null);
        DontKnowCommand = new RelayCommand(UseDontKnow, () => CanUseDontKnow);
        RevealStudyReferenceCommand = new RelayCommand(RevealStudyReference, () => CanRevealStudyReference);
        EvaluateMoveCommand = new RelayCommand(EvaluateMove, CanEvaluateCurrentAnswer);
        NextStepCommand = new RelayCommand(MoveNext, () => guidedSession is not null && currentStepIndex < guidedSession.Positions.Count);
        PreviousStepCommand = new RelayCommand(MovePrevious, () => guidedSession is not null && currentStepIndex > 0);
        RestartStudyCommand = new RelayCommand(RestartStudy, () => SelectedOpening is not null && overview is not null);
        ExecuteNextActionCommand = new RelayCommand(ExecuteSelectedNextAction, () => SelectedNextAction is not null);
        ExecutePrimaryNextActionCommand = new RelayCommand(ExecutePrimaryNextAction, () => PrimaryNextAction is not null);
        ExecuteSecondaryNextActionCommand = new RelayCommand<TrainingNextActionCardViewModel>(
            ExecuteSecondaryNextAction,
            action => action is not null);
        ExecuteSelectedSecondaryNextActionCommand = new RelayCommand(
            ExecuteSelectedSecondaryNextAction,
            () => SelectedSecondaryNextAction is not null);

        selectedProfileChoice = AvailableProfileChoices.First(choice => choice.Id == "both");
        selectedIntensityChoice = AvailableIntensityChoices.First(choice => choice.Id == "balanced");
        selectedSide = selectedProfileChoice.Side;
        selectedStrictness = selectedIntensityChoice.Strictness;
        RefreshOpenings();
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.OpeningTrainerOpened,
            PlayerKey,
            SelectedOpening,
            properties: BuildBaseTelemetryProperties());
    }

    public ObservableCollection<OpeningLineCatalogItem> OpeningItems { get; } = [];

    public ObservableCollection<string> MainLineItems { get; } = [];

    public ObservableCollection<string> BranchItems { get; } = [];

    public ObservableCollection<TrainingPriorityItem> PriorityItems { get; } = [];

    public ObservableCollection<string> WeakPositionItems { get; } = [];

    public ObservableCollection<string> ResultItems { get; } = [];

    public ObservableCollection<TrainingNextAction> NextActionItems { get; } = [];

    public ObservableCollection<TrainingNextActionCardViewModel> NextActionCards { get; } = [];

    public ObservableCollection<TrainingNextActionCardViewModel> SecondaryNextActionCards { get; } = [];

    public ObservableCollection<OpeningTrainingAnswerOption> AnswerOptionItems { get; } = [];

    public ObservableCollection<TrainingResultReviewItem> LearningPlanReviewItems { get; } = [];

    public ObservableCollection<OpeningUnderstandingCard> UnderstandingCards { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> TodayPlanItems { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> WeeklyPlanItems { get; } = [];

    public ObservableCollection<PlayerOpeningPlanItem> LongTermGapItems { get; } = [];

    public ObservableCollection<SpecialTrainingModeDefinition> SpecialTrainingModes { get; } = [];

    public IReadOnlyList<OpeningTrainingProfileChoice> AvailableProfileChoices { get; } =
    [
        new("white", "Play White", "Build today's training from your White repertoire.", RepertoireSide.White, "opening-coach:white"),
        new("black", "Play Black", "Build today's training from your Black repertoire.", RepertoireSide.Black, "opening-coach:black"),
        new("both", "Both Sides", "Use the full repertoire when choosing today's training.", RepertoireSide.Both, "opening-coach:both")
    ];

    public IReadOnlyList<OpeningTrainingIntensityChoice> AvailableIntensityChoices { get; } =
    [
        new("safe", "Safe Review", "Only due known positions.", OpeningTrainingStrictness.StrictRepertoire),
        new("balanced", "Balanced", "Due positions plus weak branches.", OpeningTrainingStrictness.BookFlexible),
        new("challenge", "Challenge", "Adds less familiar opponent replies.", OpeningTrainingStrictness.Exploration)
    ];

    public IReadOnlyList<RepertoireSide> AvailableSides { get; } = Enum.GetValues<RepertoireSide>();

    public IReadOnlyList<OpeningTrainingStrictness> AvailableStrictnessOptions { get; } = Enum.GetValues<OpeningTrainingStrictness>();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand GoToOverviewCommand { get; }

    public RelayCommand GoToSelectionCommand { get; }

    public RelayCommand StartRecommendedStudyCommand { get; }

    public RelayCommand StartRecommendedPracticeNowCommand { get; }

    public RelayCommand StartGuidedStudyCommand { get; }

    public RelayCommand StartPriorityStudyCommand { get; }

    public RelayCommand StartSpecialModeCommand { get; }

    public RelayCommand ShowHintCommand { get; }

    public RelayCommand DontKnowCommand { get; }

    public RelayCommand RevealStudyReferenceCommand { get; }

    public RelayCommand EvaluateMoveCommand { get; }

    public RelayCommand NextStepCommand { get; }

    public RelayCommand PreviousStepCommand { get; }

    public RelayCommand RestartStudyCommand { get; }

    public RelayCommand ExecuteNextActionCommand { get; }

    public RelayCommand ExecutePrimaryNextActionCommand { get; }

    public RelayCommand<TrainingNextActionCardViewModel> ExecuteSecondaryNextActionCommand { get; }

    public RelayCommand ExecuteSelectedSecondaryNextActionCommand { get; }

    public void OpenLineFromCoverage(OpeningLineCatalogItem opening)
    {
        ArgumentNullException.ThrowIfNull(opening);
        SelectedOpening = opening;
        SetPage(OverviewPageIndex);
    }

    public string FilterText
    {
        get => filterText;
        set => SetProperty(ref filterText, value);
    }

    public string PlayerKey => !string.IsNullOrWhiteSpace(AdvancedPlayerKey)
        ? AdvancedPlayerKey.Trim()
        : SelectedProfileChoice?.PlayerKey ?? "opening-coach:both";

    public string AdvancedPlayerKey
    {
        get => advancedPlayerKey;
        set
        {
            if (SetProperty(ref advancedPlayerKey, value))
            {
                OnPropertyChanged(nameof(PlayerKey));
                RefreshTodayRecommendation();
                LoadOverview();
            }
        }
    }

    public OpeningTrainingProfileChoice? SelectedProfileChoice
    {
        get => selectedProfileChoice;
        set
        {
            if (SetProperty(ref selectedProfileChoice, value))
            {
                OnPropertyChanged(nameof(PlayerKey));
                OnPropertyChanged(nameof(SelectedProfileSummary));
                if (value is not null && selectedSide != value.Side)
                {
                    selectedSide = value.Side;
                    OnPropertyChanged(nameof(SelectedSide));
                }

                RefreshOpenings();
            }
        }
    }

    public string SelectedProfileSummary => SelectedProfileChoice is null
        ? "Choose how today's training should pick from your repertoire."
        : SelectedProfileChoice.Description;

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
                OnPropertyChanged(nameof(IsBoardRotated));
            }
        }
    }

    public TrainingRecommendationCard? TodayRecommendation
    {
        get => todayRecommendation;
        private set
        {
            if (SetProperty(ref todayRecommendation, value))
            {
                RaiseTodayRecommendationStateChanged();
            }
        }
    }

    public bool HasTodayRecommendation => TodayRecommendation is not null;

    public string TodayRecommendationOpening => TodayRecommendation?.OpeningLine.DisplayName ?? "No recommendation available";

    public string TodayRecommendationMeta => TodayRecommendation is null
        ? "Import opening theory to enable recommendations."
        : $"{TodayRecommendation.OpeningLine.RepertoireSide} | {TodayRecommendation.Difficulty} | about {TodayRecommendation.EstimatedDurationMinutes} min";

    public string TodayRecommendationReason => TodayRecommendation?.Reason ?? "The trainer needs at least one available opening line.";

    public string TodayRecommendationAction => TodayRecommendation?.RecommendedAction ?? "Start practice";

    public string TodayLessonOpening => TodayRecommendation?.OpeningLine.DisplayName ?? "Choose an opening first";

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

    public string TodayLessonReason => TodayRecommendation?.Reason ?? "Import or choose an opening to start today's training.";

    public string TodayDecisionSummary => TodayRecommendation is null
        ? "Recommended today: import or choose an opening before starting."
        : $"Today: {GetRecommendedPositionCount()} review positions from a {GetReviewMoveCount()}-move line, approx. {GetEstimatedDurationText()}, {FormatRepertoireSide(TodayRecommendation.OpeningLine.RepertoireSide)} repertoire.";

    public string TodayStartSequenceText => SelectedIntensityChoice?.Id switch
    {
        "safe" => "Starts with due known positions. Weak-branch repairs stay optional.",
        "challenge" => "Starts with due positions, then adds less familiar opponent replies.",
        _ => "Starts with due positions, then repairs weak branches."
    };

    public string TodayLessonReasonDetail
    {
        get
        {
            if (TodayRecommendation is null)
            {
                return "Once theory is available, the trainer will explain why this line is the next safest use of your time.";
            }

            int weakBranches = overview?.Coverage.WeakBranches ?? TodayRecommendation.OpeningLine.BookBranchCount;
            string reason = TodayRecommendation.Reason.Trim();
            if (TodayRecommendation.ReasonCode == TrainingRecommendationReasonCode.RevisitDue && TodayRecommendation.Priority >= 10_000)
            {
                reason = "This review is due because some scheduled items passed their review window.";
            }

            string modeContext = SelectedIntensityChoice?.Id switch
            {
                "safe" => "Safe Review keeps weak-branch repair optional.",
                "challenge" => "Challenge mode adds less familiar opponent replies after the main line.",
                _ => "Balanced mode adds weak-branch repair after the main line."
            };
            string goal = weakBranches > 0
                ? $"Goal: confirm the main setup and repair {weakBranches} weak branch{PluralSuffix(weakBranches)}."
                : "Goal: confirm the main setup and keep recall stable.";

            return $"{reason} {modeContext}{Environment.NewLine}{goal}";
        }
    }

    public string TodayTrainingReasonLabel => HasTodayLesson
        ? "Recommended because..."
        : "Ready when you are";

    public string TodayLessonButtonText => HasTodayLesson ? "Start guided training" : "Import openings first";

    public bool HasTodayLesson => TodayRecommendation is not null;

    public OpeningTrainingIntensityChoice? SelectedIntensityChoice
    {
        get => selectedIntensityChoice;
        set
        {
            if (SetProperty(ref selectedIntensityChoice, value))
            {
                SelectedStrictness = value?.Strictness ?? OpeningTrainingStrictness.BookFlexible;
                OnPropertyChanged(nameof(SelectedIntensitySummary));
                OnPropertyChanged(nameof(TodayDecisionSummary));
                OnPropertyChanged(nameof(TodayStartSequenceText));
                OnPropertyChanged(nameof(TodayLessonReasonDetail));
                OnPropertyChanged(nameof(PracticeFocusText));
            }
        }
    }

    public string SelectedIntensitySummary => SelectedIntensityChoice?.Description
        ?? "Choose how cautious today's practice should feel.";

    public bool IsAdvancedOptionsExpanded
    {
        get => isAdvancedOptionsExpanded;
        set
        {
            bool wasExpanded = isAdvancedOptionsExpanded;
            if (SetProperty(ref isAdvancedOptionsExpanded, value) && value && !wasExpanded)
            {
                workspaceService.TrackTelemetry(
                    OpeningTrainingTelemetryEvents.OpeningAdvancedOpened,
                    PlayerKey,
                    SelectedOpening,
                    properties: BuildBaseTelemetryProperties());
            }
        }
    }

    public PlayerOpeningPlan? PlayerOpeningPlan
    {
        get => playerOpeningPlan;
        private set => SetProperty(ref playerOpeningPlan, value);
    }

    public string PlayerOpeningPlanTitle => "Your training rhythm";

    public string PlayerOpeningPlanSummary => PlayerOpeningPlan?.Summary ?? "Your training rhythm will appear after loading local theory.";

    public string PlayerOpeningProgressText => PlayerOpeningPlan is null
        ? "No practice history yet."
        : PlayerOpeningPlan.Progress.SessionCount == 0
            ? "Start a session to build repertoire progress."
            : $"{PlayerOpeningPlan.Progress.AttemptCount} moves practiced, {PlayerOpeningPlan.Progress.AccuracyPercent:0.#}% accepted.";

    public string PlayerOpeningProgressInterpretation => PlayerOpeningPlan is null
        ? "Progress history will turn into a short coaching note after your first completed session."
        : PlayerOpeningPlan.Progress.SessionCount == 0
            ? "You are starting fresh, so today's session focuses on building the first reliable recall path."
            : PlayerOpeningPlan.Progress.AccuracyPercent >= 80
                ? "Your accuracy is stable. Today's session focuses on retention, not new material."
                : "Recent practice still has some friction. Today's session keeps the scope controlled and repairs weak spots first.";

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
            TrainingPriorityAction.RepairThisPosition => "Repair This Position",
            TrainingPriorityAction.ReviewOpponentReply => "Review This Reply",
            _ => "Train Selected Branch"
        };

    public string PreviewFen
    {
        get => previewFen;
        private set => SetProperty(ref previewFen, value);
    }

    public bool IsBoardRotated => SelectedOpening?.RepertoireSide == RepertoireSide.Black;

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
        private set
        {
            if (SetProperty(ref currentHintLevel, value))
            {
                OnPropertyChanged(nameof(StudyBoardStatusText));
            }
        }
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
        private set
        {
            if (SetProperty(ref resultText, value))
            {
                OnPropertyChanged(nameof(StudyFeedbackSummaryText));
            }
        }
    }

    public string StudyFeedbackText
    {
        get => studyFeedbackText;
        private set => SetProperty(ref studyFeedbackText, value);
    }

    public IBrush StudyFeedbackBrush
    {
        get => studyFeedbackBrush;
        private set => SetProperty(ref studyFeedbackBrush, value);
    }

    public IBrush StudyFeedbackBorderBrush
    {
        get => studyFeedbackBorderBrush;
        private set => SetProperty(ref studyFeedbackBorderBrush, value);
    }

    public double StudyFeedbackOpacity
    {
        get => studyFeedbackOpacity;
        private set => SetProperty(ref studyFeedbackOpacity, value);
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

    public bool IsStudyReferenceVisible
    {
        get => isStudyReferenceVisible;
        private set
        {
            if (SetProperty(ref isStudyReferenceVisible, value))
            {
                OnPropertyChanged(nameof(StudyReferenceButtonText));
                OnPropertyChanged(nameof(IsStudyReferenceHidden));
                OnPropertyChanged(nameof(StudyReferencePromptText));
                RevealStudyReferenceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRevealStudyReference
    {
        get => canRevealStudyReference && CurrentPosition is not null && !IsStudyReferenceVisible;
        private set
        {
            if (SetProperty(ref canRevealStudyReference, value))
            {
                OnPropertyChanged(nameof(StudyReferenceButtonText));
                OnPropertyChanged(nameof(StudyReferencePromptText));
                RevealStudyReferenceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StudyReferenceButtonText => IsStudyReferenceVisible
        ? "Line shown"
        : "Reveal line";

    public bool IsStudyReferenceHidden => !IsStudyReferenceVisible;

    public string StudyReferencePromptText => CanRevealStudyReference
        ? "The line is available because you chose I Don't Know. Reveal it when you are ready to compare."
        : "Line reference unlocks after I Don't Know, so recall and hints come first.";

    public string DontKnowButtonText => "I Don't Know";

    public bool CanUseDontKnow => CurrentPosition is not null && !HasDontKnowAttemptForCurrentPosition();

    public TrainingSessionOutcomeSummary? OutcomeSummary
    {
        get => outcomeSummary;
        private set => SetProperty(ref outcomeSummary, value);
    }

    public TrainingResultLearningPlan? LearningPlan
    {
        get => learningPlan;
        private set
        {
            if (SetProperty(ref learningPlan, value))
            {
                OnPropertyChanged(nameof(LearningPlanMasteredText));
                OnPropertyChanged(nameof(LearningPlanRepeatText));
                OnPropertyChanged(nameof(LearningPlanNextReviewText));
                OnPropertyChanged(nameof(LearningPlanReasonText));
                OnPropertyChanged(nameof(ResultsCompletedMetricText));
                OnPropertyChanged(nameof(ResultsClearMetricText));
                OnPropertyChanged(nameof(ResultsAlternativesMetricText));
                OnPropertyChanged(nameof(ResultsHintsMetricText));
                OnPropertyChanged(nameof(ResultsRevisitMetricText));
                OnPropertyChanged(nameof(ResultsNextActionReasonText));
                OnPropertyChanged(nameof(HasLearningPlanReviewItems));
                OnPropertyChanged(nameof(LearningPlanReviewPlaceholder));
                OnPropertyChanged(nameof(HasAdvancedResultDetails));
            }
        }
    }

    public string LearningPlanMasteredText => LearningPlan?.MasteredText ?? "Mastered: finish practice to build a plan.";

    public string LearningPlanRepeatText => LearningPlan?.RepeatText ?? "To review: finish practice first.";

    public string LearningPlanNextReviewText => LearningPlan?.NextReviewText ?? "Next review: finish practice first.";

    public string LearningPlanReasonText => LearningPlan?.ReasonText ?? "Reason: the trainer will use your moves, hints, and misses.";

    public string ResultsMasteredLabel => "Completed";

    public string ResultsNeedsReviewLabel => "To Revisit";

    public string ResultsBiggestWeaknessText => OpeningTrainerResultPresentation.BuildBiggestWeaknessText(ResultTone, wrongAttempts);

    public string ResultsNextBestActionText => SelectedNextAction?.Title ?? "Finish practice to unlock the next best action.";

    public string ResultsNextActionReasonText => SelectedNextAction?.Description ?? LearningPlanReasonText;

    public bool HasAdvancedResultDetails => ResultItems.Count > 0 || LearningPlanReviewItems.Count > 0;

    public string ResultCelebrationTitle => OpeningTrainerResultPresentation.BuildCelebrationTitle(ResultTone, wrongAttempts);

    public string ResultCelebrationText => OpeningTrainerResultPresentation.BuildCelebrationText(ResultTone, completedSteps);

    public string ResultOutcomeBadge => OpeningTrainerResultPresentation.BuildOutcomeBadge(ResultTone, wrongAttempts);

    public string ResultNextStepSummary => OpeningTrainerResultPresentation.BuildNextStepSummary(ResultTone, PrimaryNextAction);

    public bool HasLearningPlanReviewItems => LearningPlanReviewItems.Count > 0;

    public string LearningPlanReviewPlaceholder => HasLearningPlanReviewItems
        ? string.Empty
        : "No urgent review positions from this run.";

    public string ResultsCompletedMetricText => guidedSession is null
        ? "0/0"
        : $"{completedSteps}/{guidedSession.Positions.Count}";

    public string ResultsClearMetricText => correctAnswers.ToString();

    public string ResultsAlternativesMetricText => playableAnswers.ToString();

    public string ResultsHintsMetricText => hintUseCount.ToString();

    public string ResultsRevisitMetricText => wrongAttempts.ToString();

    private TrainingResultTone ResultTone
    {
        get
        {
            return OpeningTrainerResultPresentation.DetermineTone(
                guidedSession is not null,
                completedSteps,
                playableAnswers,
                wrongAttempts,
                hintUseCount);
        }
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
                OnPropertyChanged(nameof(ResultsNextBestActionText));
                OnPropertyChanged(nameof(ResultsNextActionReasonText));
                ExecutePrimaryNextActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasNextActions => NextActionItems.Count > 0;

    public string NextActionsPlaceholder => HasNextActions
        ? string.Empty
        : "Finish a session to unlock the next action plan.";

    public string SelectedNextActionButtonText => SelectedNextAction?.CommandLabel ?? "Select next action";

    public TrainingNextActionCardViewModel? PrimaryNextAction => NextActionCards.FirstOrDefault();

    public bool HasPrimaryNextAction => PrimaryNextAction is not null;

    public TrainingNextActionCardViewModel? SelectedSecondaryNextAction
    {
        get => selectedSecondaryNextAction;
        set
        {
            if (SetProperty(ref selectedSecondaryNextAction, value))
            {
                ExecuteSecondaryNextActionCommand.RaiseCanExecuteChanged();
                ExecuteSelectedSecondaryNextActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSecondaryNextActions => SecondaryNextActionCards.Count > 0;

    public string? StudySelectedSquare => studySelectedSquare;

    public string? StudyPreviewTargetSquare
    {
        get => studyPreviewTargetSquare;
        private set => SetProperty(ref studyPreviewTargetSquare, value);
    }

    public IReadOnlyList<string> StudyAvailableMoveSquares => studyAvailableTargets.ToList();

    public string StudyBoardHint => CurrentPosition is null
        ? "Start practice to use the board."
        : studySelectedSquare is null
            ? "Select a piece, then choose a highlighted square."
            : $"Selected {studySelectedSquare}. Click a highlighted target square to play the move.";

    public string StudyInputModeText => CurrentPosition is null
        ? "Board input is idle."
        : CurrentPosition.AnswerKind == OpeningTrainingAnswerKind.Move
            ? "Accepted: main repertoire move and sound theory alternatives."
            : "Choose the answer option that best explains the position.";

    public string StudyTaskTitle => CurrentPosition is null
        ? "Find the repertoire move"
        : CurrentPosition.AnswerKind == OpeningTrainingAnswerKind.Move
            ? $"Find {CurrentPosition.SideToMove}'s repertoire move"
            : "Choose the best plan";

    public string StudyTaskSubtitle => guidedSession is null
        ? "Position 0 of 0"
        : $"Position {Math.Min(currentStepIndex + 1, guidedSession.Positions.Count)} of {guidedSession.Positions.Count} - {SelectedOpeningName}";

    public string StudyBoardStatusText => CurrentPosition is null
        ? "No position loaded"
        : AppendCurrentPositionPracticeStatus($"{CurrentPosition.SideToMove} to move - {StudyProgressText} - {CurrentHintLevel}");

    public string StudySideToMoveText => CurrentPosition is null
        ? "No position loaded"
        : $"{CurrentPosition.SideToMove} to move";

    public string StudyMovePromptText => CurrentPosition is null
        ? "Start practice to see the move prompt."
        : CurrentPosition.AnswerKind == OpeningTrainingAnswerKind.Move
            ? $"{CurrentPosition.SideToMove} to move. Find the prepared move from your repertoire."
            : "Choose the answer that matches the plan for this position.";

    public string StudyFeedbackSummaryText => string.IsNullOrWhiteSpace(ResultText)
        ? "No move submitted yet."
        : ResultText;

    public string CurrentPositionGoalText => CurrentPosition is null
        ? "Start practice to see the current goal."
        : !string.IsNullOrWhiteSpace(CurrentPosition.ThemeLabel)
            ? $"Train the {CurrentPosition.ThemeLabel.ToLowerInvariant()} idea in this position."
            : CurrentPosition.AnswerKind == OpeningTrainingAnswerKind.Move
                ? "Train the next prepared move from memory."
                : "Train the idea behind this position before moving on.";

    public string CurrentMoveTrainingPurposeText => CurrentPosition is null
        ? "The trainer will show what this move is meant to build."
        : CurrentPosition.CandidateMoves.FirstOrDefault(move => move.IsPreferred)?.Idea?.ShortExplanation
            ?? CurrentPosition.CandidateMoves.FirstOrDefault(move => move.IsPreferred)?.Note
            ?? CurrentPosition.BetterMoveReason
            ?? CurrentPosition.Instruction;

    public string CurrentAttemptHistoryText => currentSessionAttempts.Count == 0
        ? "No moves submitted yet in this run."
        : $"This run: {currentSessionAttempts.Count} submitted, {correctAnswers} clear, {playableAnswers} accepted alternative(s), {wrongAttempts} to revisit.";

    public string SessionCorrectCountText => $"{correctAnswers} clear";

    public string SessionAcceptedAlternativesText => $"{playableAnswers} accepted alternative(s)";

    public string SessionNeedsReviewText => $"{wrongAttempts} to revisit";

    private string AppendCurrentPositionPracticeStatus(string status)
    {
        string? positionId = CurrentPosition?.PositionId;
        if (string.IsNullOrWhiteSpace(positionId))
        {
            return status;
        }

        int attempts = currentSessionAttempts.Count(attempt =>
            string.Equals(attempt.PositionId, positionId, StringComparison.Ordinal));
        if (attempts == 0)
        {
            return status;
        }

        bool needsPractice = currentSessionAttempts.Any(attempt =>
            string.Equals(attempt.PositionId, positionId, StringComparison.Ordinal)
            && attempt.Score == OpeningTrainingScore.Wrong);
        string tryText = attempts == 1 ? "1 try" : $"{attempts} tries";
        string practiceText = needsPractice ? "to revisit" : "on track";
        return $"{status} - {tryText} - {practiceText}";
    }

    public string StageTitle => currentPageIndex switch
    {
        SelectionPageIndex => "Choose Today's Training",
        OverviewPageIndex => "Understand The Idea",
        StudyPageIndex => "Practice From Memory",
        ResultsPageIndex => "Review And Continue",
        _ => "Opening Trainer"
    };

    public string StageDescription => currentPageIndex switch
    {
        SelectionPageIndex => "Start with the recommendation for today, then use Advanced Options only when you want a specific line.",
        OverviewPageIndex => "See the idea, common replies, and the focus before you practice from memory.",
        StudyPageIndex => "Recall the move first, then use hints only when you need a nudge.",
        ResultsPageIndex => "See what is stable, what needs review, and the next best action.",
        _ => string.Empty
    };

    public string StageProgressLabel => $"Stage {currentPageIndex + 1} of {TotalPages} - {StageTitle}";

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

    public string OpeningContextStartsFrom => overview is null || overview.MainLine.Count == 0
        ? "Starts from: choose an opening to see the first moves."
        : $"Starts from: {FormatMainLine(overview.MainLine, 4)}";

    public string OpeningContextPlan
    {
        get
        {
            if (overview is null)
            {
                return "Plan: load an opening to see the strategic map before practice.";
            }

            string? idea = overview.WhyTheseMovesMatter
                .Select(moveIdea => moveIdea.ShortExplanation)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            idea ??= overview.MainLine
                .Select(move => move.Idea?.ShortExplanation)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            return string.IsNullOrWhiteSpace(idea)
                ? "Plan: build the center, finish development, and avoid early tactical overreach."
                : $"Plan: {idea}";
        }
    }

    public string CoverageHumanText
    {
        get
        {
            if (overview is null)
            {
                return "Choose an opening to see what is ready and what needs another pass.";
            }

            int needsReview = overview.Coverage.WeakBranches;
            if (PlayerOpeningPlan?.Progress.SessionCount > 0)
            {
                return needsReview == 0
                    ? "You have practice history in this repertoire. This opening is in good shape for today."
                    : $"You have practice history in this repertoire. {needsReview} total review position{PluralSuffix(needsReview)} are worth revisiting today.";
            }

            if (overview.Coverage.CoveragePercent <= 0.1)
            {
                return $"You are starting fresh in this opening. {needsReview} total review position{PluralSuffix(needsReview)} are ready for practice today.";
            }

            return needsReview == 0
                ? "This opening is in good shape. Today can stay light and retention-focused."
                : $"{needsReview} total review position{PluralSuffix(needsReview)} are worth revisiting before adding new material.";
        }
    }

    public string CoverageMetricText => overview is null
        ? "No coverage metrics loaded yet."
        : $"Today's set: {overview.Coverage.CoveredBranches}/{overview.Coverage.TotalBookBranches} practiced | {overview.Coverage.WeakBranches} to revisit";

    public string PracticeFocusText => overview is null
        ? "Practice focus appears after an opening loads."
        : SelectedIntensityChoice?.Id switch
        {
            "safe" => "Focus: main line review only. Weak branches are listed below as optional repairs.",
            "challenge" => $"Focus: main line from {FormatMainLine(overview.MainLine, 4)}, plus less familiar opponent replies.",
            _ => overview.Coverage.WeakBranches > 0
                ? $"Focus: {overview.Coverage.WeakBranches} review position{PluralSuffix(overview.Coverage.WeakBranches)} from your {FormatMainLine(overview.MainLine, 4)} line. The full line contains {GetReviewMoveCount()} moves, but today's practice stays focused."
                : $"Focus: main line from {FormatMainLine(overview.MainLine, 4)}, plus the most useful opponent replies."
        };

    public string MainLineText => overview is null || overview.MainLine.Count == 0
        ? "No main line loaded yet."
        : FormatMainLine(overview.MainLine, Math.Min(12, overview.MainLine.Count));

    public string RememberThisText
    {
        get
        {
            if (overview is null || overview.MainLine.Count == 0)
            {
                return "Remember: load an opening to see the plan before practicing the moves.";
            }

            string line = FormatMainLine(overview.MainLine, Math.Min(6, overview.MainLine.Count));
            string idea = overview.WhyTheseMovesMatter
                .Select(moveIdea => moveIdea.ShortExplanation)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?? OpeningContextPlan.Replace("Plan: ", string.Empty, StringComparison.OrdinalIgnoreCase);
            return $"Remember: {idea} Start from {line}, then follow the plan instead of memorizing isolated moves.";
        }
    }

    public string WeakPositionsPlaceholder => WeakPositionItems.Count == 0
        ? "No weak positions are saved for this opening yet."
        : string.Empty;

    public bool ShowWeakPositionsPlaceholder => WeakPositionItems.Count == 0;

    public double StudyProgressPercent => guidedSession is null || guidedSession.Positions.Count == 0
        ? 0
        : Math.Round((double)Math.Min(currentStepIndex + 1, guidedSession.Positions.Count) / guidedSession.Positions.Count * 100d, 1);

    public string StudyProgressText => guidedSession is null
        ? "Practice has not started."
        : $"Position {Math.Min(currentStepIndex + 1, guidedSession.Positions.Count)}/{guidedSession.Positions.Count}";

    public string ResultsSummaryText => guidedSession is null
        ? "No session data yet."
        : $"{completedSteps}/{guidedSession.Positions.Count} positions, {correctAnswers} clear, {playableAnswers} accepted alternative(s), {wrongAttempts} to revisit, {hintUseCount} hint(s) used.";

    public string TranspositionSummaryText => transposedAnswers == 0
        ? "No transposition shortcuts appeared in this run."
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
            ReplaceItems(UnderstandingCards, []);
            SelectedPriority = null;
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "No openings matched the current filter.";
            OpponentSummary = "Opponent replies are unavailable.";
            CoverageText = "No practice history yet.";
            CoverageExplanation = "Try another filter or repertoire side.";
            OnPropertyChanged(nameof(MainLineText));
            OnPropertyChanged(nameof(RememberThisText));
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
            Dictionary<string, string> properties = BuildRecommendationTelemetryProperties(TodayRecommendation);
            workspaceService.TrackTelemetry(
                OpeningTrainingTelemetryEvents.OpeningDailyLessonShown,
                PlayerKey,
                TodayRecommendation.OpeningLine,
                recommendationId: TodayRecommendation.OpeningLine.LineKey.Value,
                properties: properties);
            workspaceService.TrackTelemetry(
                OpeningTrainingTelemetryEvents.OpeningRecommendationShown,
                PlayerKey,
                TodayRecommendation.OpeningLine,
                recommendationId: TodayRecommendation.OpeningLine.LineKey.Value,
                properties: properties);
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
        OnPropertyChanged(nameof(TodayTrainingReasonLabel));
        OnPropertyChanged(nameof(TodayLessonButtonText));
        OnPropertyChanged(nameof(HasTodayLesson));
        OnPropertyChanged(nameof(PlayerOpeningPlanTitle));
        OnPropertyChanged(nameof(PlayerOpeningPlanSummary));
        OnPropertyChanged(nameof(PlayerOpeningProgressText));
        OnPropertyChanged(nameof(PlayerOpeningProgressInterpretation));
            OnPropertyChanged(nameof(CoverageHumanText));
            OnPropertyChanged(nameof(MainLineText));
            OnPropertyChanged(nameof(RememberThisText));
        OnPropertyChanged(nameof(SelectedSpecialModeDescription));
        OnPropertyChanged(nameof(SelectedSpecialModeButtonText));
        StartRecommendedStudyCommand.RaiseCanExecuteChanged();
        StartRecommendedPracticeNowCommand.RaiseCanExecuteChanged();
        StartSpecialModeCommand.RaiseCanExecuteChanged();
    }

    private void StartRecommendedStudy()
    {
        if (!PrepareTodayRecommendationForStudy())
        {
            return;
        }

        overviewOpenedFromTodayRecommendation = true;
        SetPage(OverviewPageIndex);
    }

    private void StartRecommendedPracticeNow()
    {
        if (!PrepareTodayRecommendationForStudy())
        {
            return;
        }

        overviewOpenedFromTodayRecommendation = false;
        StartGuidedStudy(null, "today_recommendation", TodayRecommendation!.OpeningLine.LineKey.Value);
    }

    private bool PrepareTodayRecommendationForStudy()
    {
        if (TodayRecommendation is null)
        {
            return false;
        }

        SelectedOpening = TodayRecommendation.OpeningLine;
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.OpeningDailyLessonStarted,
            PlayerKey,
            SelectedOpening,
            recommendationId: TodayRecommendation.OpeningLine.LineKey.Value,
            properties: BuildRecommendationTelemetryProperties(TodayRecommendation));
        return true;
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
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["action"] = SelectedPriority.Action.ToString(),
                ["reason_code"] = SelectedPriority.ReasonCode.ToString(),
                ["recommendation_type"] = "overview_priority"
            }));
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
            ReplaceItems(UnderstandingCards, []);
            SelectedPriority = null;
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "Could not load opening overview.";
            OpponentSummary = "Opponent replies are unavailable.";
            CoverageText = "No practice history yet.";
            CoverageExplanation = "The selected opening does not have enough local theory data yet.";
            RaiseSelectionCoachingTextChanged();
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
        OpponentSummary = FormatOpponentSummary(overview.OpponentReplyProfile.Summary);
        CoverageText = $"Your saved progress: {overview.Coverage.CoveredBranches}/{overview.Coverage.TotalBookBranches}";
        CoverageExplanation = overview.Coverage.CoveragePercent <= 0.1
            ? "You do not have saved review progress for this opening yet, so coverage starts at zero."
            : $"Stable branches: {overview.Coverage.StableBranches}. Unseen common branches: {overview.Coverage.UnseenCommonBranches}.";
        ReplaceItems(MainLineItems, overview.MainLine.Select(FormatMoveLabel).ToList());
        ReplaceItems(BranchItems, overview.CommonBranches.Select(branch =>
            $"{branch.OpponentMove} - {FormatBranchFrequencyLabel(branch, overview.CommonBranches)}").ToList());
        ReplaceItems(PriorityItems, overview.Priorities);
        ReplaceItems(UnderstandingCards, workspaceService.BuildUnderstandingCards(overview, SelectedOpening));
        SelectedPriority = PriorityItems.FirstOrDefault();
        ReplaceItems(WeakPositionItems, overview.WeakPositions.Select(position =>
            $"{position.OpeningName} | {position.Instruction}").ToList());
        ResultText = string.Empty;
        RaiseSelectionCoachingTextChanged();
        OnPropertyChanged(nameof(MainLineText));
        OnPropertyChanged(nameof(RememberThisText));
        OnPropertyChanged(nameof(WeakPositionsPlaceholder));
        OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
        RaisePriorityStateChanged();
    }

    private void StartGuidedStudy()
    {
        if (overviewOpenedFromTodayRecommendation && TodayRecommendation is not null)
        {
            overviewOpenedFromTodayRecommendation = false;
            StartGuidedStudy(null, "today_recommendation", TodayRecommendation.OpeningLine.LineKey.Value);
            return;
        }

        StartGuidedStudy(null, "manual", null);
    }

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
        studyStartedUtc = workspaceService.UtcNow;
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
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["start_source"] = startSource,
                ["position_count"] = guidedSession.Positions.Count.ToString(),
                ["target_fallback"] = (target is not null && !guidedSession.Positions.Any(position => IsTargetedPosition(position, target))).ToString().ToLowerInvariant(),
                ["recommendation_type"] = BuildRecommendationType(startSource, specialMode),
                ["reason_code"] = TodayRecommendation is not null && string.Equals(recommendationId, TodayRecommendation.OpeningLine.LineKey.Value, StringComparison.Ordinal)
                    ? TodayRecommendation.ReasonCode.ToString()
                    : "unknown"
            }));
        if (specialMode is not null)
        {
            workspaceService.TrackTelemetry(
                OpeningTrainingTelemetryEvents.SpecialModeStarted,
                PlayerKey,
                SelectedOpening,
                guidedSession,
                specialMode: specialMode.Kind,
                properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
                {
                    ["time_limit_minutes"] = specialMode.TimeLimitMinutes.ToString(),
                    ["max_positions"] = specialMode.MaxPositions.ToString()
                }));
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

        firstMoveUtc ??= workspaceService.UtcNow;
        string submittedAnswer = position.AnswerKind == OpeningTrainingAnswerKind.Move
            ? MoveInput
            : SelectedAnswerOption?.Id ?? string.Empty;
        OpeningTrainingAttemptResult result = workspaceService.Evaluate(position, submittedAnswer);
        currentSessionAttempts.Add(result);
        OpeningTrainingMoveOption? mainMove = position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)
            ?? position.CandidateMoves.FirstOrDefault();
        ResultText = result.Score switch
        {
            OpeningTrainingScore.Correct => "Good. This keeps your repertoire plan on track.",
            OpeningTrainingScore.Wrong => BuildWrongMoveFeedback(position, mainMove),
            _ when IsSubmittedMainRepertoireMove(result, mainMove) => "Good. This is the prepared repertoire move.",
            _ => mainMove is not null
                ? $"Accepted, but the main repertoire move is {mainMove.DisplayText}."
                : "Accepted. This is a useful theory alternative."
        };
        TriggerStudyFeedback(result);
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
            RaiseStudyNavigationStateChanged();
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

    private static bool IsSubmittedMainRepertoireMove(
        OpeningTrainingAttemptResult result,
        OpeningTrainingMoveOption? mainMove)
    {
        if (mainMove is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(mainMove.Uci)
            && !string.IsNullOrWhiteSpace(result.ResolvedUci)
            && string.Equals(mainMove.Uci, result.ResolvedUci, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.ResolvedSan)
            && string.Equals(
                SanNotation.NormalizeSan(mainMove.DisplayText),
                SanNotation.NormalizeSan(result.ResolvedSan),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(result.SubmittedMoveText)
            && string.Equals(
                SanNotation.NormalizeSan(mainMove.DisplayText),
                SanNotation.NormalizeSan(result.SubmittedMoveText),
                StringComparison.OrdinalIgnoreCase);
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

    private void TriggerStudyFeedback(OpeningTrainingAttemptResult result)
    {
        _ = studyFeedbackAnimator.AnimateAsync(result, ApplyStudyFeedbackFrame);
    }

    private void ApplyStudyFeedbackFrame(OpeningStudyFeedbackFrame frame)
    {
        StudyFeedbackText = frame.Text;
        StudyFeedbackBrush = frame.Brush;
        StudyFeedbackBorderBrush = frame.BorderBrush;
        StudyFeedbackOpacity = frame.Opacity;
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
            CurrentPrompt = "Practice is idle.";
            CurrentWhy = string.Empty;
            PreviewArrows = [];
            ReplaceItems(AnswerOptionItems, []);
            SelectedAnswerOption = null;
            ResetCurrentHint();
            ResetStudyReference();
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
        ResetStudyReference();
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

        ResultHeadline = OpeningTrainerResultPresentation.BuildCompletionHeadline(
            guidedSession?.Positions.Count,
            SelectedOpeningName);
        ResultRecommendation = OpeningTrainerResultPresentation.BuildCompletionRecommendation(
            wrongAttempts,
            playableAnswers,
            transposedAnswers);
        ReplaceItems(NextActionItems, workspaceService.BuildNextActions(OutcomeSummary));
        RebuildNextActionCards();
        LearningPlan = workspaceService.BuildLearningPlan(OutcomeSummary, currentSessionAttempts, NextActionItems.ToList());
        ReplaceItems(LearningPlanReviewItems, LearningPlan.ReviewItems);
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
        RaiseLearningPlanStateChanged();
        RaiseNextActionStateChanged();
        Dictionary<string, string> completionProperties = BuildBaseTelemetryProperties(new Dictionary<string, string>
        {
            ["start_source"] = currentStartSource ?? "unknown",
            ["completed_steps"] = completedSteps.ToString(),
            ["wrong_attempts"] = wrongAttempts.ToString(),
            ["hint_count"] = hintUseCount.ToString(),
            ["not_known_count"] = CountDontKnowAttempts().ToString(),
            ["time_to_first_move_seconds"] = GetTimeToFirstMoveSeconds().ToString()
        });
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedSessionCompleted,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: completionProperties);
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.OpeningLearningPlanShown,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: completionProperties);
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
        ResultHeadline = "Practice in progress.";
        ResultRecommendation = "Finish the run to get your next review step.";
        LearningPlan = null;
        ReplaceItems(ResultItems, []);
        ReplaceItems(LearningPlanReviewItems, []);
        ReplaceItems(NextActionItems, []);
        RebuildNextActionCards();
        SelectedNextAction = null;
        OutcomeSummary = null;
        ResetCurrentHint();
        RaiseNextActionStateChanged();
        RaiseLearningPlanStateChanged();
        RaiseResultsStateChanged();
    }

    private void AddResultLine(OpeningTrainingAttemptResult result)
    {
        string label = result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
            ? "Transposed"
            : result.Score.ToString();
        ResultItems.Insert(0, $"{label} | {result.SubmittedMoveText} | {result.ShortExplanation}");
        OnPropertyChanged(nameof(HasAdvancedResultDetails));
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
        OnPropertyChanged(nameof(StageProgressLabel));
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
        StartRecommendedPracticeNowCommand.RaiseCanExecuteChanged();
        StartGuidedStudyCommand.RaiseCanExecuteChanged();
        StartPriorityStudyCommand.RaiseCanExecuteChanged();
        StartSpecialModeCommand.RaiseCanExecuteChanged();
        RestartStudyCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedOpening));
        OnPropertyChanged(nameof(SelectedOpeningName));
        OnPropertyChanged(nameof(SelectedOpeningSideText));
        RaiseSelectionCoachingTextChanged();
    }

    private void RaiseStudyNavigationStateChanged()
    {
        ShowHintCommand.RaiseCanExecuteChanged();
        DontKnowCommand.RaiseCanExecuteChanged();
        RevealStudyReferenceCommand.RaiseCanExecuteChanged();
        EvaluateMoveCommand.RaiseCanExecuteChanged();
        NextStepCommand.RaiseCanExecuteChanged();
        PreviousStepCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsStudyReferenceVisible));
        OnPropertyChanged(nameof(CanRevealStudyReference));
        OnPropertyChanged(nameof(StudyReferenceButtonText));
        OnPropertyChanged(nameof(IsStudyReferenceHidden));
        OnPropertyChanged(nameof(StudyReferencePromptText));
        OnPropertyChanged(nameof(DontKnowButtonText));
        OnPropertyChanged(nameof(CanUseDontKnow));
        OnPropertyChanged(nameof(StudyProgressPercent));
        OnPropertyChanged(nameof(StudyProgressText));
        OnPropertyChanged(nameof(StudyTaskTitle));
        OnPropertyChanged(nameof(StudyTaskSubtitle));
        OnPropertyChanged(nameof(StudyBoardStatusText));
        OnPropertyChanged(nameof(StudySideToMoveText));
        OnPropertyChanged(nameof(StudyMovePromptText));
        OnPropertyChanged(nameof(StudyFeedbackSummaryText));
        OnPropertyChanged(nameof(StudySelectedSquare));
        OnPropertyChanged(nameof(StudyAvailableMoveSquares));
        OnPropertyChanged(nameof(StudyBoardHint));
        OnPropertyChanged(nameof(StudyInputModeText));
        OnPropertyChanged(nameof(CurrentPositionGoalText));
        OnPropertyChanged(nameof(CurrentAttemptHistoryText));
        OnPropertyChanged(nameof(CurrentMoveTrainingPurposeText));
        OnPropertyChanged(nameof(SessionCorrectCountText));
        OnPropertyChanged(nameof(SessionAcceptedAlternativesText));
        OnPropertyChanged(nameof(SessionNeedsReviewText));
        OnPropertyChanged(nameof(CurrentHintText));
        OnPropertyChanged(nameof(CurrentHintLevel));
        OnPropertyChanged(nameof(HasAnswerOptions));
    }

    private void RaiseResultsStateChanged()
    {
        OnPropertyChanged(nameof(ResultsSummaryText));
        OnPropertyChanged(nameof(TranspositionSummaryText));
        OnPropertyChanged(nameof(ResultsBiggestWeaknessText));
        OnPropertyChanged(nameof(ResultCelebrationTitle));
        OnPropertyChanged(nameof(ResultCelebrationText));
        OnPropertyChanged(nameof(ResultOutcomeBadge));
        OnPropertyChanged(nameof(ResultNextStepSummary));
        OnPropertyChanged(nameof(ResultsCompletedMetricText));
        OnPropertyChanged(nameof(ResultsClearMetricText));
        OnPropertyChanged(nameof(ResultsAlternativesMetricText));
        OnPropertyChanged(nameof(ResultsHintsMetricText));
        OnPropertyChanged(nameof(ResultsRevisitMetricText));
        OnPropertyChanged(nameof(ResultsNextBestActionText));
        OnPropertyChanged(nameof(ResultsNextActionReasonText));
        OnPropertyChanged(nameof(HasAdvancedResultDetails));
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

    private void ExecutePrimaryNextAction()
    {
        if (PrimaryNextAction is null)
        {
            return;
        }

        ExecuteNextAction(PrimaryNextAction.Action);
    }

    private void ExecuteSecondaryNextAction(TrainingNextActionCardViewModel? nextAction)
    {
        if (nextAction is null)
        {
            return;
        }

        ExecuteNextAction(nextAction.Action);
    }

    private void ExecuteSelectedSecondaryNextAction()
    {
        if (SelectedSecondaryNextAction is null)
        {
            return;
        }

        ExecuteNextAction(SelectedSecondaryNextAction.Action);
    }

    private void ExecuteSelectedNextAction()
    {
        if (SelectedNextAction is null)
        {
            return;
        }

        ExecuteNextAction(SelectedNextAction);
    }

    private void ExecuteNextAction(TrainingNextAction action)
    {
        completedNextActionIds.Add(action.Id);
        if (scheduledActionIdsBySource.TryGetValue(action.Id, out string? scheduledActionId)
            && action.Kind == TrainingNextActionKind.RepeatNow)
        {
            workspaceService.MarkScheduledActionCompleted(PlayerKey, scheduledActionId);
        }

        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.ResultsNextActionClicked,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["next_action_id"] = action.Id,
                ["next_action_kind"] = action.Kind.ToString(),
                ["delay_minutes"] = action.DelayMinutes.ToString()
            }));

        switch (action.Kind)
        {
            case TrainingNextActionKind.RepeatNow:
            case TrainingNextActionKind.PracticeMainLineOnly:
            case TrainingNextActionKind.ReviewWithHintsAllowed:
                RestartStudy();
                break;
            case TrainingNextActionKind.RepeatAfterBreak:
                ResultText = action.DelayMinutes > 0
                    ? $"Scheduled. Review this line in about {action.DelayMinutes} min. Starting another recommended opening now."
                    : "Scheduled for a later review.";
                StartAnotherRecommendedOpening();
                break;
            case TrainingNextActionKind.RepairWeakBranches:
                SetPage(OverviewPageIndex);
                break;
            case TrainingNextActionKind.BrowseAnotherOpening:
                if (string.Equals(action.Id, "train-another-opening", StringComparison.OrdinalIgnoreCase))
                {
                    StartAnotherRecommendedOpening();
                }
                else
                {
                    SetPage(SelectionPageIndex);
                }
                break;
            case TrainingNextActionKind.ReturnTomorrow:
            case TrainingNextActionKind.StopForNow:
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

    private void StartAnotherRecommendedOpening()
    {
        OpeningLineCatalogItem? currentLine = SelectedOpening;
        RefreshTodayRecommendation();

        OpeningLineCatalogItem? nextLine = FindAnotherRecommendedOpening(currentLine);
        if (nextLine is null)
        {
            SetPage(SelectionPageIndex);
            return;
        }

        SelectedOpening = nextLine;
        LoadOverview();
        StartGuidedStudy(null, "next_recommended_opening", nextLine.LineKey.Value);
    }

    private OpeningLineCatalogItem? FindAnotherRecommendedOpening(OpeningLineCatalogItem? currentLine)
    {
        IEnumerable<string> recommendedEco = (PlayerOpeningPlan?.Today ?? [])
            .Concat(PlayerOpeningPlan?.ThisWeek ?? [])
            .Concat(PlayerOpeningPlan?.LongTermGaps ?? [])
            .Select(item => item.Eco)
            .Where(eco => !string.IsNullOrWhiteSpace(eco))
            .Cast<string>();

        foreach (string eco in recommendedEco)
        {
            OpeningLineCatalogItem? match = OpeningItems.FirstOrDefault(item =>
                !IsSameOpeningLine(item, currentLine)
                && !string.Equals(item.Eco, currentLine?.Eco, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Eco, eco, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return OpeningItems.FirstOrDefault(item => !IsSameOpeningLine(item, currentLine));
    }

    private static bool IsSameOpeningLine(OpeningLineCatalogItem? left, OpeningLineCatalogItem? right)
        => left is not null && right is not null && left.LineKey.Equals(right.LineKey);

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
        DateTime abandonedUtc = workspaceService.UtcNow;
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
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["start_source"] = currentStartSource ?? "unknown",
                ["completed_steps"] = completedSteps.ToString(),
                ["position_count"] = guidedSession.Positions.Count.ToString(),
                ["time_to_first_move_seconds"] = GetTimeToFirstMoveSeconds().ToString()
            }));
    }

    private void RaiseNextActionStateChanged()
    {
        OnPropertyChanged(nameof(HasNextActions));
        OnPropertyChanged(nameof(NextActionsPlaceholder));
        OnPropertyChanged(nameof(SelectedNextActionButtonText));
        OnPropertyChanged(nameof(PrimaryNextAction));
        OnPropertyChanged(nameof(HasPrimaryNextAction));
        OnPropertyChanged(nameof(HasSecondaryNextActions));
        OnPropertyChanged(nameof(ResultNextStepSummary));
        OnPropertyChanged(nameof(ResultsNextBestActionText));
        OnPropertyChanged(nameof(ResultsNextActionReasonText));
        ExecuteNextActionCommand.RaiseCanExecuteChanged();
        ExecutePrimaryNextActionCommand.RaiseCanExecuteChanged();
        ExecuteSecondaryNextActionCommand.RaiseCanExecuteChanged();
        ExecuteSelectedSecondaryNextActionCommand.RaiseCanExecuteChanged();
    }

    private void RebuildNextActionCards()
    {
        IReadOnlyList<TrainingNextActionCardViewModel> cards = NextActionItems
            .Select((action, index) => TrainingNextActionCardViewModel.Create(action, index == 0))
            .ToList();
        ReplaceItems(NextActionCards, cards);
        ReplaceItems(SecondaryNextActionCards, cards.Skip(1).ToList());
        SelectedNextAction = NextActionItems.FirstOrDefault();
        SelectedSecondaryNextAction = SecondaryNextActionCards.FirstOrDefault();
        RaiseNextActionStateChanged();
    }

    private void RaiseLearningPlanStateChanged()
    {
        OnPropertyChanged(nameof(LearningPlan));
        OnPropertyChanged(nameof(LearningPlanMasteredText));
        OnPropertyChanged(nameof(LearningPlanRepeatText));
        OnPropertyChanged(nameof(LearningPlanNextReviewText));
        OnPropertyChanged(nameof(LearningPlanReasonText));
        OnPropertyChanged(nameof(ResultsNextActionReasonText));
        OnPropertyChanged(nameof(HasLearningPlanReviewItems));
        OnPropertyChanged(nameof(LearningPlanReviewPlaceholder));
        OnPropertyChanged(nameof(HasAdvancedResultDetails));
    }

    private void UseDontKnow()
    {
        OpeningTrainingPosition? position = CurrentPosition;
        if (position is null || HasDontKnowAttemptForCurrentPosition())
        {
            return;
        }

        firstMoveUtc ??= workspaceService.UtcNow;
        OpeningTrainingAttemptResult result = BuildDontKnowAttempt(position);
        currentSessionAttempts.Add(result);
        wrongAttempts++;
        ResultText = "Marked to revisit: use the hint, then play the prepared move on the board.";
        AddResultLine(result);
        ShowNextHint(trackAsDontKnow: true);
        UnlockStudyReference();
        ClearStudySelection();
        RaiseResultsStateChanged();
        RaiseStudyNavigationStateChanged();

        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedDontKnowUsed,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["position_id"] = position.PositionId,
                ["step_index"] = currentStepIndex.ToString(),
                ["hint_count"] = hintUseCount.ToString(),
                ["not_known_count"] = CountDontKnowAttempts().ToString()
            }));
    }

    private static OpeningTrainingAttemptResult BuildDontKnowAttempt(OpeningTrainingPosition position)
    {
        IReadOnlyList<OpeningTrainingMoveOption> preferredReferences = position.CandidateMoves
            .Where(option => option.IsPreferred)
            .ToList();

        if (preferredReferences.Count == 0)
        {
            preferredReferences = position.CandidateMoves.Take(1).ToList();
        }

        return new OpeningTrainingAttemptResult(
            position.PositionId,
            position.Mode,
            position.SourceKind,
            OpeningTrainingAttemptStatus.Normal,
            "I do not know",
            null,
            null,
            position.CandidateMoves,
            OpeningTrainingScore.Wrong,
            "Marked for review because you chose to see help before answering.",
            [],
            preferredReferences,
            position.CandidateMoves.Where(option => !preferredReferences.Contains(option)).ToList(),
            null,
            preferredReferences.FirstOrDefault()?.Idea,
            "Use the first hint, then make the prepared move yourself.",
            TrainingCoachHintLevel.Plan,
            TrainingMistakeCategory.Unknown,
            true);
    }

    private void RevealStudyReference()
    {
        if (!CanRevealStudyReference)
        {
            return;
        }

        IsStudyReferenceVisible = true;
        workspaceService.TrackTelemetry(
            OpeningTrainingTelemetryEvents.GuidedReferenceRevealed,
            PlayerKey,
            SelectedOpening,
            guidedSession,
            recommendationId: currentRecommendationId,
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["position_id"] = CurrentPosition?.PositionId ?? string.Empty,
                ["step_index"] = currentStepIndex.ToString(),
                ["reference_revealed_before_attempt"] = currentSessionAttempts.All(attempt => attempt.PositionId != CurrentPosition?.PositionId).ToString().ToLowerInvariant(),
                ["hint_count"] = hintUseCount.ToString(),
                ["not_known_count"] = CountDontKnowAttempts().ToString()
            }));
        RaiseStudyNavigationStateChanged();
    }

    private void UnlockStudyReference()
    {
        CanRevealStudyReference = true;
    }

    private void ResetStudyReference()
    {
        IsStudyReferenceVisible = false;
        CanRevealStudyReference = false;
    }

    private void ShowNextHint()
        => ShowNextHint(trackAsDontKnow: false);

    private void ShowNextHint(bool trackAsDontKnow)
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
            RaiseStudyNavigationStateChanged();
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
            properties: BuildBaseTelemetryProperties(new Dictionary<string, string>
            {
                ["hint_level"] = hint.Level.ToString(),
                ["position_id"] = position.PositionId,
                ["source"] = trackAsDontKnow ? "dont_know" : "hint_button",
                ["hint_count"] = hintUseCount.ToString()
            }));
        RaiseResultsStateChanged();
        RaiseStudyNavigationStateChanged();
    }

    private bool HasDontKnowAttemptForCurrentPosition()
    {
        string? positionId = CurrentPosition?.PositionId;
        return !string.IsNullOrWhiteSpace(positionId)
            && currentSessionAttempts.Any(attempt =>
                string.Equals(attempt.PositionId, positionId, StringComparison.Ordinal)
                && string.Equals(attempt.SubmittedMoveText, "I do not know", StringComparison.OrdinalIgnoreCase));
    }

    private int CountDontKnowAttempts()
        => currentSessionAttempts.Count(attempt =>
            string.Equals(attempt.SubmittedMoveText, "I do not know", StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, string> BuildRecommendationTelemetryProperties(TrainingRecommendationCard recommendation)
        => telemetryAdapter.BuildRecommendationProperties(
            recommendation,
            SelectedProfileChoice,
            SelectedSide,
            AdvancedPlayerKey);

    private Dictionary<string, string> BuildBaseTelemetryProperties(Dictionary<string, string>? properties = null)
    {
        return telemetryAdapter.BuildBaseProperties(
            SelectedProfileChoice,
            SelectedSide,
            AdvancedPlayerKey,
            properties);
    }

    private static string BuildRecommendationType(string startSource, SpecialTrainingModeDefinition? specialMode)
    {
        if (specialMode is not null)
        {
            return $"special_mode:{specialMode.Kind}";
        }

        return startSource switch
        {
            "today_recommendation" => "daily_lesson",
            "overview_priority" => "overview_priority",
            "manual" => "manual",
            _ => startSource
        };
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

    private void RaiseTodayRecommendationStateChanged()
    {
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
        OnPropertyChanged(nameof(TodayDecisionSummary));
        OnPropertyChanged(nameof(TodayStartSequenceText));
        OnPropertyChanged(nameof(TodayLessonReasonDetail));
        OnPropertyChanged(nameof(TodayTrainingReasonLabel));
        OnPropertyChanged(nameof(TodayLessonButtonText));
        OnPropertyChanged(nameof(HasTodayLesson));
        StartRecommendedStudyCommand.RaiseCanExecuteChanged();
        StartRecommendedPracticeNowCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSelectionCoachingTextChanged()
    {
        OnPropertyChanged(nameof(OpeningContextStartsFrom));
        OnPropertyChanged(nameof(OpeningContextPlan));
        OnPropertyChanged(nameof(CoverageHumanText));
        OnPropertyChanged(nameof(CoverageMetricText));
        OnPropertyChanged(nameof(PracticeFocusText));
        OnPropertyChanged(nameof(TodayLessonReasonDetail));
        OnPropertyChanged(nameof(TodayDecisionSummary));
        OnPropertyChanged(nameof(TodayStartSequenceText));
    }

    private int GetRecommendedPositionCount()
    {
        if (overview is not null && TodayRecommendation is not null && Equals(SelectedOpening, TodayRecommendation.OpeningLine))
        {
            return Math.Max(1, overview.Coverage.WeakBranches > 0 ? overview.Coverage.WeakBranches : overview.MainLine.Count);
        }

        return TodayRecommendation is null
            ? 0
            : Math.Max(1, TodayRecommendation.OpeningLine.BookBranchCount);
    }

    private int GetReviewMoveCount()
    {
        string? targetEco = TodayRecommendation?.OpeningLine.Eco ?? SelectedOpening?.Eco;
        if (!string.IsNullOrWhiteSpace(targetEco))
        {
            PlayerOpeningPlanItem? matchingWeeklyItem = WeeklyPlanItems.FirstOrDefault(item =>
                string.Equals(item.Eco, targetEco, StringComparison.OrdinalIgnoreCase));
            int parsedCount = ExtractLeadingInt(matchingWeeklyItem?.Evidence);
            if (parsedCount > 0)
            {
                return parsedCount;
            }
        }

        if (overview is not null && overview.MainLine.Count > 0)
        {
            return overview.MainLine.Count;
        }

        return Math.Max(1, GetRecommendedPositionCount());
    }

    private static int ExtractLeadingInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        int value = 0;
        foreach (char character in text)
        {
            if (!char.IsDigit(character))
            {
                break;
            }

            value = value * 10 + character - '0';
        }

        return value;
    }

    private string GetEstimatedDurationText()
    {
        if (TodayRecommendation is null)
        {
            return "0 min";
        }

        if (SelectedIntensityChoice?.Id == "balanced" && (overview?.Coverage.WeakBranches ?? TodayRecommendation.OpeningLine.BookBranchCount) > 0)
        {
            int lowerBound = Math.Max(TodayRecommendation.EstimatedDurationMinutes, 8);
            int upperBound = Math.Max(lowerBound + 2, 10);
            return $"{lowerBound}-{upperBound} min";
        }

        return $"{TodayRecommendation.EstimatedDurationMinutes} min";
    }

    private static string FormatRepertoireSide(RepertoireSide side)
        => side switch
        {
            RepertoireSide.White => "White",
            RepertoireSide.Black => "Black",
            _ => "Both sides"
        };

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

public sealed record OpeningTrainingProfileChoice(
    string Id,
    string Title,
    string Description,
    RepertoireSide Side,
    string PlayerKey);

public sealed record OpeningTrainingIntensityChoice(
    string Id,
    string Title,
    string Description,
    OpeningTrainingStrictness Strictness);

public sealed record TrainingNextActionCardViewModel(
    TrainingNextAction Action,
    string Title,
    string Reason,
    string TimingText,
    string ButtonText,
    bool IsPrimary)
{
    public static TrainingNextActionCardViewModel Create(TrainingNextAction action, bool isPrimary)
    {
        string timingText = action.DelayMinutes switch
        {
            _ when !isPrimary && action.Kind == TrainingNextActionKind.RepairWeakBranches => "Optional",
            <= 0 => "Ready now",
            < 60 => $"{action.DelayMinutes} min",
            1440 => "Tomorrow",
            _ => $"{Math.Round(action.DelayMinutes / 60d, 1):0.#} hours"
        };

        string buttonText = action.Kind switch
        {
            TrainingNextActionKind.RepeatAfterBreak when action.DelayMinutes > 0 && isPrimary => "Train another opening",
            TrainingNextActionKind.RepeatAfterBreak when action.DelayMinutes > 0 => "Schedule repeat",
            TrainingNextActionKind.RepeatNow => "Repeat now",
            TrainingNextActionKind.ReturnTomorrow => "Back to selection",
            TrainingNextActionKind.RepairWeakBranches => "Open priorities",
            TrainingNextActionKind.PracticeMainLineOnly => "Practice main line only",
            TrainingNextActionKind.ReviewWithHintsAllowed => "Review with hints",
            TrainingNextActionKind.StopForNow => "Stop for now",
            TrainingNextActionKind.BrowseAnotherOpening when string.Equals(action.Id, "train-another-opening", StringComparison.OrdinalIgnoreCase) => "Train another opening",
            TrainingNextActionKind.BrowseAnotherOpening => "Browse openings",
            _ => action.CommandLabel
        };

        return new TrainingNextActionCardViewModel(
            action,
            action.Title,
            action.Description,
            timingText,
            buttonText,
            isPrimary);
    }
}
