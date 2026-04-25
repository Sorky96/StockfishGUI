using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using MoveMentorChess.App.Views;

namespace MoveMentorChess.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<string> AnalysisFilterOptions = ["All", "Blunder", "Mistake", "Inaccuracy"];
    private static readonly IReadOnlyList<PlayerSide> AnalysisSides = [PlayerSide.White, PlayerSide.Black];

    private readonly ChessGame chessGame = new();
    private readonly Stack<string> undoFenStack = new();
    private readonly HashSet<string> availableTargets = new(StringComparer.Ordinal);

    private StockfishEngine? engine;
    private ImportedGame? importedGame;
    private IReadOnlyList<ReplayPly> importedReplay = Array.Empty<ReplayPly>();
    private GameAnalysisResult? cachedAnalysisResult;
    private Func<IReadOnlyList<LegalMoveInfo>, Task<LegalMoveInfo?>>? promotionMoveSelector;
    private string? selectedSquare;
    private string? previewTargetSquare;
    private string statusMessage = "MoveMentor Chess is ready.";
    private string importedGameSummary = "Imported moves: none";
    private string suggestionText = "Engine suggestions: unavailable";
    private string evaluationText = "Evaluation: unavailable";
    private string analysisDetails = "Load a PGN and run analysis to inspect mistakes.";
    private string pieceMoveOptionsHeader = "Selected piece: none";
    private ImportedMoveItemViewModel? selectedImportedMove;
    private PieceMoveOptionViewModel? selectedPieceMoveOption;
    private bool rotateBoard;
    private bool isBusy;
    private int importedCursor;
    private int evaluationBarValue = 50;
    private string selectedAnalysisFilter = AnalysisFilterOptions[0];
    private PlayerSide selectedAnalysisSide = PlayerSide.White;
    private AnalysisMistakeItemViewModel? selectedAnalysisMistake;

    public MainWindowViewModel()
    {
        UndoCommand = new RelayCommand(UndoLastMove, () => undoFenStack.Count > 0 && !IsBusy);
        RotateBoardCommand = new RelayCommand(ToggleBoardRotation, () => !IsBusy);
        ApplyNextImportedMoveCommand = new RelayCommand(ApplyNextImportedMove, () => !IsBusy && importedCursor < importedReplay.Count);
        ApplySelectedImportedMoveCommand = new RelayCommand(ApplySelectedImportedMove, () => !IsBusy && SelectedImportedMove is not null);
        AnalyzeImportedGameCommand = new RelayCommand(async () => await AnalyzeImportedGameAsync(), () => !IsBusy && importedGame is not null && engine is not null);
        ShowSelectedMistakeOnBoardCommand = new RelayCommand(ShowSelectedMistakeOnBoard, () => !IsBusy && SelectedAnalysisMistake is not null);

        TryInitializeEngine();
        ClearPieceMoveOptions();
        RefreshBoard();
        RefreshEngineSummary();
    }

    public ObservableCollection<ImportedMoveItemViewModel> ImportedMoves { get; } = [];

    public ObservableCollection<AnalysisMistakeItemViewModel> AnalysisMistakes { get; } = [];

    public ObservableCollection<PieceMoveOptionViewModel> PieceMoveOptions { get; } = [];

    public IReadOnlyList<string> AvailableAnalysisFilters => AnalysisFilterOptions;

    public IReadOnlyList<PlayerSide> AvailableAnalysisSides => AnalysisSides;

    public RelayCommand UndoCommand { get; }

    public RelayCommand RotateBoardCommand { get; }

    public RelayCommand ApplyNextImportedMoveCommand { get; }

    public RelayCommand ApplySelectedImportedMoveCommand { get; }

    public RelayCommand AnalyzeImportedGameCommand { get; }

    public RelayCommand ShowSelectedMistakeOnBoardCommand { get; }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string ImportedGameSummary
    {
        get => importedGameSummary;
        private set => SetProperty(ref importedGameSummary, value);
    }

    public string SuggestionText
    {
        get => suggestionText;
        private set => SetProperty(ref suggestionText, value);
    }

    public string EvaluationText
    {
        get => evaluationText;
        private set => SetProperty(ref evaluationText, value);
    }

    public string AnalysisDetails
    {
        get => analysisDetails;
        private set => SetProperty(ref analysisDetails, value);
    }

    public string PieceMoveOptionsHeader
    {
        get => pieceMoveOptionsHeader;
        private set => SetProperty(ref pieceMoveOptionsHeader, value);
    }

    public string BoardFen => chessGame.GetFen();

    public IReadOnlyList<string> AvailableMoveSquares => availableTargets.ToList();

    public IReadOnlyList<BoardArrowViewModel> BestMoveArrows { get; private set; } = [];

    public string? SelectedSquareName => selectedSquare;

    public string? PreviewTargetSquare
    {
        get => previewTargetSquare;
        private set => SetProperty(ref previewTargetSquare, value);
    }

    public bool RotateBoard
    {
        get => rotateBoard;
        private set => SetProperty(ref rotateBoard, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public int EvaluationBarValue
    {
        get => evaluationBarValue;
        private set => SetProperty(ref evaluationBarValue, value);
    }

    public string SelectedAnalysisFilter
    {
        get => selectedAnalysisFilter;
        set
        {
            if (SetProperty(ref selectedAnalysisFilter, value))
            {
                ApplyAnalysisFilter();
            }
        }
    }

    public PlayerSide SelectedAnalysisSide
    {
        get => selectedAnalysisSide;
        set => SetProperty(ref selectedAnalysisSide, value);
    }

    public ImportedMoveItemViewModel? SelectedImportedMove
    {
        get => selectedImportedMove;
        set
        {
            if (SetProperty(ref selectedImportedMove, value))
            {
                ApplySelectedImportedMoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public PieceMoveOptionViewModel? SelectedPieceMoveOption
    {
        get => selectedPieceMoveOption;
        set
        {
            if (SetProperty(ref selectedPieceMoveOption, value))
            {
                PreviewTargetSquare = string.IsNullOrWhiteSpace(value?.ToSquare) ? null : value.ToSquare;
                RefreshBoard();
            }
        }
    }

    public AnalysisMistakeItemViewModel? SelectedAnalysisMistake
    {
        get => selectedAnalysisMistake;
        set
        {
            if (SetProperty(ref selectedAnalysisMistake, value))
            {
                AnalysisDetails = value?.Details ?? "Select a mistake to inspect its details.";
                ShowSelectedMistakeOnBoardCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Dispose()
    {
        engine?.Dispose();
    }

    public void SetPromotionMoveSelector(Func<IReadOnlyList<LegalMoveInfo>, Task<LegalMoveInfo?>> selector)
    {
        promotionMoveSelector = selector;
    }

    public async Task HandleSquareClickAsync(string? squareName)
    {
        if (string.IsNullOrWhiteSpace(squareName) || IsBusy)
        {
            return;
        }

        if (!TryParseSquare(squareName, out (int X, int Y) point))
        {
            return;
        }

        string? piece = GetPieceAt(point.X, point.Y);
        if (selectedSquare is null)
        {
            if (string.IsNullOrEmpty(piece))
            {
                return;
            }

            bool isWhitePiece = char.IsUpper(piece[0]);
            if (isWhitePiece != chessGame.WhiteToMove)
            {
                return;
            }

            selectedSquare = squareName;
            List<LegalMoveInfo> movesForPiece = chessGame.GetLegalMoves()
                .Where(move => move.FromSquare == squareName)
                .ToList();
            availableTargets.Clear();
            foreach (LegalMoveInfo move in movesForPiece)
            {
                availableTargets.Add(move.ToSquare);
            }

            UpdatePieceMoveOptions(squareName, piece, movesForPiece);
            RefreshBoard();
            return;
        }

        if (selectedSquare == squareName)
        {
            ClearSelection();
            return;
        }

        List<LegalMoveInfo> matchingMoves = chessGame.GetLegalMoves()
            .Where(move => move.FromSquare == selectedSquare && move.ToSquare == squareName)
            .ToList();

        if (matchingMoves.Count == 0)
        {
            ClearSelection();
            StatusMessage = $"Move {selectedSquare}-{squareName} is not legal in the current position.";
            return;
        }

        string? uci = await SelectMoveToApplyAsync(matchingMoves);
        if (string.IsNullOrWhiteSpace(uci))
        {
            StatusMessage = "Promotion was canceled.";
            RefreshBoard();
            return;
        }

        string previousFen = chessGame.GetFen();
        if (!chessGame.TryApplyUci(uci, out _, out string? error))
        {
            StatusMessage = error ?? "Could not apply the selected move.";
            ClearSelection();
            return;
        }

        undoFenStack.Push(previousFen);
        importedCursor = importedReplay.Count > 0 ? Math.Min(importedCursor, importedReplay.Count) : 0;
        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = $"Applied {uci}.";
        RaiseCommandStates();
    }

    public void ImportPgn(string pgnText)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(pgnText))
        {
            return;
        }

        try
        {
            ImportedGame parsedGame = PgnGameParser.Parse(pgnText);
            importedReplay = new GameReplayService().Replay(parsedGame);
            importedGame = parsedGame;
            cachedAnalysisResult = null;
            importedCursor = 0;
            ImportedMoves.Clear();
            for (int i = 0; i < importedReplay.Count; i++)
            {
                ImportedMoves.Add(new ImportedMoveItemViewModel(i, importedReplay[i]));
            }

            undoFenStack.Clear();
            chessGame.Reset();
            SelectedImportedMove = null;
            ClearSelection();
            RefreshBoard();
            RefreshEngineSummary();
            RefreshImportedSummary();
            AnalysisMistakes.Clear();
            AnalysisDetails = "Imported game loaded. Choose a side and run analysis.";
            SaveImportedGame(parsedGame);
            StatusMessage = importedReplay.Count == 0
                ? "PGN loaded, but no SAN moves were found."
                : $"Imported {importedReplay.Count} plies from PGN.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not parse PGN: {ex.Message}";
        }
        finally
        {
            RaiseCommandStates();
        }
    }

    public PgnFileImportResult ImportPgnGames(PgnBatchParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (IsBusy)
        {
            return new PgnFileImportResult(0, parseResult.Errors.Count, []);
        }

        int skippedGames = parseResult.Errors.Count;
        if (parseResult.Games.Count == 0)
        {
            StatusMessage = skippedGames == 0
                ? "PGN file did not contain any games."
                : $"PGN file did not contain any parsed games. Skipped {skippedGames}.";
            RaiseCommandStates();
            return new PgnFileImportResult(0, skippedGames, []);
        }

        SaveImportedGames(parseResult.Games);
        if (!TryLoadFirstReplayableImportedGame(parseResult.Games, out int replaySkippedGames))
        {
            skippedGames += replaySkippedGames;
            StatusMessage = $"PGN file contained {parseResult.Games.Count} parsed games, but none could be replayed.";
            RaiseCommandStates();
            return new PgnFileImportResult(0, skippedGames, []);
        }

        skippedGames += replaySkippedGames;
        StatusMessage = skippedGames == 0
            ? $"Loaded {parseResult.Games.Count} games from PGN file. Showing the first game."
            : $"Loaded {parseResult.Games.Count} games from PGN file. Skipped {skippedGames}. Showing the first replayable game.";
        return new PgnFileImportResult(parseResult.Games.Count, skippedGames, parseResult.Games);
    }

    public void LoadImportedGame(ImportedGame game)
    {
        if (IsBusy)
        {
            return;
        }

        LoadImportedGameCore(game);
        StatusMessage = importedReplay.Count == 0
            ? "Saved game loaded, but it does not contain SAN moves."
            : $"Loaded saved game with {importedReplay.Count} plies.";
    }

    public async Task<BulkPgnAnalysisResult> AnalyzeImportedGamesAsync(IReadOnlyList<ImportedGame> games)
    {
        if (IsBusy || engine is null || games.Count == 0)
        {
            return new BulkPgnAnalysisResult(DetectPrimaryPlayer(games), 0, 0, 0, 0, []);
        }

        string? primaryPlayer = DetectPrimaryPlayer(games);
        int analyzed = 0;
        int cached = 0;
        int failed = 0;
        int skipped = 0;
        List<string> failureMessages = [];

        try
        {
            IsBusy = true;
            AnalysisMistakes.Clear();
            SelectedAnalysisMistake = null;
            AnalysisDetails = string.IsNullOrWhiteSpace(primaryPlayer)
                ? "The analysis engine is reviewing the imported PGN file. This may take a while."
                : $"The analysis engine is reviewing games for {primaryPlayer}. This may take a while.";

            EngineAnalysisOptions options = new();
            GameAnalysisService analysisService = new(engine);
            GameAnalysisResult? lastResult = null;

            foreach (ImportedGame game in games)
            {
                PlayerSide side = ResolveAnalysisSide(game, primaryPlayer, SelectedAnalysisSide);
                if (!PlayerMatchesSide(game, primaryPlayer, side))
                {
                    skipped++;
                    continue;
                }

                SelectedAnalysisSide = side;
                StatusMessage = BuildBulkAnalysisStatus(game, side, analyzed + cached + failed + skipped + 1, games.Count, primaryPlayer);

                GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(game, side, options);
                if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
                {
                    cached++;
                    lastResult = cachedResult;
                    continue;
                }

                try
                {
                    GameAnalysisResult result = await Task.Run(() => analysisService.AnalyzeGame(game, side, options));
                    GameAnalysisCache.StoreResult(cacheKey, result);
                    analyzed++;
                    lastResult = result;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (failureMessages.Count < 5)
                    {
                        failureMessages.Add(BuildAnalysisFailureMessage(game, side, ex));
                    }
                }
            }

            if (lastResult is not null)
            {
                LoadImportedGameCore(lastResult.Game);
                SelectedAnalysisSide = lastResult.AnalyzedSide;
                cachedAnalysisResult = lastResult;
                ApplyAnalysisFilter();
            }

            string playerText = string.IsNullOrWhiteSpace(primaryPlayer) ? string.Empty : $" for {primaryPlayer}";
            StatusMessage = $"Bulk analysis finished{playerText}. New: {analyzed}, cached: {cached}, skipped: {skipped}, failed: {failed}.";
        }
        finally
        {
            IsBusy = false;
            RefreshEngineSummary();
            RefreshImportedSummary();
        }

        return new BulkPgnAnalysisResult(primaryPlayer, analyzed, cached, skipped, failed, failureMessages);
    }

    public async Task NavigateToProfileExampleAsync(ProfileMistakeExample example)
    {
        if (IsBusy)
        {
            return;
        }

        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null || !store.TryLoadImportedGame(example.GameFingerprint, out ImportedGame? game) || game is null)
        {
            StatusMessage = "Could not find the selected game in local storage.";
            return;
        }

        LoadImportedGame(game);
        SelectedAnalysisSide = example.Side;
        await AnalyzeImportedGameAsync();

        AnalysisMistakeItemViewModel? matchingMistake = AnalysisMistakes.FirstOrDefault(item =>
            item.Mistake.Moves.Any(move => move.Replay.Ply == example.Ply));

        if (matchingMistake is not null)
        {
            SelectedAnalysisMistake = matchingMistake;
            ShowSelectedMistakeOnBoard();
            return;
        }

        if (!chessGame.TryLoadFen(example.FenBefore, out string? error))
        {
            StatusMessage = error ?? "Could not open the selected example position.";
            return;
        }

        importedCursor = Math.Max(0, example.Ply - 1);
        if (importedCursor - 1 >= 0 && importedCursor - 1 < ImportedMoves.Count)
        {
            SelectedImportedMove = ImportedMoves[importedCursor - 1];
        }

        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = $"Opened profile example for move {example.MoveNumber}{(example.Side == PlayerSide.White ? "." : "...")} {example.PlayedSan}.";
    }

    public async Task NavigateToOpeningExampleAsync(OpeningExampleGame example)
    {
        if (!await TryLoadStoredGameForNavigationAsync(example.GameFingerprint, example.Side))
        {
            return;
        }

        if (TryFocusAnalyzedMistake(example.FirstMistakePly))
        {
            StatusMessage = example.FirstMistakePly is int ply
                ? $"Opened {example.OpeningDisplayName} example at ply {ply}."
                : $"Opened {example.OpeningDisplayName} example.";
            return;
        }

        if (TryShowPositionBeforePly(
            example.FirstMistakePly,
            null,
            $"Opened {example.OpeningDisplayName} example game against {example.OpponentName}."))
        {
            return;
        }

        StatusMessage = "Could not open the selected opening example.";
    }

    public async Task NavigateToOpeningPositionAsync(OpeningMoveRecommendation recommendation)
    {
        if (!await TryLoadStoredGameForNavigationAsync(recommendation.GameFingerprint, recommendation.Side))
        {
            return;
        }

        if (TryShowPositionBeforePly(
            recommendation.Ply,
            recommendation.FenBefore,
            $"Opened {OpeningCatalog.Describe(recommendation.Eco)} position before {recommendation.MoveNumber}{(recommendation.Side == PlayerSide.White ? "." : "...")} {recommendation.PlayedSan}."))
        {
            return;
        }

        StatusMessage = "Could not open the selected opening position.";
    }

    public AnalysisWindow? CreateAnalysisWindow()
    {
        if (importedGame is null)
        {
            StatusMessage = "Import or load a game before opening analysis.";
            return null;
        }

        if (engine is null)
        {
            StatusMessage = "The analysis engine is unavailable.";
            return null;
        }

        if (IsBusy)
        {
            return null;
        }

        return new AnalysisWindow(importedGame, engine, NavigateToAnalysisMistakeAsync, ShowAnalysisProgressOnBoard, SelectedAnalysisSide);
    }

    public bool HasAnalysisEngine()
    {
        return engine is not null;
    }

    private void UndoLastMove()
    {
        if (undoFenStack.Count == 0 || IsBusy)
        {
            return;
        }

        string previousFen = undoFenStack.Pop();
        chessGame.TryLoadFen(previousFen, out _);
        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        StatusMessage = "Last move has been undone.";
        RaiseCommandStates();
    }

    private void ToggleBoardRotation()
    {
        RotateBoard = !RotateBoard;
        RefreshBoard();
    }

    private void ApplyNextImportedMove()
    {
        if (importedCursor >= importedReplay.Count || IsBusy)
        {
            return;
        }

        ApplyImportedMoveByIndex(importedCursor);
    }

    private void ApplySelectedImportedMove()
    {
        if (SelectedImportedMove is null || IsBusy)
        {
            return;
        }

        ApplyImportedMoveByIndex(SelectedImportedMove.Index);
    }

    private void ApplyImportedMoveByIndex(int index)
    {
        if (index < 0 || index >= importedReplay.Count)
        {
            return;
        }

        ReplayPly replayPly = importedReplay[index];
        undoFenStack.Clear();
        if (!chessGame.TryLoadFen(replayPly.FenAfter, out string? error))
        {
            StatusMessage = error ?? "Could not load the imported position.";
            return;
        }

        importedCursor = index + 1;
        SelectedImportedMove = ImportedMoves[index];
        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = $"Moved board to {ImportedMoves[index].DisplayText}.";
        RaiseCommandStates();
    }

    private void LoadImportedGameCore(ImportedGame game)
    {
        importedReplay = new GameReplayService().Replay(game);
        importedGame = game;
        cachedAnalysisResult = null;
        importedCursor = 0;
        ImportedMoves.Clear();
        for (int i = 0; i < importedReplay.Count; i++)
        {
            ImportedMoves.Add(new ImportedMoveItemViewModel(i, importedReplay[i]));
        }

        undoFenStack.Clear();
        chessGame.Reset();
        SelectedImportedMove = null;
        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        AnalysisMistakes.Clear();
        AnalysisDetails = "Imported game loaded. Choose a side and run analysis.";
        RaiseCommandStates();
    }

    private bool TryLoadFirstReplayableImportedGame(IReadOnlyList<ImportedGame> games, out int skippedGames)
    {
        skippedGames = 0;
        foreach (ImportedGame game in games)
        {
            try
            {
                LoadImportedGameCore(game);
                return true;
            }
            catch
            {
                skippedGames++;
            }
        }

        return false;
    }

    private async Task AnalyzeImportedGameAsync()
    {
        if (importedGame is null || engine is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Analyzing imported game for {SelectedAnalysisSide}...";
            AnalysisDetails = "The analysis engine is reviewing the imported game. This may take a moment.";
            AnalysisMistakes.Clear();
            SelectedAnalysisMistake = null;
            IProgress<GameAnalysisProgress> progress = new Progress<GameAnalysisProgress>(ShowAnalysisProgressOnBoard);

            GameAnalysisService analysisService = new(engine);
            cachedAnalysisResult = await Task.Run(() => analysisService.AnalyzeGame(
                importedGame,
                SelectedAnalysisSide,
                new EngineAnalysisOptions(),
                progress));
            ApplyAnalysisFilter();
            StatusMessage = $"Analysis finished for {SelectedAnalysisSide}.";
        }
        catch (Exception ex)
        {
            cachedAnalysisResult = null;
            AnalysisMistakes.Clear();
            AnalysisDetails = "Analysis failed.";
            StatusMessage = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshEngineSummary();
            RefreshImportedSummary();
        }
    }

    private void ShowAnalysisProgressOnBoard(GameAnalysisProgress progress)
    {
        if (!chessGame.TryLoadFen(progress.Fen, out _))
        {
            return;
        }

        importedCursor = progress.Stage == GameAnalysisProgressStage.AfterMove
            ? progress.Replay.Ply
            : Math.Max(0, progress.Replay.Ply - 1);

        if (progress.Replay.Ply - 1 >= 0 && progress.Replay.Ply - 1 < ImportedMoves.Count)
        {
            SelectedImportedMove = ImportedMoves[progress.Replay.Ply - 1];
        }

        BestMoveArrows = [new BoardArrowViewModel(progress.Replay.FromSquare, progress.Replay.ToSquare, Color.Parse("#D33838"))];
        ClearSelection();
        RefreshImportedSummary();

        string positionText = progress.Stage == GameAnalysisProgressStage.BeforeMove
            ? "before"
            : "after";
        StatusMessage = $"Analyzing {progress.CurrentAnalyzedMove}/{progress.TotalAnalyzedMoves}: {positionText} {progress.Replay.MoveNumber}{(progress.Replay.Side == PlayerSide.White ? "." : "...")} {progress.Replay.San}.";
    }

    private void ApplyAnalysisFilter()
    {
        AnalysisMistakes.Clear();
        SelectedAnalysisMistake = null;

        if (cachedAnalysisResult is null)
        {
            AnalysisDetails = importedGame is null
                ? "Load a PGN and run analysis to inspect mistakes."
                : "Run analysis to inspect mistakes.";
            return;
        }

        IEnumerable<SelectedMistake> source = cachedAnalysisResult.HighlightedMistakes;
        source = SelectedAnalysisFilter switch
        {
            "Blunder" => source.Where(item => item.Quality == MoveQualityBucket.Blunder),
            "Mistake" => source.Where(item => item.Quality == MoveQualityBucket.Mistake),
            "Inaccuracy" => source.Where(item => item.Quality == MoveQualityBucket.Inaccuracy),
            _ => source
        };

        foreach (SelectedMistake mistake in source)
        {
            AnalysisMistakes.Add(new AnalysisMistakeItemViewModel(mistake));
        }

        if (AnalysisMistakes.Count == 0)
        {
            AnalysisDetails = "No mistakes match the selected filter.";
            return;
        }

        SelectedAnalysisMistake = AnalysisMistakes[0];
    }

    private void ShowSelectedMistakeOnBoard()
    {
        if (SelectedAnalysisMistake is null)
        {
            return;
        }

        ReplayPly replayPly = SelectedAnalysisMistake.LeadMove.Replay;
        if (!chessGame.TryLoadFen(replayPly.FenAfter, out string? error))
        {
            StatusMessage = error ?? "Could not navigate to the selected mistake.";
            return;
        }

        importedCursor = replayPly.Ply;
        if (replayPly.Ply - 1 >= 0 && replayPly.Ply - 1 < ImportedMoves.Count)
        {
            SelectedImportedMove = ImportedMoves[replayPly.Ply - 1];
        }

        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = $"Jumped to the board position after {replayPly.MoveNumber}{(replayPly.Side == PlayerSide.White ? "." : "...")} {replayPly.San}.";
    }

    private async Task<bool> TryLoadStoredGameForNavigationAsync(string gameFingerprint, PlayerSide side)
    {
        if (IsBusy)
        {
            return false;
        }

        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null || !store.TryLoadImportedGame(gameFingerprint, out ImportedGame? game) || game is null)
        {
            StatusMessage = "Could not find the selected game in local storage.";
            return false;
        }

        LoadImportedGame(game);
        SelectedAnalysisSide = side;
        await AnalyzeImportedGameAsync();
        return true;
    }

    private bool TryFocusAnalyzedMistake(int? ply)
    {
        if (!ply.HasValue)
        {
            return false;
        }

        AnalysisMistakeItemViewModel? matchingMistake = AnalysisMistakes.FirstOrDefault(item =>
            item.Mistake.Moves.Any(move => move.Replay.Ply == ply.Value));

        if (matchingMistake is null)
        {
            return false;
        }

        SelectedAnalysisMistake = matchingMistake;
        ShowSelectedMistakeOnBoard();
        return true;
    }

    private bool TryShowPositionBeforePly(int? ply, string? fenBefore, string successMessage)
    {
        string? targetFen = !string.IsNullOrWhiteSpace(fenBefore)
            ? fenBefore
            : ResolveFenBeforePly(ply);
        if (string.IsNullOrWhiteSpace(targetFen))
        {
            return false;
        }

        if (!chessGame.TryLoadFen(targetFen, out string? error))
        {
            StatusMessage = error ?? "Could not open the selected position.";
            return false;
        }

        importedCursor = Math.Max(0, (ply ?? 1) - 1);
        if (importedCursor - 1 >= 0 && importedCursor - 1 < ImportedMoves.Count)
        {
            SelectedImportedMove = ImportedMoves[importedCursor - 1];
        }
        else
        {
            SelectedImportedMove = null;
        }

        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = successMessage;
        return true;
    }

    public Task NavigateToAnalysisMistakeAsync(MoveAnalysisResult move)
    {
        if (!chessGame.TryLoadFen(move.Replay.FenAfter, out string? error))
        {
            StatusMessage = error ?? "Could not navigate to the selected mistake.";
            return Task.CompletedTask;
        }

        importedCursor = move.Replay.Ply;
        if (move.Replay.Ply - 1 >= 0 && move.Replay.Ply - 1 < ImportedMoves.Count)
        {
            SelectedImportedMove = ImportedMoves[move.Replay.Ply - 1];
        }
        else
        {
            SelectedImportedMove = null;
        }

        ClearSelection();
        RefreshBoard();
        RefreshEngineSummary();
        RefreshImportedSummary();
        StatusMessage = $"Jumped to the board position after {move.Replay.MoveNumber}{(move.Replay.Side == PlayerSide.White ? "." : "...")} {move.Replay.San}.";
        return Task.CompletedTask;
    }

    private string? ResolveFenBeforePly(int? ply)
    {
        if (!ply.HasValue)
        {
            return null;
        }

        if (ply.Value <= 1)
        {
            ChessGame initialGame = new();
            return initialGame.GetFen();
        }

        int previousIndex = ply.Value - 2;
        if (previousIndex < 0 || previousIndex >= importedReplay.Count)
        {
            return null;
        }

        return importedReplay[previousIndex].FenAfter;
    }

    private void TryInitializeEngine()
    {
        string? enginePath = ResolveStockfishPath();
        try
        {
            if (string.IsNullOrWhiteSpace(enginePath))
            {
                throw new FileNotFoundException("Could not locate the external chess engine executable.");
            }

            engine = new StockfishEngine(enginePath);
            engine.SendCommand("setoption name MultiPV value 3");
            StatusMessage = $"MoveMentor Chess is ready. External chess engine loaded from {enginePath}.";
        }
        catch (Exception ex)
        {
            engine = null;
            StatusMessage = $"MoveMentor Chess is ready, but the analysis engine is unavailable. {ex.Message}";
        }
    }

    private void RefreshEngineSummary()
    {
        if (engine is null)
        {
            SuggestionText = "Engine suggestions: unavailable";
            EvaluationText = "Evaluation: unavailable";
            EvaluationBarValue = 50;
            BestMoveArrows = [];
            OnPropertyChanged(nameof(BestMoveArrows));
            AnalyzeImportedGameCommand.RaiseCanExecuteChanged();
            return;
        }

        try
        {
            string currentFen = chessGame.GetFen();
            engine.SetPositionFen(currentFen);
            string[] moves = engine.GetTopMoves(3).ToArray();
            SuggestionText = moves.Length == 0
                ? "Top moves: none"
                : "Top moves: " + string.Join(", ", moves);
            BestMoveArrows = moves
                .Where(move => move.Length >= 4)
                .Select((move, index) => new BoardArrowViewModel(
                    move[..2],
                    move.Substring(2, 2),
                    index switch
                    {
                        0 => Color.Parse("#2146FF"),
                        1 => Color.Parse("#169C16"),
                        _ => Color.Parse("#F39C12")
                    }))
                .ToList();
            OnPropertyChanged(nameof(BestMoveArrows));

            EvaluationSummary? evaluation = engine.GetEvaluationSummary();
            if (evaluation is null)
            {
                EvaluationText = "Evaluation: unavailable";
                EvaluationBarValue = 50;
            }
            else if (evaluation.MateIn is int mateIn)
            {
                int signedMate = chessGame.WhiteToMove ? mateIn : -mateIn;
                bool whiteWinning = signedMate > 0;
                EvaluationText = whiteWinning
                    ? $"Evaluation: White mates in {Math.Abs(signedMate)}"
                    : $"Evaluation: Black mates in {Math.Abs(signedMate)}";
                EvaluationBarValue = whiteWinning ? 100 : 0;
            }
            else
            {
                int cp = evaluation.Centipawns ?? 0;
                int whitePerspectiveCp = chessGame.WhiteToMove ? cp : -cp;
                double pawns = whitePerspectiveCp / 100.0;
                double normalized = Math.Clamp((pawns + 5.0) / 10.0, 0.0, 1.0);
                EvaluationBarValue = (int)Math.Round(normalized * 100);
                EvaluationText = Math.Abs(pawns) < 0.15
                    ? "Evaluation: even"
                    : pawns > 0
                        ? $"Evaluation: White {pawns:+0.0;-0.0;0.0}"
                        : $"Evaluation: Black +{Math.Abs(pawns):0.0}";
            }
        }
        catch (Exception ex)
        {
            SuggestionText = "Engine suggestions: unavailable";
            EvaluationText = "Evaluation: unavailable";
            EvaluationBarValue = 50;
            BestMoveArrows = [];
            OnPropertyChanged(nameof(BestMoveArrows));
            StatusMessage = $"Engine refresh failed: {ex.Message}";
        }
        finally
        {
            AnalyzeImportedGameCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshImportedSummary()
    {
        if (importedGame is null || importedReplay.Count == 0)
        {
            ImportedGameSummary = "Imported moves: none";
            return;
        }

        string players = $"{importedGame.WhitePlayer ?? "White"} vs {importedGame.BlackPlayer ?? "Black"}";
        string result = string.IsNullOrWhiteSpace(importedGame.Result) ? "Result: ?" : $"Result: {importedGame.Result}";
        string eco = string.IsNullOrWhiteSpace(importedGame.Eco) ? string.Empty : $" | {OpeningCatalog.Describe(importedGame.Eco)}";
        string date = string.IsNullOrWhiteSpace(importedGame.DateText) ? string.Empty : $" | {importedGame.DateText}";
        ImportedGameSummary = $"Imported moves: {importedCursor}/{importedReplay.Count} applied | {players}{Environment.NewLine}{result}{date}{eco}";
    }

    private void RefreshBoard()
    {
        OnPropertyChanged(nameof(BoardFen));
        OnPropertyChanged(nameof(SelectedSquareName));
        OnPropertyChanged(nameof(AvailableMoveSquares));
        OnPropertyChanged(nameof(BestMoveArrows));
        OnPropertyChanged(nameof(RotateBoard));
        RaiseCommandStates();
    }

    private string? GetPieceAt(int x, int y)
    {
        if (!FenPosition.TryParse(chessGame.GetFen(), out FenPosition? position, out _)
            || position is null)
        {
            return null;
        }

        return position.Board[x, y];
    }

    private void ClearSelection()
    {
        selectedSquare = null;
        availableTargets.Clear();
        PreviewTargetSquare = null;
        SelectedPieceMoveOption = null;
        ClearPieceMoveOptions();
        RefreshBoard();
    }

    private void UpdatePieceMoveOptions(string fromSquare, string pieceName, IReadOnlyList<LegalMoveInfo> movesForPiece)
    {
        PieceMoveOptions.Clear();
        PieceMoveOptionsHeader = $"Selected piece: {pieceName} from {fromSquare} | legal moves: {movesForPiece.Count}";
        PreviewTargetSquare = null;
        SelectedPieceMoveOption = null;

        if (movesForPiece.Count == 0)
        {
            PieceMoveOptions.Add(new PieceMoveOptionViewModel("-", string.Empty, "No legal moves for this piece.", string.Empty, false));
            return;
        }

        string currentFen = chessGame.GetFen();
        string perspectiveSide = chessGame.WhiteToMove ? "White" : "Black";
        EngineAnalysis? baselineAnalysis = null;
        string? bestMove = null;

        if (engine is not null)
        {
            try
            {
                baselineAnalysis = engine.AnalyzePosition(currentFen, new EngineAnalysisOptions(Depth: 10, MultiPv: 1, MoveTimeMs: 90));
                bestMove = baselineAnalysis.BestMoveUci;
            }
            catch
            {
                baselineAnalysis = null;
                bestMove = null;
            }
        }

        foreach (LegalMoveInfo move in movesForPiece.OrderBy(m => m.San, StringComparer.Ordinal))
        {
            string label = BuildPieceMoveLabel(move, currentFen, perspectiveSide, baselineAnalysis, bestMove);
            PieceMoveOptions.Add(new PieceMoveOptionViewModel(move.San, move.Uci, label, move.ToSquare, string.Equals(move.Uci, bestMove, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void ClearPieceMoveOptions()
    {
        PieceMoveOptions.Clear();
        PieceMoveOptionsHeader = "Selected piece: none";
        PieceMoveOptions.Add(new PieceMoveOptionViewModel("-", string.Empty, "Select a piece to inspect all legal moves.", string.Empty, false));
    }

    private string BuildPieceMoveLabel(LegalMoveInfo move, string currentFen, string perspectiveSide, EngineAnalysis? baselineAnalysis, string? bestMoveUci)
    {
        string bestMarker = string.Equals(move.Uci, bestMoveUci, StringComparison.OrdinalIgnoreCase) ? "* " : "  ";
        if (engine is null || baselineAnalysis is null)
        {
            return $"{bestMarker}{FormatSanAndUci(move.San, move.Uci),-18} | n/a        | d n/a";
        }

        try
        {
            ChessGame tempGame = new();
            if (!tempGame.TryLoadFen(currentFen, out _)
                || !tempGame.TryApplyUci(move.Uci, out AppliedMoveInfo? appliedMove, out _)
                || appliedMove is null)
            {
                return $"{bestMarker}{FormatSanAndUci(move.San, move.Uci),-18} | n/a        | d n/a";
            }

            EngineLine? baselineLine = baselineAnalysis.Lines.FirstOrDefault();
            int baselineCp = NormalizePerspectiveScore(baselineLine?.Centipawns, perspectiveSide, perspectiveSide);
            EngineAnalysis moveAnalysis = engine.AnalyzePosition(appliedMove.FenAfter, new EngineAnalysisOptions(Depth: 10, MultiPv: 1, MoveTimeMs: 90));
            EngineLine? moveLine = moveAnalysis.Lines.FirstOrDefault();
            int moveCp = NormalizePerspectiveScore(moveLine?.Centipawns, perspectiveSide, perspectiveSide == "White" ? "Black" : "White");
            int? delta = baselineLine?.Centipawns is null || moveLine?.Centipawns is null ? null : moveCp - baselineCp;
            string scoreText = moveLine?.MateIn is int mate ? $"mate {mate}" : $"{moveCp / 100.0:+0.0;-0.0;0.0}";
            string deltaText = delta is int d ? (d >= 0 ? $"+{d}" : d.ToString()) : "n/a";
            return $"{bestMarker}{FormatSanAndUci(move.San, move.Uci),-18} | {scoreText,-10} | d {deltaText}";
        }
        catch
        {
            return $"{bestMarker}{FormatSanAndUci(move.San, move.Uci),-18} | n/a        | d n/a";
        }
    }

    private async Task<string?> SelectMoveToApplyAsync(IReadOnlyList<LegalMoveInfo> matchingMoves)
    {
        if (matchingMoves.Count == 0)
        {
            return null;
        }

        if (matchingMoves.Count == 1)
        {
            return matchingMoves[0].Uci;
        }

        if (promotionMoveSelector is not null && matchingMoves.All(move => !string.IsNullOrWhiteSpace(move.PromotionPiece)))
        {
            LegalMoveInfo? selectedMove = await promotionMoveSelector(matchingMoves);
            return selectedMove?.Uci;
        }

        string queenPiece = chessGame.WhiteToMove ? "Q" : "q";
        LegalMoveInfo? queenPromotion = matchingMoves.FirstOrDefault(move => string.Equals(move.PromotionPiece, queenPiece, StringComparison.Ordinal));
        return queenPromotion?.Uci ?? matchingMoves[0].Uci;
    }

    private void RaiseCommandStates()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RotateBoardCommand.RaiseCanExecuteChanged();
        ApplyNextImportedMoveCommand.RaiseCanExecuteChanged();
        ApplySelectedImportedMoveCommand.RaiseCanExecuteChanged();
        AnalyzeImportedGameCommand.RaiseCanExecuteChanged();
        ShowSelectedMistakeOnBoardCommand.RaiseCanExecuteChanged();
    }

    private static void SaveImportedGame(ImportedGame parsedGame)
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            return;
        }

        try
        {
            store.SaveImportedGame(parsedGame);
        }
        catch
        {
            // Import should still succeed even if local persistence is temporarily unavailable.
        }
    }

    private static void SaveImportedGames(IReadOnlyList<ImportedGame> games)
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null || games.Count == 0)
        {
            return;
        }

        try
        {
            store.SaveImportedGames(games);
        }
        catch
        {
            // Import should still succeed even if local persistence is temporarily unavailable.
        }
    }

    public static string? DetectPrimaryPlayer(IReadOnlyList<ImportedGame> games)
    {
        return games
            .SelectMany(game => new[] { game.WhitePlayer, game.BlackPlayer })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Key;
    }

    private static PlayerSide ResolveAnalysisSide(ImportedGame game, string? primaryPlayer, PlayerSide fallbackSide)
    {
        if (!string.IsNullOrWhiteSpace(primaryPlayer))
        {
            if (string.Equals(game.WhitePlayer, primaryPlayer, StringComparison.OrdinalIgnoreCase))
            {
                return PlayerSide.White;
            }

            if (string.Equals(game.BlackPlayer, primaryPlayer, StringComparison.OrdinalIgnoreCase))
            {
                return PlayerSide.Black;
            }
        }

        return fallbackSide;
    }

    private static bool PlayerMatchesSide(ImportedGame game, string? primaryPlayer, PlayerSide side)
    {
        if (string.IsNullOrWhiteSpace(primaryPlayer))
        {
            return true;
        }

        string? playerName = side == PlayerSide.White ? game.WhitePlayer : game.BlackPlayer;
        return string.Equals(playerName, primaryPlayer, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBulkAnalysisStatus(
        ImportedGame game,
        PlayerSide side,
        int current,
        int total,
        string? primaryPlayer)
    {
        string playerText = string.IsNullOrWhiteSpace(primaryPlayer)
            ? side.ToString()
            : $"{primaryPlayer} as {side}";
        string players = $"{game.WhitePlayer ?? "White"} vs {game.BlackPlayer ?? "Black"}";
        return $"Analyzing PGN file {current}/{total}: {players} ({playerText})...";
    }

    private static string BuildAnalysisFailureMessage(ImportedGame game, PlayerSide side, Exception ex)
    {
        string players = $"{game.WhitePlayer ?? "White"} vs {game.BlackPlayer ?? "Black"}";
        string date = string.IsNullOrWhiteSpace(game.DateText) ? string.Empty : $" {game.DateText}";
        return $"{players}{date}, {side}: {ex.Message}";
    }

    private static bool TryParseSquare(string square, out (int X, int Y) point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(square) || square.Length != 2)
        {
            return false;
        }

        char file = char.ToLowerInvariant(square[0]);
        char rank = square[1];
        if (file < 'a' || file > 'h' || rank < '1' || rank > '8')
        {
            return false;
        }

        point = (file - 'a', 8 - (rank - '0'));
        return true;
    }

    private static string FormatSanAndUci(string san, string uci)
        => string.Equals(san, uci, StringComparison.OrdinalIgnoreCase) ? san : $"{san} ({uci})";

    private static int NormalizePerspectiveScore(int? cp, string perspectiveSide, string sideToMove)
    {
        int sign = string.Equals(perspectiveSide, sideToMove, StringComparison.Ordinal) ? 1 : -1;
        return (cp ?? 0) * sign;
    }

    private static string? ResolveStockfishPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDirectory, "stockfish.exe"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "MoveMentorChessServices", "bin", "Debug", "net8.0-windows", "stockfish.exe"),
            Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "MoveMentorChessServices", "bin", "Debug", "net8.0-windows", "stockfish.exe"),
            Path.Combine(Environment.CurrentDirectory, "MoveMentorChessServices", "bin", "Debug", "net8.0-windows", "stockfish.exe"),
            Path.Combine(Environment.CurrentDirectory, "stockfish.exe")
        ];

        foreach (string candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}

public sealed record PgnFileImportResult(
    int ImportedGames,
    int SkippedGames,
    IReadOnlyList<ImportedGame> Games);

public sealed record BulkPgnAnalysisResult(
    string? PrimaryPlayer,
    int AnalyzedGames,
    int CachedGames,
    int SkippedGames,
    int FailedGames,
    IReadOnlyList<string> FailureMessages);
