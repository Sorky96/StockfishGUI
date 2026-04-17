using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1
{
    private readonly Stack<GameStateSnapshot> undoStack = new();
    private readonly List<ImportedMove> importedMoves = new();
    private ImportedGame? importedGame;

    private Button? undoButton;
    private Button? importPgnButton;
    private Button? applyNextImportedButton;
    private Button? applySelectedImportedButton;
    private Button? analyzeImportedButton;
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
            Location = new Point(430, TileSize * GridSize + 5),
            Size = new Size(80, 30)
        };
        undoButton.Click += (_, _) => UndoLastMove();
        Controls.Add(undoButton);

        importPgnButton = new Button
        {
            Text = "Paste PGN",
            Location = new Point(TileSize * GridSize + 20, 16),
            Size = new Size(120, 32)
        };
        importPgnButton.Click += (_, _) => ImportMovesFromPgn();
        Controls.Add(importPgnButton);

        applyNextImportedButton = new Button
        {
            Text = "Apply Next",
            Location = new Point(TileSize * GridSize + 150, 16),
            Size = new Size(120, 32)
        };
        applyNextImportedButton.Click += (_, _) => ApplyNextImportedMove();
        Controls.Add(applyNextImportedButton);

        applySelectedImportedButton = new Button
        {
            Text = "Apply Selected",
            Location = new Point(TileSize * GridSize + 20, 56),
            Size = new Size(120, 32)
        };
        applySelectedImportedButton.Click += (_, _) => ApplyImportedMovesThroughSelection();
        Controls.Add(applySelectedImportedButton);

        analyzeImportedButton = new Button
        {
            Text = "Analyze Imported",
            Location = new Point(TileSize * GridSize + 150, 56),
            Size = new Size(120, 32)
        };
        analyzeImportedButton.Click += async (_, _) => await OpenImportedGameAnalysisAsync();
        Controls.Add(analyzeImportedButton);

        importedMovesLabel = new Label
        {
            AutoSize = false,
            Location = new Point(TileSize * GridSize + 20, 100),
            Size = new Size(260, 36),
            Text = "Imported moves: none"
        };
        Controls.Add(importedMovesLabel);

        importedMovesList = new ListBox
        {
            Location = new Point(TileSize * GridSize + 20, 140),
            Size = new Size(260, 250),
            Font = new Font("Consolas", 10)
        };
        importedMovesList.SelectedIndexChanged += (_, _) => ApplyImportedMovesThroughSelection(resetToStart: true);
        importedMovesList.DoubleClick += (_, _) => ApplyImportedMovesThroughSelection();
        Controls.Add(importedMovesList);

        InitializeTrackingControls();
    }

    private void ResetGameState()
    {
        ResetBoardState();
        importedGame = null;
        importedMoves.Clear();
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
        selectedSquare = null;
        availableMoves.Clear();
        bestMoves.Clear();
        moveHistory.Clear();
        LoadStartingPosition();
    }

    private bool TryExecuteMove(Point from, Point to, string piece, string? importedSan, bool advanceImportedCursor)
    {
        if (!IsLegalMove(from, to, piece))
        {
            return false;
        }

        string? promotionPiece = null;
        if (NeedsPromotion(piece, to))
        {
            if (!string.IsNullOrEmpty(importedSan))
            {
                promotionPiece = GetPromotionPieceFromSan(importedSan, IsPieceWhite(piece)) ?? (IsPieceWhite(piece) ? "Q" : "q");
            }
            else
            {
                using PromotionForm promotionDialog = new(IsPieceWhite(piece), pieceImages);
                if (promotionDialog.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(promotionDialog.SelectedPiece))
                {
                    return false;
                }

                promotionPiece = promotionDialog.SelectedPiece;
            }
        }

        ExecuteMove(from, to, piece, promotionPiece, advanceImportedCursor);
        return true;
    }

    private void ExecuteMove(Point from, Point to, string piece, string? promotionPiece, bool advanceImportedCursor)
    {
        undoStack.Push(CaptureCurrentState());
        analysisArrows.Clear();
        analysisTargetSquare = null;

        string uciMove = BuildUciMove(from, to, promotionPiece);
        string? capturedPiece = board[to.X, to.Y];

        ApplyMoveToBoard(from, to, piece, promotionPiece);
        UpdateCastlingRights(from, to, piece, capturedPiece);

        whiteToMove = !whiteToMove;
        moveHistory.Add(uciMove);
        if (advanceImportedCursor)
        {
            importedMoveCursor++;
        }

        if (!suppressEngineRefresh)
        {
            RefreshEngineSuggestions();
            if (engine?.IsGameOver() == true)
            {
                MessageBox.Show("Game over. Stockfish reports no further legal continuation.", "Game Over", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
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
            GameReplayService replayService = new();
            IReadOnlyList<ReplayPly> replay = replayService.Replay(parsedGame);
            ResetGameState();
            importedGame = parsedGame;
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
        Invalidate();

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

        ImportedMove move = importedMoves[index];
        if (!TryResolveSan(move.San, out MoveCandidate candidate, out string? error))
        {
            if (showError)
            {
                MessageBox.Show($"Move {move.DisplayText} could not be applied.\n{error}", "Import PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        ExecuteMove(candidate.From, candidate.To, candidate.Piece, candidate.PromotionPiece, advanceImportedCursor: true);
        ClearSelection();
        return true;
    }

    private void UpdateExtendedControls()
    {
        if (importedMovesLabel is not null)
        {
            string players = importedGame is null
                ? string.Empty
                : $" | {importedGame.WhitePlayer ?? "White"} vs {importedGame.BlackPlayer ?? "Black"}";

            importedMovesLabel.Text = importedMoves.Count == 0
                ? "Imported moves: none"
                : $"Imported moves: {importedMoveCursor}/{importedMoves.Count} applied{players}";
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
        }
    }

    private static List<string> ParsePgnMoves(string pgnText)
    {
        return SanNotation.ParsePgnMoves(pgnText);
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

    private bool TryResolveSan(string san, out MoveCandidate candidate, out string? error)
    {
        string normalizedSan = SanNotation.NormalizeSan(san);
        List<MoveCandidate> legalMoves = GetAllLegalMoves(whiteToMove);
        List<MoveCandidate> generatedSanMatches = new();

        foreach (MoveCandidate move in legalMoves)
        {
            string generatedSan = GenerateSan(move, legalMoves);
            if (SanNotation.NormalizeSan(generatedSan) == normalizedSan)
            {
                generatedSanMatches.Add(move);
            }
        }

        if (generatedSanMatches.Count == 1)
        {
            candidate = generatedSanMatches[0];
            error = null;
            return true;
        }

        if (normalizedSan == "O-O" || normalizedSan == "O-O-O")
        {
            int rank = whiteToMove ? 7 : 0;
            int targetFile = normalizedSan == "O-O" ? 6 : 2;

            foreach (MoveCandidate move in legalMoves)
            {
                if (move.Piece == (whiteToMove ? "K" : "k")
                    && move.From == new Point(4, rank)
                    && move.To == new Point(targetFile, rank))
                {
                    candidate = move;
                    error = null;
                    return true;
                }
            }

            candidate = default;
            error = "Castling is not legal in the current position.";
            return false;
        }

        Match destinationMatch = Regex.Match(normalizedSan, @"([a-h][1-8])", RegexOptions.IgnoreCase);
        if (!destinationMatch.Success)
        {
            candidate = default;
            error = $"Could not read target square from SAN '{san}'.";
            return false;
        }

        Point target = ParseSquare(destinationMatch.Groups[1].Value);
        string sanWithoutSuffix = Regex.Replace(normalizedSan, @"[+#]+$", string.Empty);
        string sanWithoutPromotion = Regex.Replace(sanWithoutSuffix, @"=([QRBN])", string.Empty);

        string promotionPiece = string.Empty;
        Match promotionMatch = Regex.Match(normalizedSan, @"=([QRBN])");
        if (promotionMatch.Success)
        {
            promotionPiece = whiteToMove ? promotionMatch.Groups[1].Value : promotionMatch.Groups[1].Value.ToLowerInvariant();
        }

        bool isCapture = sanWithoutSuffix.Contains('x');
        char firstChar = sanWithoutSuffix[0];
        bool hasExplicitPiecePrefix = "KQRBNkqrbn".Contains(firstChar);
        char pieceLetter = hasExplicitPiecePrefix ? char.ToUpperInvariant(firstChar) : 'P';
        string moverPiece = whiteToMove
            ? pieceLetter.ToString()
            : pieceLetter == 'P' ? "p" : pieceLetter.ToString().ToLowerInvariant();

        int destinationIndex = sanWithoutPromotion.IndexOf(destinationMatch.Groups[1].Value, StringComparison.Ordinal);
        string prefix = destinationIndex > 0 ? sanWithoutPromotion[..destinationIndex] : string.Empty;
        prefix = prefix.Replace("x", string.Empty, StringComparison.Ordinal);
        if (pieceLetter != 'P')
        {
            prefix = prefix.Replace(pieceLetter.ToString(), string.Empty, StringComparison.Ordinal);
        }

        char? disambiguationFile = prefix.FirstOrDefault(c => c is >= 'a' and <= 'h');
        char? disambiguationRank = prefix.FirstOrDefault(c => c is >= '1' and <= '8');

        List<MoveCandidate> matches = new();
        foreach (MoveCandidate move in legalMoves)
        {
            if (move.Piece != moverPiece || move.To != target || move.IsCapture != isCapture)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece != promotionPiece)
            {
                continue;
            }

            if (string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece is not null)
            {
                continue;
            }

            if (disambiguationFile.HasValue && move.From.X != disambiguationFile.Value - 'a')
            {
                continue;
            }

            if (disambiguationRank.HasValue && 8 - move.From.Y != disambiguationRank.Value - '0')
            {
                continue;
            }

            matches.Add(move);
        }

        if (matches.Count == 1)
        {
            candidate = matches[0];
            error = null;
            return true;
        }

        if (matches.Count == 0 && !hasExplicitPiecePrefix)
        {
            List<MoveCandidate> implicitPieceMatches = new();
            foreach (MoveCandidate move in legalMoves)
            {
                if (move.To != target || move.IsCapture != isCapture)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece != promotionPiece)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(promotionPiece) && move.PromotionPiece is not null)
                {
                    continue;
                }

                if (disambiguationFile.HasValue && move.From.X != disambiguationFile.Value - 'a')
                {
                    continue;
                }

                if (disambiguationRank.HasValue && 8 - move.From.Y != disambiguationRank.Value - '0')
                {
                    continue;
                }

                implicitPieceMatches.Add(move);
            }

            if (implicitPieceMatches.Count == 1)
            {
                candidate = implicitPieceMatches[0];
                error = null;
                return true;
            }
        }

        candidate = default;
        error = matches.Count == 0
            ? $"No legal move matches SAN '{san}' in the current position."
            : $"SAN '{san}' is ambiguous in the current position.";
        return false;
    }

    private string GenerateSan(MoveCandidate move, List<MoveCandidate> legalMoves)
    {
        if (move.Piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(move.To.X - move.From.X) == 2)
        {
            string castleSan = move.To.X > move.From.X ? "O-O" : "O-O-O";
            return castleSan + GetCheckSuffix(move);
        }

        bool isPawn = move.Piece.Equals("P", StringComparison.OrdinalIgnoreCase);
        string piecePrefix = isPawn ? string.Empty : move.Piece.ToUpperInvariant();
        string disambiguation = GetSanDisambiguation(move, legalMoves, isPawn);
        string captureMarker = move.IsCapture ? "x" : string.Empty;
        string targetSquare = ToUCI(move.To);
        string promotion = move.PromotionPiece is null ? string.Empty : $"={move.PromotionPiece.ToUpperInvariant()}";

        return $"{piecePrefix}{disambiguation}{captureMarker}{targetSquare}{promotion}{GetCheckSuffix(move)}";
    }

    private string GetSanDisambiguation(MoveCandidate move, List<MoveCandidate> legalMoves, bool isPawn)
    {
        if (isPawn)
        {
            return move.IsCapture ? ((char)('a' + move.From.X)).ToString() : string.Empty;
        }

        List<MoveCandidate> conflicts = new();
        foreach (MoveCandidate candidate in legalMoves)
        {
            if (candidate.From == move.From)
            {
                continue;
            }

            if (candidate.Piece == move.Piece && candidate.To == move.To)
            {
                conflicts.Add(candidate);
            }
        }

        if (conflicts.Count == 0)
        {
            return string.Empty;
        }

        bool fileUnique = !conflicts.Any(candidate => candidate.From.X == move.From.X);
        bool rankUnique = !conflicts.Any(candidate => candidate.From.Y == move.From.Y);

        char file = (char)('a' + move.From.X);
        char rank = (char)('8' - move.From.Y);

        if (fileUnique)
        {
            return file.ToString();
        }

        if (rankUnique)
        {
            return rank.ToString();
        }

        return $"{file}{rank}";
    }

    private string GetCheckSuffix(MoveCandidate move)
    {
        GameStateSnapshot snapshot = CaptureCurrentState();

        string? capturedPiece = board[move.To.X, move.To.Y];
        ApplyMoveToBoard(move.From, move.To, move.Piece, move.PromotionPiece);
        UpdateCastlingRights(move.From, move.To, move.Piece, capturedPiece);
        whiteToMove = !whiteToMove;

        bool opponentInCheck = false;
        Point? opponentKing = FindKing(whiteToMove);
        if (opponentKing.HasValue)
        {
            opponentInCheck = IsSquareAttacked(opponentKing.Value, !whiteToMove);
        }

        bool opponentHasLegalMoves = GetAllLegalMoves(whiteToMove).Count > 0;
        RestoreState(snapshot);

        if (!opponentInCheck)
        {
            return string.Empty;
        }

        return opponentHasLegalMoves ? "+" : "#";
    }

    private List<MoveCandidate> GetAllLegalMoves(bool forWhite)
    {
        List<MoveCandidate> moves = new();
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece) || IsPieceWhite(piece) != forWhite)
                {
                    continue;
                }

                moves.AddRange(GetLegalMovesForPiece(new Point(x, y)));
            }
        }

        return moves;
    }

    private List<MoveCandidate> GetLegalMovesForPiece(Point from)
    {
        List<MoveCandidate> moves = new();
        string? piece = board[from.X, from.Y];
        if (string.IsNullOrEmpty(piece))
        {
            return moves;
        }

        for (int tx = 0; tx < GridSize; tx++)
        {
            for (int ty = 0; ty < GridSize; ty++)
            {
                Point to = new(tx, ty);
                if (!IsLegalMove(from, to, piece))
                {
                    continue;
                }

                bool isCapture = !string.IsNullOrEmpty(board[to.X, to.Y]);
                if (NeedsPromotion(piece, to))
                {
                    foreach (string promotionPiece in GetPromotionOptions(piece))
                    {
                        moves.Add(new MoveCandidate(from, to, piece, promotionPiece, isCapture));
                    }
                }
                else
                {
                    moves.Add(new MoveCandidate(from, to, piece, null, isCapture));
                }
            }
        }

        return moves;
    }

    private static IEnumerable<string> GetPromotionOptions(string piece)
    {
        bool white = IsPieceWhite(piece);
        yield return white ? "Q" : "q";
        yield return white ? "R" : "r";
        yield return white ? "B" : "b";
        yield return white ? "N" : "n";
    }

    private static Point ParseSquare(string square)
    {
        char file = char.ToLowerInvariant(square[0]);
        return new Point(file - 'a', 8 - (square[1] - '0'));
    }

    private static string? GetPromotionPieceFromSan(string san, bool isWhite)
    {
        Match match = Regex.Match(san, @"=([QRBN])", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string piece = match.Groups[1].Value.ToUpperInvariant();
        return isWhite ? piece : piece.ToLowerInvariant();
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
        importedMoveCursor = snapshot.ImportedMoveCursor;
    }

    private readonly record struct MoveCandidate(Point From, Point To, string Piece, string? PromotionPiece, bool IsCapture);
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
        int ImportedMoveCursor);
}
