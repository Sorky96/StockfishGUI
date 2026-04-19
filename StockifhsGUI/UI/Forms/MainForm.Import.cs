using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Windows.Forms;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public partial class MainForm
{
    private readonly Stack<GameStateSnapshot> undoStack = new();
    private readonly ImportedGameSession importedSession = new();

    private MaterialButton? undoButton;
    private MaterialButton? importPgnButton;
    private MaterialButton? loadSavedGamesButton;
    private MaterialButton? applyNextImportedButton;
    private MaterialButton? applySelectedImportedButton;
    private MaterialButton? analyzeImportedButton;
    private MaterialButton? playerProfilesButton;
    private MaterialButton? savedAnalysesButton;
    private Label? importedMovesLabel;
    private ListBox? importedMovesList;
    private bool suppressImportedSelectionHandling;
    private bool suppressEngineRefresh;

    private void InitializeExtendedControls()
    {
        undoButton = new MaterialButton
        {
            Text = "Undo",
            AutoSize = false,
            Size = new Size(80, 36)
        };
        undoButton.Click += (_, _) => UndoLastMove();
        Controls.Add(undoButton);

        importPgnButton = new MaterialButton
        {
            Text = "Paste PGN",
            AutoSize = false,
            Size = new Size(120, 36)
        };
        importPgnButton.Click += (_, _) => ImportMovesFromPgn();
        sidebarLayout.Controls.Add(importPgnButton, 0, 0);
        importPgnButton.Dock = DockStyle.Fill;

        loadSavedGamesButton = new MaterialButton
        {
            Text = "Load Saved",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36)
        };
        loadSavedGamesButton.Click += (_, _) => LoadSavedImportedGame();
        sidebarLayout.Controls.Add(loadSavedGamesButton, 1, 0);
        loadSavedGamesButton.Dock = DockStyle.Fill;

        applyNextImportedButton = new MaterialButton
        {
            Text = "Apply Next",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36)
        };
        applyNextImportedButton.Click += (_, _) => ApplyNextImportedMove();
        sidebarLayout.Controls.Add(applyNextImportedButton, 0, 1);
        applyNextImportedButton.Dock = DockStyle.Fill;

        applySelectedImportedButton = new MaterialButton
        {
            Text = "Apply Selected",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36)
        };
        applySelectedImportedButton.Click += (_, _) => ApplyImportedMovesThroughSelection();
        sidebarLayout.Controls.Add(applySelectedImportedButton, 1, 1);
        applySelectedImportedButton.Dock = DockStyle.Fill;

        analyzeImportedButton = new MaterialButton
        {
            Text = "Analyze Imported",
            AutoSize = false,
            Size = new Size(120, 36)
        };
        analyzeImportedButton.Click += async (_, _) => await OpenImportedGameAnalysisAsync();
        sidebarLayout.Controls.Add(analyzeImportedButton, 0, 2);
        analyzeImportedButton.Dock = DockStyle.Fill;

        playerProfilesButton = new MaterialButton
        {
            Text = "Profiles",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36)
        };
        playerProfilesButton.Click += (_, _) => OpenPlayerProfiles();
        sidebarLayout.Controls.Add(playerProfilesButton, 1, 2);
        playerProfilesButton.Dock = DockStyle.Fill;

        savedAnalysesButton = new MaterialButton
        {
            Text = "Saved Analyses",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36)
        };
        savedAnalysesButton.Click += (_, _) => OpenSavedAnalyses();
        sidebarLayout.Controls.Add(savedAnalysesButton, 0, 3);
        sidebarLayout.SetColumnSpan(savedAnalysesButton, 2);
        savedAnalysesButton.Dock = DockStyle.Fill;

        importedMovesLabel = new Label
        {
            AutoSize = false,
            Size = new Size(260, 52),
            Text = "Imported moves: none",
            TextAlign = ContentAlignment.BottomLeft
        };
        sidebarLayout.Controls.Add(importedMovesLabel, 0, 4);
        sidebarLayout.SetColumnSpan(importedMovesLabel, 2);
        importedMovesLabel.Dock = DockStyle.Fill;

        importedMovesList = new ListBox
        {
            Font = new Font("Consolas", 10),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        importedMovesList.SelectedIndexChanged += (_, _) => ApplyImportedMovesThroughSelection(resetToStart: true);
        importedMovesList.DoubleClick += (_, _) => ApplyImportedMovesThroughSelection();
        sidebarLayout.Controls.Add(importedMovesList, 0, 5);
        sidebarLayout.SetColumnSpan(importedMovesList, 2);
        importedMovesList.Dock = DockStyle.Fill;

        InitializePieceMoveOptionsControls();
        InitializeTrackingControls();
    }

    private void ResetGameState()
    {
        ResetBoardState();
        importedSession.Clear();
        PopulateImportedMovesList();
    }

    private void ResetBoardState()
    {
        undoStack.Clear();
        analysisArrows.Clear();
        analysisTargetSquare = null;
        whiteToMove = true;
        whiteKingMoved = false;
        blackKingMoved = false;
        whiteRookLeftMoved = false;
        whiteRookRightMoved = false;
        blackRookLeftMoved = false;
        blackRookRightMoved = false;
        enPassantTargetSquare = null;
        halfmoveClock = 0;
        fullmoveNumber = 1;
        selectedSquare = null;
        availableMoves.Clear();
        bestMoveArrows.Clear();
        moveHistory.Clear();
        ClearPieceMoveOptions();
        LoadStartingPosition();
    }

    private bool TryExecuteMove(Point from, Point to, string piece, bool advanceImportedCursor)
    {
        if (!TryCreateGameFromCurrentPosition(out ChessGame? game, out string? error) || game is null)
        {
            return false;
        }

        string? promotionPiece = null;
        if (NeedsPromotion(piece, to))
        {
            using PromotionForm promotionDialog = new(IsPieceWhite(piece), pieceImages);
            if (promotionDialog.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(promotionDialog.SelectedPiece))
            {
                return false;
            }

            promotionPiece = promotionDialog.SelectedPiece;
        }

        string uciMove = BuildUciMove(from, to, promotionPiece);
        if (!game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out error) || appliedMove is null)
        {
            return false;
        }

        return TryApplyMoveResult(appliedMove, advanceImportedCursor, out _);
    }

    private void UndoLastMove()
    {
        if (undoStack.Count == 0)
        {
            SystemSounds.Beep.Play();
            return;
        }

        RestoreState(undoStack.Pop());
        ClearSelection();
        RefreshEngineSuggestions();
    }

    private void ImportMovesFromPgn()
    {
        using PgnPasteForm dialog = new();
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.PgnText))
        {
            return;
        }

        try
        {
            ImportedGame parsedGame = PgnGameParser.Parse(dialog.PgnText);
            LoadImportedGame(parsedGame);
            SaveImportedGameToStore(parsedGame);

            if (importedSession.Moves.Count == 0)
            {
                MessageBox.Show("No SAN moves were found in the pasted PGN.", "Paste PGN", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import moves from PGN.\n{ex.Message}", "Paste PGN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSavedImportedGame()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            MessageBox.Show("Local storage for imported games is unavailable on this machine.", "Saved Imported Games", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SavedImportedGamesForm dialog = new(store);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedGame is null)
        {
            return;
        }

        try
        {
            LoadImportedGame(dialog.SelectedGame);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the selected imported game.\n{ex.Message}", "Saved Imported Games", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyNextImportedMove()
    {
        if (!importedPlayback.TryApplyNextImportedMove(out ImportedPlaybackError? error))
        {
            PresentImportedPlaybackError(error);
        }
    }

    private void ApplyImportedMovesThroughSelection(bool resetToStart = false)
    {
        if (suppressImportedSelectionHandling || importedMovesList is null || importedMovesList.SelectedIndex < 0)
        {
            return;
        }

        int targetIndex = importedMovesList.SelectedIndex;
        if (!importedPlayback.TryApplySelection(targetIndex, resetToStart, out ImportedPlaybackError? error))
        {
            PresentImportedPlaybackError(error);
        }
    }

    private void ReplayImportedMovesThrough(int targetIndex)
    {
        if (!importedPlayback.TryReplayThrough(targetIndex, out ImportedPlaybackError? error))
        {
            PresentImportedPlaybackError(error);
        }
    }

    private void UpdateExtendedControls()
    {
        if (importedMovesLabel is not null)
        {
            importedMovesLabel.Text = BuildImportedGameSummaryText();
        }

        if (importedMovesList is not null)
        {
            suppressImportedSelectionHandling = true;
            for (int i = 0; i < importedMovesList.Items.Count; i++)
            {
                importedMovesList.SetSelected(i, false);
            }

            int highlightIndex = importedSession.HighlightIndex;
            if (highlightIndex >= 0 && highlightIndex < importedMovesList.Items.Count)
            {
                importedMovesList.SelectedIndex = highlightIndex;
                EnsureImportedMoveVisible(highlightIndex);
            }
            suppressImportedSelectionHandling = false;
        }

        if (undoButton is not null)
        {
            undoButton.Enabled = undoStack.Count > 0;
        }

        if (loadSavedGamesButton is not null)
        {
            loadSavedGamesButton.Enabled = AnalysisStoreProvider.GetStore() is not null;
        }

        if (applyNextImportedButton is not null)
        {
            applyNextImportedButton.Enabled = importedSession.Cursor < importedSession.Moves.Count;
        }

        if (applySelectedImportedButton is not null)
        {
            applySelectedImportedButton.Enabled = importedSession.Moves.Count > 0;
        }

        if (analyzeImportedButton is not null)
        {
            analyzeImportedButton.Enabled = importedSession.Game?.SanMoves.Count > 0 && engine is not null;
            analyzeImportedButton.Text = BuildAnalyzeButtonText();
        }

        if (playerProfilesButton is not null)
        {
            playerProfilesButton.Enabled = AnalysisStoreProvider.GetStore() is not null;
        }

        if (savedAnalysesButton is not null)
        {
            savedAnalysesButton.Enabled = AnalysisStoreProvider.GetStore() is not null;
        }
    }

    private void LoadImportedGame(ImportedGame parsedGame)
    {
        ArgumentNullException.ThrowIfNull(parsedGame);

        if (!importedPlayback.TryLoadImportedGame(parsedGame, out string? error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private static void SaveImportedGameToStore(ImportedGame parsedGame)
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
            // Import should still work if local persistence is temporarily unavailable.
        }
    }

    private string BuildImportedGameSummaryText()
    {
        return importedSession.BuildSummaryText(GetSavedAnalysisSide);
    }

    private string BuildAnalyzeButtonText()
    {
        return importedSession.BuildAnalyzeButtonText(GetSavedAnalysisSide);
    }

    private static bool HasSavedAnalysis(ImportedGame game, out PlayerSide? savedSide)
    {
        savedSide = null;
        EngineAnalysisOptions options = new();

        if (GameAnalysisCache.TryGetWindowState(game, out AnalysisWindowState? state) && state is not null)
        {
            GameAnalysisCacheKey preferredKey = GameAnalysisCache.CreateKey(game, state.SelectedSide, options);
            if (GameAnalysisCache.TryGetResult(preferredKey, out _))
            {
                savedSide = state.SelectedSide;
                return true;
            }
        }

        foreach (PlayerSide side in new[] { PlayerSide.White, PlayerSide.Black })
        {
            GameAnalysisCacheKey key = GameAnalysisCache.CreateKey(game, side, options);
            if (GameAnalysisCache.TryGetResult(key, out _))
            {
                savedSide = side;
                return true;
            }
        }

        return false;
    }

    private static PlayerSide? GetSavedAnalysisSide(ImportedGame game)
    {
        return HasSavedAnalysis(game, out PlayerSide? savedSide) ? savedSide : null;
    }

    private void EnsureImportedMoveVisible(int index)
    {
        if (importedMovesList is null || index < 0 || index >= importedMovesList.Items.Count)
        {
            return;
        }

        int itemHeight = Math.Max(1, importedMovesList.ItemHeight);
        int visibleItemCount = Math.Max(1, importedMovesList.ClientSize.Height / itemHeight);
        int targetTopIndex = Math.Max(0, index - (visibleItemCount / 2));
        importedMovesList.TopIndex = targetTopIndex;
    }

    private GameStateSnapshot CaptureCurrentState()
    {
        string?[,] boardCopy = new string?[GridSize, GridSize];
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                boardCopy[x, y] = board[x, y];
            }
        }

        return new GameStateSnapshot(
            boardCopy,
            new List<string>(moveHistory),
            whiteToMove,
            rotateBoard,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            enPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber,
            importedSession.Cursor);
    }

    private void RestoreState(GameStateSnapshot snapshot)
    {
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                board[x, y] = snapshot.Board[x, y];
            }
        }

        moveHistory.Clear();
        moveHistory.AddRange(snapshot.MoveHistory);
        whiteToMove = snapshot.WhiteToMove;
        rotateBoard = snapshot.RotateBoard;
        whiteKingMoved = snapshot.WhiteKingMoved;
        blackKingMoved = snapshot.BlackKingMoved;
        whiteRookLeftMoved = snapshot.WhiteRookLeftMoved;
        whiteRookRightMoved = snapshot.WhiteRookRightMoved;
        blackRookLeftMoved = snapshot.BlackRookLeftMoved;
        blackRookRightMoved = snapshot.BlackRookRightMoved;
        enPassantTargetSquare = snapshot.EnPassantTargetSquare;
        halfmoveClock = snapshot.HalfmoveClock;
        fullmoveNumber = snapshot.FullmoveNumber;
        importedSession.Cursor = snapshot.ImportedMoveCursor;
    }

    private void PopulateImportedMovesList()
    {
        if (importedMovesList is null)
        {
            return;
        }

        suppressImportedSelectionHandling = true;
        importedMovesList.Items.Clear();
        foreach (ImportedMoveListItem move in importedSession.Moves)
        {
            importedMovesList.Items.Add(move);
        }

        suppressImportedSelectionHandling = false;
    }

    private void PresentImportedPlaybackError(ImportedPlaybackError? error)
    {
        if (error is null)
        {
            return;
        }

        if (error.BeepOnly)
        {
            SystemSounds.Beep.Play();
            return;
        }

        MessageBox.Show(error.Message, error.Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    ImportedGameSession IImportedGamePlaybackHost.ImportedSession => importedSession;

    Stack<GameStateSnapshot> IImportedGamePlaybackHost.UndoStack => undoStack;

    IList<string> IImportedGamePlaybackHost.MoveHistory => moveHistory;

    bool IImportedGamePlaybackHost.SuppressEngineRefresh
    {
        get => suppressEngineRefresh;
        set => suppressEngineRefresh = value;
    }

    void IImportedGamePlaybackHost.ResetBoardState() => ResetBoardState();

    void IImportedGamePlaybackHost.ClearSelection() => ClearSelection();

    void IImportedGamePlaybackHost.ClearAnalysisFocus()
    {
        analysisArrows.Clear();
        analysisTargetSquare = null;
    }

    void IImportedGamePlaybackHost.RefreshEngineSuggestions() => RefreshEngineSuggestions();

    void IImportedGamePlaybackHost.UpdateImportedControls() => UpdateExtendedControls();

    void IImportedGamePlaybackHost.InvalidateBoardSurface() => InvalidateBoardSurface();

    void IImportedGamePlaybackHost.PopulateImportedMovesList() => PopulateImportedMovesList();

    GameStateSnapshot IImportedGamePlaybackHost.CaptureCurrentState() => CaptureCurrentState();

    bool IImportedGamePlaybackHost.TryApplyFen(string fen, out string? error) => TryApplyFen(fen, out error);

    bool IImportedGamePlaybackHost.TryApplyMoveResult(AppliedMoveInfo appliedMove, bool advanceImportedCursor, out string? error)
        => TryApplyMoveResult(appliedMove, advanceImportedCursor, out error);
}
