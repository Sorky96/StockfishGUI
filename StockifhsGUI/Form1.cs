using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class Form1 : Form
{
    private const int TileSize = 64;
    private const int GridSize = 8;
    private const int SidePanelWidth = 300;
    private const string MissingEngineMessage = "Stockfish unavailable. Download stockfish.exe and place it next to the app.";

    private readonly string?[,] board = new string?[8, 8];
    private StockfishEngine? engine;
    private readonly List<string> moveHistory = new();
    private readonly Label suggestionLabel;
    private readonly Button rotateButton;
    private readonly Label evaluationLabel;
    private readonly Panel evaluationBarBackground;
    private readonly Panel evaluationBarFill;
    private readonly List<(Point from, Point to)> bestMoves = new();
    private readonly List<AnalysisArrow> analysisArrows = new();
    private Point? analysisTargetSquare;
    private readonly List<Point> availableMoves = new();
    private readonly Dictionary<string, Image> pieceImages = new();

    private Point? selectedSquare;
    private bool whiteToMove = true;
    private bool rotateBoard;

    private bool whiteKingMoved;
    private bool blackKingMoved;
    private bool whiteRookLeftMoved;
    private bool whiteRookRightMoved;
    private bool blackRookLeftMoved;
    private bool blackRookRightMoved;

    public Form1(bool rotate = false)
    {
        DoubleBuffered = true;
        ClientSize = new Size(TileSize * GridSize + SidePanelWidth + 20, TileSize * GridSize + 80);
        Text = "Manual Chess (Player vs Player)";
        rotateBoard = rotate;

        suggestionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(10, TileSize * GridSize + 5),
            Font = new Font("Segoe UI", 12),
            Text = "Stockfish suggests:"
        };
        Controls.Add(suggestionLabel);

        evaluationLabel = new Label
        {
            AutoSize = false,
            Location = new Point(10, TileSize * GridSize + 32),
            Size = new Size(250, 22),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Text = "Evaluation: even"
        };
        Controls.Add(evaluationLabel);

        evaluationBarBackground = new Panel
        {
            Location = new Point(10, TileSize * GridSize + 56),
            Size = new Size(250, 14),
            BackColor = Color.FromArgb(45, 45, 45),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(evaluationBarBackground);

        evaluationBarFill = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(125, 14),
            BackColor = Color.White
        };
        evaluationBarBackground.Controls.Add(evaluationBarFill);

        rotateButton = new Button
        {
            Text = "Rotate Board",
            Location = new Point(300, TileSize * GridSize + 5),
            Size = new Size(120, 30)
        };
        rotateButton.Click += (_, _) =>
        {
            rotateBoard = !rotateBoard;
            Invalidate();
        };
        Controls.Add(rotateButton);
        InitializeExtendedControls();

        MouseClick += Form1_MouseClick;
        Paint += Form1_Paint;

        ResetGameState();

        string enginePath = Path.Combine(AppContext.BaseDirectory, "stockfish.exe");
        LoadPieceImages();
        InitializeEngine(enginePath);
        RefreshEngineSuggestions();
        UpdateExtendedControls();
    }

    private void InitializeEngine(string enginePath)
    {
        try
        {
            StockfishEngine initializedEngine = new(enginePath);
            initializedEngine.SendCommand("setoption name MultiPV value 3");
            engine = initializedEngine;
        }
        catch (Exception)
        {
            engine = null;
        }
    }

    private void LoadStartingPosition()
    {
        Array.Clear(board, 0, board.Length);

        string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        string[] rows = fen.Split('/');

        for (int y = 0; y < GridSize; y++)
        {
            int x = 0;
            foreach (char c in rows[y])
            {
                if (char.IsDigit(c))
                {
                    x += (int)char.GetNumericValue(c);
                    continue;
                }

                board[x, y] = c.ToString();
                x++;
            }
        }
    }

    private void Form1_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using Font coordinateFont = new("Segoe UI", 10, FontStyle.Bold);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int drawX = rotateBoard ? 7 - x : x;
                int drawY = rotateBoard ? 7 - y : y;
                bool lightSquare = (x + y) % 2 == 0;
                Brush brush = lightSquare ? Brushes.Beige : Brushes.Brown;
                g.FillRectangle(brush, drawX * TileSize, drawY * TileSize, TileSize, TileSize);

                if (selectedSquare.HasValue && selectedSquare.Value.X == x && selectedSquare.Value.Y == y)
                {
                    g.DrawRectangle(Pens.Red, drawX * TileSize, drawY * TileSize, TileSize, TileSize);
                }

                if (analysisTargetSquare.HasValue && analysisTargetSquare.Value.X == x && analysisTargetSquare.Value.Y == y)
                {
                    using Pen analysisPen = new(Color.Gold, 4);
                    g.DrawRectangle(
                        analysisPen,
                        drawX * TileSize + 2,
                        drawY * TileSize + 2,
                        TileSize - 4,
                        TileSize - 4);
                }

                if (availableMoves.Contains(new Point(x, y)))
                {
                    g.FillEllipse(
                        Brushes.Gold,
                        drawX * TileSize + TileSize / 3,
                        drawY * TileSize + TileSize / 3,
                        TileSize / 3,
                        TileSize / 3);
                }

                string? piece = board[x, y];
                if (!string.IsNullOrEmpty(piece) && pieceImages.TryGetValue(piece, out Image? pieceImage))
                {
                    g.DrawImage(pieceImage, drawX * TileSize, drawY * TileSize, TileSize, TileSize);
                }

                DrawBoardCoordinates(g, coordinateFont, x, y, drawX, drawY, lightSquare);
            }
        }

        Color[] colors = { Color.Blue, Color.Green, Color.Orange };
        for (int i = 0; i < bestMoves.Count && i < colors.Length; i++)
        {
            DrawArrow(g, bestMoves[i].from, bestMoves[i].to, colors[i]);
        }

        foreach (AnalysisArrow arrow in analysisArrows)
        {
            DrawArrow(g, arrow.From, arrow.To, arrow.Color);
        }
    }

    private void DrawArrow(Graphics g, Point from, Point to, Color color)
    {
        using Pen arrowPen = new(color, 4)
        {
            CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(6, 6)
        };

        Point drawFrom = rotateBoard ? new Point(7 - from.X, 7 - from.Y) : from;
        Point drawTo = rotateBoard ? new Point(7 - to.X, 7 - to.Y) : to;

        PointF start = new(drawFrom.X * TileSize + TileSize / 2f, drawFrom.Y * TileSize + TileSize / 2f);
        PointF end = new(drawTo.X * TileSize + TileSize / 2f, drawTo.Y * TileSize + TileSize / 2f);

        g.DrawLine(arrowPen, start, end);
    }

    private void DrawBoardCoordinates(Graphics g, Font coordinateFont, int boardX, int boardY, int drawX, int drawY, bool lightSquare)
    {
        using SolidBrush textBrush = new(lightSquare ? Color.SaddleBrown : Color.Bisque);
        Rectangle squareRect = new(drawX * TileSize, drawY * TileSize, TileSize, TileSize);

        if (drawX == 0)
        {
            string rankLabel = (8 - boardY).ToString();
            g.DrawString(rankLabel, coordinateFont, textBrush, squareRect.Left + 3, squareRect.Top + 2);
        }

        if (drawY == GridSize - 1)
        {
            string fileLabel = ((char)('a' + boardX)).ToString();
            SizeF textSize = g.MeasureString(fileLabel, coordinateFont);
            g.DrawString(
                fileLabel,
                coordinateFont,
                textBrush,
                squareRect.Right - textSize.Width - 4,
                squareRect.Bottom - textSize.Height - 2);
        }
    }

    private void Form1_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.X >= TileSize * GridSize || e.Y >= TileSize * GridSize)
        {
            return;
        }

        int x = e.X / TileSize;
        int y = e.Y / TileSize;

        if (rotateBoard)
        {
            x = 7 - x;
            y = 7 - y;
        }

        if (!IsOnBoard(x, y))
        {
            return;
        }

        if (selectedSquare is null)
        {
            TrySelectPiece(x, y);
            return;
        }

        Point from = selectedSquare.Value;
        Point to = new(x, y);
        string? piece = board[from.X, from.Y];

        if (string.IsNullOrEmpty(piece))
        {
            ClearSelection();
            return;
        }

        if (from == to)
        {
            ClearSelection();
            return;
        }

        if (!TryExecuteMove(from, to, piece, importedSan: null, advanceImportedCursor: false))
        {
            SystemSounds.Beep.Play();
            ClearSelection();
            return;
        }

        ClearSelection();
    }

    private void TrySelectPiece(int x, int y)
    {
        string? piece = board[x, y];
        if (string.IsNullOrEmpty(piece) || IsPieceWhite(piece) != whiteToMove)
        {
            return;
        }

        selectedSquare = new Point(x, y);
        availableMoves.Clear();
        foreach (MoveCandidate move in GetLegalMovesForPiece(selectedSquare.Value))
        {
            availableMoves.Add(move.To);
        }

        Invalidate();
    }

    private void ClearSelection()
    {
        selectedSquare = null;
        availableMoves.Clear();
        Invalidate();
    }

    private void RefreshEngineSuggestions()
    {
        if (engine is null)
        {
            bestMoves.Clear();
            suggestionLabel.Text = MissingEngineMessage;
            UpdateEvaluationDisplay(null);
            UpdateExtendedControls();
            Invalidate();
            return;
        }

        engine.SetPositionFen(GetCurrentFen());

        bestMoves.Clear();
        List<string> topMoves = engine.GetTopMoves(3);
        topMoves.RemoveAll(move => !IsSuggestionLegal(move));

        foreach (string move in topMoves)
        {
            Point from = new(move[0] - 'a', 8 - (move[1] - '0'));
            Point to = new(move[2] - 'a', 8 - (move[3] - '0'));
            bestMoves.Add((from, to));
        }

        suggestionLabel.Text = topMoves.Count == 0
            ? "Top moves: none"
            : "Top moves: " + string.Join(", ", topMoves);
        UpdateEvaluationDisplay(engine.GetEvaluationSummary());
        UpdateExtendedControls();
    }

    private string BuildUciMove(Point from, Point to, string? promotionPiece)
    {
        string move = $"{ToUCI(from)}{ToUCI(to)}";
        if (!string.IsNullOrEmpty(promotionPiece))
        {
            move += promotionPiece.ToLowerInvariant();
        }

        return move;
    }

    private string ToUCI(Point p) => $"{(char)('a' + p.X)}{8 - p.Y}";

    private bool NeedsPromotion(string piece, Point to)
    {
        return piece.Equals("P", StringComparison.Ordinal) && to.Y == 0
            || piece.Equals("p", StringComparison.Ordinal) && to.Y == 7;
    }

    private bool IsLegalMove(Point from, Point to, string piece)
    {
        if (from == to || !IsOnBoard(from.X, from.Y) || !IsOnBoard(to.X, to.Y))
        {
            return false;
        }

        string? currentPiece = board[from.X, from.Y];
        if (!string.Equals(currentPiece, piece, StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsPseudoLegalMove(from, to, piece))
        {
            return false;
        }

        return !WouldLeaveKingInCheck(from, to, piece);
    }

    private bool IsPseudoLegalMove(Point from, Point to, string piece)
    {
        string? target = board[to.X, to.Y];
        if (!string.IsNullOrEmpty(target) && IsPieceWhite(target) == IsPieceWhite(piece))
        {
            return false;
        }

        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        switch (piece.ToLowerInvariant())
        {
            case "k":
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                {
                    return true;
                }

                return dy == 0 && Math.Abs(dx) == 2 && CanCastle(from, to, IsPieceWhite(piece));

            case "p":
                int direction = IsPieceWhite(piece) ? -1 : 1;
                int startRow = IsPieceWhite(piece) ? 6 : 1;

                if (dx == 0 && dy == direction && string.IsNullOrEmpty(target))
                {
                    return true;
                }

                if (dx == 0 && dy == 2 * direction && from.Y == startRow
                    && string.IsNullOrEmpty(target)
                    && string.IsNullOrEmpty(board[from.X, from.Y + direction]))
                {
                    return true;
                }

                if (Math.Abs(dx) == 1 && dy == direction && !string.IsNullOrEmpty(target)
                    && IsPieceWhite(target) != IsPieceWhite(piece))
                {
                    return true;
                }

                return false;

            case "r":
                return (dx == 0 || dy == 0) && IsPathClear(from, to);

            case "n":
                return (Math.Abs(dx) == 2 && Math.Abs(dy) == 1)
                    || (Math.Abs(dx) == 1 && Math.Abs(dy) == 2);

            case "b":
                return Math.Abs(dx) == Math.Abs(dy) && IsPathClear(from, to);

            case "q":
                return (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && IsPathClear(from, to);

            default:
                return false;
        }
    }

    private bool CanCastle(Point from, Point to, bool isWhite)
    {
        int homeRow = isWhite ? 7 : 0;
        if (from.Y != homeRow || from.X != 4 || to.Y != homeRow)
        {
            return false;
        }

        if (IsSquareAttacked(from, !isWhite))
        {
            return false;
        }

        if (to.X == 6)
        {
            if (isWhite ? whiteKingMoved || whiteRookRightMoved : blackKingMoved || blackRookRightMoved)
            {
                return false;
            }

            string? rook = board[7, homeRow];
            if (rook != (isWhite ? "R" : "r"))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(board[5, homeRow]) || !string.IsNullOrEmpty(board[6, homeRow]))
            {
                return false;
            }

            return !IsSquareAttacked(new Point(5, homeRow), !isWhite)
                && !IsSquareAttacked(new Point(6, homeRow), !isWhite);
        }

        if (to.X == 2)
        {
            if (isWhite ? whiteKingMoved || whiteRookLeftMoved : blackKingMoved || blackRookLeftMoved)
            {
                return false;
            }

            string? rook = board[0, homeRow];
            if (rook != (isWhite ? "R" : "r"))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(board[1, homeRow])
                || !string.IsNullOrEmpty(board[2, homeRow])
                || !string.IsNullOrEmpty(board[3, homeRow]))
            {
                return false;
            }

            return !IsSquareAttacked(new Point(3, homeRow), !isWhite)
                && !IsSquareAttacked(new Point(2, homeRow), !isWhite);
        }

        return false;
    }

    private bool WouldLeaveKingInCheck(Point from, Point to, string piece)
    {
        string? originalTarget = board[to.X, to.Y];
        string? originalFrom = board[from.X, from.Y];
        string? rookOriginalFrom = null;
        string? rookOriginalTo = null;
        Point? rookFrom = null;
        Point? rookTo = null;

        bool isCastling = piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(to.X - from.X) == 2;
        if (isCastling)
        {
            rookFrom = to.X > from.X ? new Point(7, from.Y) : new Point(0, from.Y);
            rookTo = to.X > from.X ? new Point(5, from.Y) : new Point(3, from.Y);
            rookOriginalFrom = board[rookFrom.Value.X, rookFrom.Value.Y];
            rookOriginalTo = board[rookTo.Value.X, rookTo.Value.Y];
        }

        board[from.X, from.Y] = null;
        board[to.X, to.Y] = piece;

        if (isCastling && rookFrom.HasValue && rookTo.HasValue)
        {
            board[rookTo.Value.X, rookTo.Value.Y] = board[rookFrom.Value.X, rookFrom.Value.Y];
            board[rookFrom.Value.X, rookFrom.Value.Y] = null;
        }

        Point? kingPosition = FindKing(IsPieceWhite(piece));
        bool inCheck = kingPosition is null || IsSquareAttacked(kingPosition.Value, !IsPieceWhite(piece));

        board[from.X, from.Y] = originalFrom;
        board[to.X, to.Y] = originalTarget;

        if (isCastling && rookFrom.HasValue && rookTo.HasValue)
        {
            board[rookFrom.Value.X, rookFrom.Value.Y] = rookOriginalFrom;
            board[rookTo.Value.X, rookTo.Value.Y] = rookOriginalTo;
        }

        return inCheck;
    }

    private Point? FindKing(bool whiteKing)
    {
        string king = whiteKing ? "K" : "k";
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (board[x, y] == king)
                {
                    return new Point(x, y);
                }
            }
        }

        return null;
    }

    private bool IsSquareAttacked(Point square, bool byWhite)
    {
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece) || IsPieceWhite(piece) != byWhite)
                {
                    continue;
                }

                Point from = new(x, y);
                if (AttacksSquare(from, square, piece))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool AttacksSquare(Point from, Point to, string piece)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        switch (piece.ToLowerInvariant())
        {
            case "p":
                int direction = IsPieceWhite(piece) ? -1 : 1;
                return Math.Abs(dx) == 1 && dy == direction;

            case "n":
                return (Math.Abs(dx) == 2 && Math.Abs(dy) == 1)
                    || (Math.Abs(dx) == 1 && Math.Abs(dy) == 2);

            case "b":
                return Math.Abs(dx) == Math.Abs(dy) && IsPathClear(from, to);

            case "r":
                return (dx == 0 || dy == 0) && IsPathClear(from, to);

            case "q":
                return (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && IsPathClear(from, to);

            case "k":
                return Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1;

            default:
                return false;
        }
    }

    private void ApplyMoveToBoard(Point from, Point to, string piece, string? promotionPiece)
    {
        board[from.X, from.Y] = null;

        if (piece.Equals("K", StringComparison.OrdinalIgnoreCase) && Math.Abs(to.X - from.X) == 2)
        {
            if (to.X > from.X)
            {
                board[5, from.Y] = board[7, from.Y];
                board[7, from.Y] = null;
            }
            else
            {
                board[3, from.Y] = board[0, from.Y];
                board[0, from.Y] = null;
            }
        }

        board[to.X, to.Y] = promotionPiece ?? piece;
    }

    private void UpdateCastlingRights(Point from, Point to, string movingPiece, string? capturedPiece)
    {
        switch (movingPiece)
        {
            case "K":
                whiteKingMoved = true;
                break;
            case "k":
                blackKingMoved = true;
                break;
            case "R":
                if (from == new Point(0, 7))
                {
                    whiteRookLeftMoved = true;
                }
                else if (from == new Point(7, 7))
                {
                    whiteRookRightMoved = true;
                }
                break;
            case "r":
                if (from == new Point(0, 0))
                {
                    blackRookLeftMoved = true;
                }
                else if (from == new Point(7, 0))
                {
                    blackRookRightMoved = true;
                }
                break;
        }

        switch (capturedPiece)
        {
            case "R":
                if (to == new Point(0, 7))
                {
                    whiteRookLeftMoved = true;
                }
                else if (to == new Point(7, 7))
                {
                    whiteRookRightMoved = true;
                }
                break;
            case "r":
                if (to == new Point(0, 0))
                {
                    blackRookLeftMoved = true;
                }
                else if (to == new Point(7, 0))
                {
                    blackRookRightMoved = true;
                }
                break;
        }
    }

    private bool IsPathClear(Point from, Point to)
    {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);

        int x = from.X + dx;
        int y = from.Y + dy;

        while (x != to.X || y != to.Y)
        {
            if (!string.IsNullOrEmpty(board[x, y]))
            {
                return false;
            }

            x += dx;
            y += dy;
        }

        return true;
    }

    private bool IsSuggestionLegal(string move)
    {
        if (move.Length < 4)
        {
            return false;
        }

        int fx = move[0] - 'a';
        int fy = 8 - (move[1] - '0');
        int tx = move[2] - 'a';
        int ty = 8 - (move[3] - '0');

        if (!IsOnBoard(fx, fy) || !IsOnBoard(tx, ty))
        {
            return false;
        }

        string? piece = board[fx, fy];
        return !string.IsNullOrEmpty(piece) && IsPieceWhite(piece) == whiteToMove
            && IsLegalMove(new Point(fx, fy), new Point(tx, ty), piece);
    }

    private static bool IsPieceWhite(string piece) => char.IsUpper(piece[0]);

    private static bool IsOnBoard(int x, int y) => x >= 0 && x < GridSize && y >= 0 && y < GridSize;

    private void UpdateEvaluationDisplay(EvaluationSummary? evaluation)
    {
        if (evaluation is null)
        {
            evaluationLabel.Text = "Evaluation: unavailable";
            evaluationBarFill.Width = evaluationBarBackground.ClientSize.Width / 2;
            evaluationBarFill.BackColor = Color.Silver;
            return;
        }

        if (evaluation.MateIn is int mateIn)
        {
            int signedMate = whiteToMove ? mateIn : -mateIn;
            bool whiteWinning = signedMate > 0;
            evaluationLabel.Text = whiteWinning
                ? $"Evaluation: White mates in {Math.Abs(signedMate)}"
                : $"Evaluation: Black mates in {Math.Abs(signedMate)}";
            evaluationBarFill.Width = whiteWinning ? evaluationBarBackground.ClientSize.Width : 0;
            evaluationBarFill.BackColor = whiteWinning ? Color.WhiteSmoke : Color.FromArgb(30, 30, 30);
            return;
        }

        int cp = evaluation.Centipawns ?? 0;
        int whitePerspectiveCp = whiteToMove ? cp : -cp;
        double pawnAdvantage = whitePerspectiveCp / 100.0;
        double normalized = Math.Clamp((pawnAdvantage + 5.0) / 10.0, 0.0, 1.0);

        evaluationBarFill.Width = Math.Max(0, (int)Math.Round(evaluationBarBackground.ClientSize.Width * normalized));
        evaluationBarFill.BackColor = whitePerspectiveCp >= 0 ? Color.WhiteSmoke : Color.FromArgb(30, 30, 30);

        if (Math.Abs(pawnAdvantage) < 0.15)
        {
            evaluationLabel.Text = "Evaluation: even";
        }
        else if (pawnAdvantage > 0)
        {
            evaluationLabel.Text = $"Evaluation: White +{pawnAdvantage:F1}";
        }
        else
        {
            evaluationLabel.Text = $"Evaluation: Black +{Math.Abs(pawnAdvantage):F1}";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        trackingTimer?.Stop();
        engine?.Dispose();
        foreach (Image image in pieceImages.Values)
        {
            image.Dispose();
        }

        base.OnFormClosed(e);
    }

    private void LoadPieceImages()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Images");
        pieceImages["K"] = Image.FromFile(Path.Combine(path, "wK.svg"));
        pieceImages["Q"] = Image.FromFile(Path.Combine(path, "wQ.svg"));
        pieceImages["R"] = Image.FromFile(Path.Combine(path, "wR.svg"));
        pieceImages["B"] = Image.FromFile(Path.Combine(path, "wB.svg"));
        pieceImages["N"] = Image.FromFile(Path.Combine(path, "wN.svg"));
        pieceImages["P"] = Image.FromFile(Path.Combine(path, "wP.svg"));
        pieceImages["k"] = Image.FromFile(Path.Combine(path, "bK.svg"));
        pieceImages["q"] = Image.FromFile(Path.Combine(path, "bQ.svg"));
        pieceImages["r"] = Image.FromFile(Path.Combine(path, "bR.svg"));
        pieceImages["b"] = Image.FromFile(Path.Combine(path, "bB.svg"));
        pieceImages["n"] = Image.FromFile(Path.Combine(path, "bN.svg"));
        pieceImages["p"] = Image.FromFile(Path.Combine(path, "bP.svg"));
    }

    private readonly record struct AnalysisArrow(Point From, Point To, Color Color);
}
