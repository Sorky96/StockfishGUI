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
    private OpeningLineCatalogItem? selectedOpening;
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
    private string moveInput = string.Empty;
    private string resultText = string.Empty;
    private string resultHeadline = "Finish a guided study session to see results.";
    private string resultRecommendation = "Your next review recommendations will appear here.";

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
        StartGuidedStudyCommand = new RelayCommand(StartGuidedStudy, () => SelectedOpening is not null && overview is not null);
        EvaluateMoveCommand = new RelayCommand(EvaluateMove, () => CurrentPosition is not null && !string.IsNullOrWhiteSpace(MoveInput));
        NextStepCommand = new RelayCommand(MoveNext, () => guidedSession is not null && currentStepIndex < guidedSession.Positions.Count);
        PreviousStepCommand = new RelayCommand(MovePrevious, () => guidedSession is not null && currentStepIndex > 0);
        RestartStudyCommand = new RelayCommand(RestartStudy, () => SelectedOpening is not null && overview is not null);

        RefreshOpenings();
    }

    public ObservableCollection<OpeningLineCatalogItem> OpeningItems { get; } = [];

    public ObservableCollection<string> MainLineItems { get; } = [];

    public ObservableCollection<string> BranchItems { get; } = [];

    public ObservableCollection<string> WeakPositionItems { get; } = [];

    public ObservableCollection<string> ResultItems { get; } = [];

    public IReadOnlyList<RepertoireSide> AvailableSides { get; } = Enum.GetValues<RepertoireSide>();

    public IReadOnlyList<OpeningTrainingStrictness> AvailableStrictnessOptions { get; } = Enum.GetValues<OpeningTrainingStrictness>();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand GoToOverviewCommand { get; }

    public RelayCommand GoToSelectionCommand { get; }

    public RelayCommand StartGuidedStudyCommand { get; }

    public RelayCommand EvaluateMoveCommand { get; }

    public RelayCommand NextStepCommand { get; }

    public RelayCommand PreviousStepCommand { get; }

    public RelayCommand RestartStudyCommand { get; }

    public string FilterText
    {
        get => filterText;
        set => SetProperty(ref filterText, value);
    }

    public string PlayerKey
    {
        get => playerKey;
        set => SetProperty(ref playerKey, value);
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
        : "Board input is active. The trainer accepts the main book move and, depending on strictness, also good alternatives from theory.";

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
        SelectionPageIndex => "Pick the repertoire side, strictness, and opening family you want to train.",
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
        : $"Completed {completedSteps}/{guidedSession.Positions.Count} positions | Correct {correctAnswers} | Playable {playableAnswers} | Wrong attempts {wrongAttempts}";

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
        if (items.Count > 0)
        {
            SelectedOpening = items[0];
        }
        else
        {
            SelectedOpening = null;
            overview = null;
            ReplaceItems(MainLineItems, []);
            ReplaceItems(BranchItems, []);
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "No openings matched the current filter.";
            OpponentSummary = "Opponent prep data is unavailable.";
            CoverageText = "Coverage is unavailable.";
            CoverageExplanation = "Try another filter or repertoire side.";
            OnPropertyChanged(nameof(WeakPositionsPlaceholder));
            OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
        }
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
            ReplaceItems(WeakPositionItems, []);
            SummaryText = "Could not load opening overview.";
            OpponentSummary = "Opponent prep data is unavailable.";
            CoverageText = "Coverage is unavailable.";
            CoverageExplanation = "The selected opening does not have enough local theory data yet.";
            OnPropertyChanged(nameof(WeakPositionsPlaceholder));
            OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
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
        ReplaceItems(WeakPositionItems, overview.WeakPositions.Select(position =>
            $"{position.OpeningName} | {position.Instruction}").ToList());
        ResultText = string.Empty;
        OnPropertyChanged(nameof(WeakPositionsPlaceholder));
        OnPropertyChanged(nameof(ShowWeakPositionsPlaceholder));
    }

    private void StartGuidedStudy()
    {
        if (SelectedOpening is null || overview is null)
        {
            return;
        }

        guidedSession = workspaceService.BuildGuidedStudySession(SelectedOpening, overview, PlayerKey, SelectedStrictness);
        currentStepIndex = 0;
        MoveInput = string.Empty;
        ResultText = string.Empty;
        ResetResults();
        ClearStudySelection();
        LoadCurrentStep();
        SetPage(StudyPageIndex);
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

        OpeningTrainingAttemptResult result = workspaceService.Evaluate(position, MoveInput);
        string statusText = result.Status == OpeningTrainingAttemptStatus.TransposedToKnownPosition
            ? "TransposedToKnownPosition"
            : result.Score.ToString();
        ResultText = $"{statusText}: {result.ShortExplanation}";
        CurrentWhy = result.WhyThisMove?.ShortExplanation ?? position.BetterMoveReason ?? string.Empty;
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

        MoveInput = string.Empty;
        ClearStudySelection();
        MoveNext();
        RaiseResultsStateChanged();
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
            ClearStudySelection();
            OnPropertyChanged(nameof(PreviewArrows));
            RaiseStudyNavigationStateChanged();
            return;
        }

        PreviewFen = position.Fen;
        CurrentPrompt = $"Step {currentStepIndex + 1}/{guidedSession!.Positions.Count}: {position.Prompt}";
        CurrentWhy = position.BetterMoveReason ?? position.CandidateMoves.FirstOrDefault(option => option.IsPreferred)?.Idea?.ShortExplanation ?? string.Empty;
        PreviewArrows = BuildArrows(position);
        ClearStudySelection();
        OnPropertyChanged(nameof(PreviewArrows));
        RaiseStudyNavigationStateChanged();
    }

    private void CompleteStudy()
    {
        ResultHeadline = guidedSession is null
            ? "Guided study finished."
            : $"Finished {guidedSession.Positions.Count} guided positions for {SelectedOpeningName}.";
        ResultRecommendation = wrongAttempts > 0
            ? "Repeat this line soon. Wrong attempts suggest at least one branch still needs reinforcement."
            : playableAnswers > 0 || transposedAnswers > 0
                ? "The line is mostly stable. Review again after a short break to make the moves automatic."
                : "This line looks stable. You can move on to another branch or opening.";
        SetPage(ResultsPageIndex);
    }

    private void ResetResults()
    {
        completedSteps = 0;
        correctAnswers = 0;
        playableAnswers = 0;
        wrongAttempts = 0;
        transposedAnswers = 0;
        ResultHeadline = "Guided study in progress.";
        ResultRecommendation = "Finish the run to get a follow-up recommendation.";
        ReplaceItems(ResultItems, []);
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
        StartGuidedStudyCommand.RaiseCanExecuteChanged();
        RestartStudyCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedOpening));
        OnPropertyChanged(nameof(SelectedOpeningName));
        OnPropertyChanged(nameof(SelectedOpeningSideText));
    }

    private void RaiseStudyNavigationStateChanged()
    {
        EvaluateMoveCommand.RaiseCanExecuteChanged();
        NextStepCommand.RaiseCanExecuteChanged();
        PreviousStepCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(StudyProgressPercent));
        OnPropertyChanged(nameof(StudyProgressText));
        OnPropertyChanged(nameof(StudySelectedSquare));
        OnPropertyChanged(nameof(StudyAvailableMoveSquares));
        OnPropertyChanged(nameof(StudyBoardHint));
        OnPropertyChanged(nameof(StudyInputModeText));
    }

    private void RaiseResultsStateChanged()
    {
        OnPropertyChanged(nameof(ResultsSummaryText));
        OnPropertyChanged(nameof(TranspositionSummaryText));
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
