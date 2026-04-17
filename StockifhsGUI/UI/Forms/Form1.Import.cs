using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1
{
    private readonly Stack<GameStateSnapshot> undoStack = new();
    private readonly List<ImportedMove> importedMoves = new();
    private readonly List<ReplayPly> importedReplay = new();
    private ImportedGame? importedGame;

    private Button? undoButton;
    private Button? importPgnButton;
    private Button? loadSavedGamesButton;
    private Button? applyNextImportedButton;
    private Button? applySelectedImportedButton;
    private Button? analyzeImportedButton;
    private Button? playerProfilesButton;
    private Button? savedAnalysesButton;
    private Label? importedMovesLabel;
    private ListBox? importedMovesList;
    private int importedMoveCursor;
    private bool suppressImportedSelectionHandling;
    private bool suppressEngineRefresh;

    private void InitializeExtendedControls()
    {
        undoButton = new Button
        {
            Text = "Undo",
            Size = new Size(80, 30)
        };
        undoButton.Click += (_, _) => UndoLastMove();
        Controls.Add(undoButton);

        importPgnButton = new Button
        {
            Text = "Paste PGN",
            Size = new Size(120, 32)
        };
        importPgnButton.Click += (_, _) => ImportMovesFromPgn();
        Controls.Add(importPgnButton);

        loadSavedGamesButton = new Button
        {
            Text = "Load Saved",
            Size = new Size(120, 32)
        };
        loadSavedGamesButton.Click += (_, _) => LoadSavedImportedGame();
        Controls.Add(loadSavedGamesButton);

        applyNextImportedButton = new Button
        {
            Text = "Apply Next",
            Size = new Size(120, 32)
        };
        applyNextImportedButton.Click += (_, _) => ApplyNextImportedMove();
        Controls.Add(applyNextImportedButton);

        applySelectedImportedButton = new Button
        {
            Text = "Apply Selected",
            Size = new Size(120, 32)
        };
        applySelectedImportedButton.Click += (_, _) => ApplyImportedMovesThroughSelection();
        Controls.Add(applySelectedImportedButton);

        analyzeImportedButton = new Button
        {
            Text = "Analyze Imported",
            Size = new Size(120, 32)
        };
        analyzeImportedButton.Click += async (_, _) => await OpenImportedGameAnalysisAsync();
        Controls.Add(analyzeImportedButton);

        playerProfilesButton = new Button
        {
            Text = "Profiles",
            Size = new Size(120, 32)
        };
        playerProfilesButton.Click += (_, _) => OpenPlayerProfiles();
        Controls.Add(playerProfilesButton);

        savedAnalysesButton = new Button
        {
            Text = "Saved Analyses",
            Size = new Size(120, 32)
        };
        savedAnalysesButton.Click += (_, _) => OpenSavedAnalyses();
        Controls.Add(savedAnalysesButton);

        importedMovesLabel = new Label
        {
            AutoSize = false,
            Size = new Size(260, 52),
            Text = "Imported moves: none"
        };
        Controls.Add(importedMovesLabel);

        importedMovesList = new ListBox
        {
            Font = new Font("Consolas", 10),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        importedMovesList.SelectedIndexChanged += (_, _) => ApplyImportedMovesThroughSelection(resetToStart: true);
        importedMovesList.DoubleClick += (_, _) => ApplyImportedMovesThroughSelection();
        Controls.Add(importedMovesList);

        InitializePieceMoveOptionsControls();
        InitializeTrackingControls();
    }

    private void ResetGameState()
    {
        ResetBoardState();
        importedGame = null;
        importedMoves.Clear();
        importedReplay.Clear();
        importedMovesList?.Items.Clear();
        importedMoveCursor = 0;
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
        bestMoves.Clear();
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

            if (importedMoves.Count == 0)
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
        if (importedMoveCursor >= importedMoves.Count)
        {
            SystemSounds.Beep.Play();
            return;
        }

        ApplyImportedMove(importedMoveCursor, showError: true);
    }

    private void ApplyImportedMovesThroughSelection(bool resetToStart = false)
    {
        if (suppressImportedSelectionHandling || importedMovesList is null || importedMovesList.SelectedIndex < 0)
        {
            return;
        }

        int targetIndex = importedMovesList.SelectedIndex;
        if (TryJumpToImportedMove(targetIndex, preserveUndoHistory: false))
        {
            return;
        }

        if (resetToStart || targetIndex < importedMoveCursor)
        {
            ReplayImportedMovesThrough(targetIndex);
            return;
        }

        while (importedMoveCursor <= targetIndex)
        {
            if (!ApplyImportedMove(importedMoveCursor, showError: true))
            {
                break;
            }
        }
    }

    private void ReplayImportedMovesThrough(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= importedMoves.Count)
        {
            SystemSounds.Beep.Play();
            return;
        }

        if (TryJumpToImportedMove(targetIndex, preserveUndoHistory: false))
        {
            return;
        }

        ResetBoardState();
        importedMoveCursor = 0;
        ClearSelection();

        bool replayFailed = false;
        suppressEngineRefresh = true;
        try
        {
            for (int i = 0; i <= targetIndex; i++)
            {
                if (!ApplyImportedMove(i, showError: true))
                {
                    replayFailed = true;
                    break;
                }
            }
        }
        finally
        {
            suppressEngineRefresh = false;
        }

        RefreshEngineSuggestions();
        UpdateExtendedControls();
        InvalidateBoardSurface();

        if (replayFailed)
        {
            return;
        }
    }

    private bool ApplyImportedMove(int index, bool showError)
    {
        if (index < 0 || index >= importedMoves.Count)
        {
            return false;
        }

        if (TryJumpToImportedMove(index, preserveUndoHistory: true))
        {
            ClearSelection();
            return true;
        }

        ImportedMove move = importedMoves[index];
        if (!TryBuildImportedMoveResult(index, out AppliedMoveInfo? appliedMove, out string? error)
            || appliedMove is null)
        {
            if (showError)
            {
                MessageBox.Show($"Move {move.DisplayText} could not be applied.\n{error}", "Import PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        if (!TryApplyMoveResult(appliedMove, advanceImportedCursor: true, out error))
        {
            if (showError)
            {
                MessageBox.Show($"Move {move.DisplayText} could not be shown on the board.\n{error}", "Import PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        ClearSelection();
        return true;
    }

    private bool TryJumpToImportedMove(int index, bool preserveUndoHistory)
    {
        if (index < 0 || index >= importedReplay.Count)
        {
            return false;
        }

        if (preserveUndoHistory)
        {
            undoStack.Push(CaptureCurrentState());
        }
        else
        {
            undoStack.Clear();
        }

        ReplayPly replayPly = importedReplay[index];
        if (!TryApplyFen(replayPly.FenAfter, out _))
        {
            if (preserveUndoHistory && undoStack.Count > 0)
            {
                undoStack.Pop();
            }

            return false;
        }

        analysisArrows.Clear();
        analysisTargetSquare = null;
        moveHistory.Clear();
        foreach (ReplayPly appliedReplayPly in importedReplay.Take(index + 1))
        {
            moveHistory.Add(appliedReplayPly.Uci);
        }

        importedMoveCursor = index + 1;
        ClearSelection();

        if (!suppressEngineRefresh)
        {
            RefreshEngineSuggestions();
        }

        UpdateExtendedControls();
        InvalidateBoardSurface();
        return true;
    }

    private bool TryBuildImportedMoveResult(int index, out AppliedMoveInfo? appliedMove, out string? error)
    {
        appliedMove = null;
        error = null;

        try
        {
            ChessGame replayGame = new();
            for (int i = 0; i < index; i++)
            {
                replayGame.ApplySan(importedMoves[i].San);
            }

            appliedMove = replayGame.ApplySanWithResult(importedMoves[index].San);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
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

            int highlightIndex = importedMoveCursor == 0 ? -1 : importedMoveCursor - 1;
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
            applyNextImportedButton.Enabled = importedMoveCursor < importedMoves.Count;
        }

        if (applySelectedImportedButton is not null)
        {
            applySelectedImportedButton.Enabled = importedMoves.Count > 0;
        }

        if (analyzeImportedButton is not null)
        {
            analyzeImportedButton.Enabled = importedGame?.SanMoves.Count > 0 && engine is not null;
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

        GameReplayService replayService = new();
        IReadOnlyList<ReplayPly> replay = replayService.Replay(parsedGame);
        ResetGameState();
        importedGame = parsedGame;
        importedReplay.AddRange(replay);
        suppressImportedSelectionHandling = true;
        for (int i = 0; i < replay.Count; i++)
        {
            ReplayPly replayPly = replay[i];
            ImportedMove move = new(i + 1, replayPly.MoveNumber, replayPly.Side, replayPly.San);
            importedMoves.Add(move);
            importedMovesList?.Items.Add(move);
        }

        suppressImportedSelectionHandling = false;
        importedMoveCursor = 0;
        RefreshEngineSuggestions();
        UpdateExtendedControls();
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
        if (importedMoves.Count == 0 || importedGame is null)
        {
            return "Imported moves: none";
        }

        string players = $"{importedGame.WhitePlayer ?? "White"} vs {importedGame.BlackPlayer ?? "Black"}";
        string result = string.IsNullOrWhiteSpace(importedGame.Result) ? "Result: ?" : $"Result: {importedGame.Result}";
        string date = string.IsNullOrWhiteSpace(importedGame.DateText) ? string.Empty : $" | {importedGame.DateText}";
        string eco = string.IsNullOrWhiteSpace(importedGame.Eco) ? string.Empty : $" | {OpeningCatalog.Describe(importedGame.Eco)}";
        string analysisStatus = HasSavedAnalysis(importedGame, out PlayerSide? savedSide)
            ? $" | saved analysis: {savedSide}"
            : string.Empty;

        return $"Imported moves: {importedMoveCursor}/{importedMoves.Count} applied | {players}{Environment.NewLine}{result}{date}{eco}{analysisStatus}";
    }

    private string BuildAnalyzeButtonText()
    {
        if (importedGame is null)
        {
            return "Analyze Imported";
        }

        return HasSavedAnalysis(importedGame, out PlayerSide? savedSide)
            ? $"Open Analysis ({savedSide})"
            : "Analyze Imported";
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
            importedMoveCursor);
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
        importedMoveCursor = snapshot.ImportedMoveCursor;
    }

    private readonly record struct ImportedMove(int Ply, int MoveNumber, PlayerSide Side, string San)
    {
        public string DisplayText => Side == PlayerSide.White
            ? $"{MoveNumber,3}. {San}"
            : $"{MoveNumber,3}... {San}";

        public override string ToString() => DisplayText;
    }

    private sealed record GameStateSnapshot(
        string?[,] Board,
        List<string> MoveHistory,
        bool WhiteToMove,
        bool RotateBoard,
        bool WhiteKingMoved,
        bool BlackKingMoved,
        bool WhiteRookLeftMoved,
        bool WhiteRookRightMoved,
        bool BlackRookLeftMoved,
        bool BlackRookRightMoved,
        string? EnPassantTargetSquare,
        int HalfmoveClock,
        int FullmoveNumber,
        int ImportedMoveCursor);
}
