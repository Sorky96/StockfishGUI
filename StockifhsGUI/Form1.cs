using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
    private string? enPassantTargetSquare;
    private int halfmoveClock;
    private int fullmoveNumber = 1;

    public Form1(bool rotate = false)
    {
        DoubleBuffered = true;
        ClientSize = new Size(TileSize * GridSize + SidePanelWidth + 20, TileSize * GridSize + 260);
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

                if (pieceMoveOptionTargetSquare.HasValue && pieceMoveOptionTargetSquare.Value.X == x && pieceMoveOptionTargetSquare.Value.Y == y)
                {
                    using SolidBrush previewBrush = new(Color.FromArgb(96, Color.DeepSkyBlue));
                    using Pen previewPen = new(Color.DeepSkyBlue, 3);
                    g.FillRectangle(previewBrush, drawX * TileSize + 4, drawY * TileSize + 4, TileSize - 8, TileSize - 8);
                    g.DrawRectangle(previewPen, drawX * TileSize + 4, drawY * TileSize + 4, TileSize - 8, TileSize - 8);
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

        if (!TryExecuteMove(from, to, piece, advanceImportedCursor: false))
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

        if (!TryCreateGameFromCurrentPosition(out ChessGame? game, out _) || game is null)
        {
            return;
        }

        selectedSquare = new Point(x, y);
        availableMoves.Clear();
        string fromSquare = ToUCI(selectedSquare.Value);
        List<LegalMoveInfo> movesForPiece = game.GetLegalMoves()
            .Where(move => move.FromSquare == fromSquare)
            .ToList();

        foreach (LegalMoveInfo move in movesForPiece)
        {
            if (TryParseUciSquare(move.ToSquare, out Point targetSquare) && !availableMoves.Contains(targetSquare))
            {
                availableMoves.Add(targetSquare);
            }
        }

        UpdateSelectedPieceMoveOptions(GetCurrentFen(), selectedSquare.Value, movesForPiece);
        Invalidate();
    }

    private void ClearSelection()
    {
        selectedSquare = null;
        availableMoves.Clear();
        ClearPieceMoveOptions();
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

    private bool IsSuggestionLegal(string move)
    {
        if (!TryCreateGameFromCurrentPosition(out ChessGame? game, out _) || game is null)
        {
            return false;
        }

        return game.GetLegalMoves().Any(legalMove => string.Equals(legalMove.Uci, move, StringComparison.OrdinalIgnoreCase));
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
