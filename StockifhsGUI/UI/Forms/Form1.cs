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
    private const int GridSize = 8;
    private const int DefaultTileSize = 64;
    private const int MinTileSize = 56;
    private const int MaxTileSize = 88;
    private const int MinimumSidePanelWidth = 320;
    private const int LayoutMargin = 10;
    private const int LayoutGap = 18;
    private const int MinimumBottomPanelHeight = 118;
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
    private EvaluationSummary? currentEvaluation;

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
    private int boardTileSize = DefaultTileSize;

    public Form1(bool rotate = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        DoubleBuffered = true;
        ResizeRedraw = true;
        ClientSize = new Size(DefaultTileSize * GridSize + MinimumSidePanelWidth + LayoutGap + LayoutMargin, DefaultTileSize * GridSize + 260);
        MinimumSize = SizeFromClientSize(new Size(MinTileSize * GridSize + MinimumSidePanelWidth + LayoutGap + LayoutMargin, MinTileSize * GridSize + 260));
        Text = "Manual Chess (Player vs Player)";
        rotateBoard = rotate;

        suggestionLabel = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 12),
            AutoEllipsis = true,
            Text = "Stockfish suggests:"
        };
        Controls.Add(suggestionLabel);

        evaluationLabel = new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Text = "Evaluation: even"
        };
        Controls.Add(evaluationLabel);

        evaluationBarBackground = new Panel
        {
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
            Size = new Size(120, 30)
        };
        rotateButton.Click += (_, _) =>
        {
            rotateBoard = !rotateBoard;
            InvalidateBoardSurface();
        };
        Controls.Add(rotateButton);
        InitializeExtendedControls();
        Resize += (_, _) => UpdateResponsiveLayout();

        MouseClick += Form1_MouseClick;
        Paint += Form1_Paint;

        ResetGameState();

        string enginePath = Path.Combine(AppContext.BaseDirectory, "stockfish.exe");
        LoadPieceImages();
        InitializeEngine(enginePath);
        RefreshEngineSuggestions();
        UpdateResponsiveLayout();
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
        using Font coordinateFont = new("Segoe UI", Math.Max(9, boardTileSize / 6f), FontStyle.Bold);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int drawX = rotateBoard ? 7 - x : x;
                int drawY = rotateBoard ? 7 - y : y;
                bool lightSquare = (x + y) % 2 == 0;
                Brush brush = lightSquare ? Brushes.Beige : Brushes.Brown;
                g.FillRectangle(brush, drawX * boardTileSize, drawY * boardTileSize, boardTileSize, boardTileSize);

                if (selectedSquare.HasValue && selectedSquare.Value.X == x && selectedSquare.Value.Y == y)
                {
                    g.DrawRectangle(Pens.Red, drawX * boardTileSize, drawY * boardTileSize, boardTileSize, boardTileSize);
                }

                if (analysisTargetSquare.HasValue && analysisTargetSquare.Value.X == x && analysisTargetSquare.Value.Y == y)
                {
                    using Pen analysisPen = new(Color.Gold, 4);
                    g.DrawRectangle(
                        analysisPen,
                        drawX * boardTileSize + 2,
                        drawY * boardTileSize + 2,
                        boardTileSize - 4,
                        boardTileSize - 4);
                }

                if (pieceMoveOptionTargetSquare.HasValue && pieceMoveOptionTargetSquare.Value.X == x && pieceMoveOptionTargetSquare.Value.Y == y)
                {
                    using SolidBrush previewBrush = new(Color.FromArgb(96, Color.DeepSkyBlue));
                    using Pen previewPen = new(Color.DeepSkyBlue, 3);
                    g.FillRectangle(previewBrush, drawX * boardTileSize + 4, drawY * boardTileSize + 4, boardTileSize - 8, boardTileSize - 8);
                    g.DrawRectangle(previewPen, drawX * boardTileSize + 4, drawY * boardTileSize + 4, boardTileSize - 8, boardTileSize - 8);
                }

                if (availableMoves.Contains(new Point(x, y)))
                {
                    g.FillEllipse(
                        Brushes.Gold,
                        drawX * boardTileSize + boardTileSize / 3,
                        drawY * boardTileSize + boardTileSize / 3,
                        boardTileSize / 3,
                        boardTileSize / 3);
                }

                string? piece = board[x, y];
                if (!string.IsNullOrEmpty(piece) && pieceImages.TryGetValue(piece, out Image? pieceImage))
                {
                    g.DrawImage(pieceImage, drawX * boardTileSize, drawY * boardTileSize, boardTileSize, boardTileSize);
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

        PointF start = new(drawFrom.X * boardTileSize + boardTileSize / 2f, drawFrom.Y * boardTileSize + boardTileSize / 2f);
        PointF end = new(drawTo.X * boardTileSize + boardTileSize / 2f, drawTo.Y * boardTileSize + boardTileSize / 2f);

        g.DrawLine(arrowPen, start, end);
    }

    private void DrawBoardCoordinates(Graphics g, Font coordinateFont, int boardX, int boardY, int drawX, int drawY, bool lightSquare)
    {
        using SolidBrush textBrush = new(lightSquare ? Color.SaddleBrown : Color.Bisque);
        Rectangle squareRect = new(drawX * boardTileSize, drawY * boardTileSize, boardTileSize, boardTileSize);

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
        if (e.X >= BoardPixelSize || e.Y >= BoardPixelSize)
        {
            return;
        }

        int x = e.X / boardTileSize;
        int y = e.Y / boardTileSize;

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
        InvalidateBoardSurface();
    }

    private void ClearSelection()
    {
        selectedSquare = null;
        availableMoves.Clear();
        ClearPieceMoveOptions();
        InvalidateBoardSurface();
    }

    private void RefreshEngineSuggestions()
    {
        if (engine is null)
        {
            bestMoves.Clear();
            suggestionLabel.Text = MissingEngineMessage;
            UpdateEvaluationDisplay(null);
            UpdateExtendedControls();
            InvalidateBoardSurface();
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
        InvalidateBoardSurface();
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
        currentEvaluation = evaluation;

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

    private int BoardPixelSize => boardTileSize * GridSize;
    private Rectangle BoardBounds => new(0, 0, BoardPixelSize, BoardPixelSize);

    private void UpdateResponsiveLayout()
    {
        boardTileSize = CalculateBoardTileSize();

        int boardBottom = BoardPixelSize;
        int panelLeft = BoardPixelSize + LayoutGap;
        int panelWidth = Math.Max(MinimumSidePanelWidth, ClientSize.Width - panelLeft - LayoutMargin);
        int buttonHeight = 32;
        int buttonGap = 10;
        int buttonWidth = Math.Max(140, (panelWidth - buttonGap) / 2);
        int secondColumnLeft = panelLeft + buttonWidth + buttonGap;

        importPgnButton?.SetBounds(panelLeft, 16, buttonWidth, buttonHeight);
        loadSavedGamesButton?.SetBounds(secondColumnLeft, 16, buttonWidth, buttonHeight);
        applyNextImportedButton?.SetBounds(panelLeft, 16 + buttonHeight + buttonGap, buttonWidth, buttonHeight);
        applySelectedImportedButton?.SetBounds(secondColumnLeft, 16 + buttonHeight + buttonGap, buttonWidth, buttonHeight);
        analyzeImportedButton?.SetBounds(panelLeft, 16 + ((buttonHeight + buttonGap) * 2), buttonWidth, buttonHeight);
        playerProfilesButton?.SetBounds(secondColumnLeft, 16 + ((buttonHeight + buttonGap) * 2), buttonWidth, buttonHeight);

        int buttonRow4Y = 16 + ((buttonHeight + buttonGap) * 3);
        savedAnalysesButton?.SetBounds(panelLeft, buttonRow4Y, panelWidth, buttonHeight);

        int rightColumnY = buttonRow4Y + buttonHeight + 10;
        importedMovesLabel?.SetBounds(panelLeft, rightColumnY, panelWidth, 54);
        rightColumnY += 60;

        int trackingSectionHeight = 146;
        int pieceOptionsLabelHeight = 42;
        int minimumPieceOptionsListHeight = 150;
        int importedListHeight = Math.Max(
            170,
            ClientSize.Height - rightColumnY - LayoutMargin - trackingSectionHeight - pieceOptionsLabelHeight - minimumPieceOptionsListHeight - 20);
        importedMovesList?.SetBounds(panelLeft, rightColumnY, panelWidth, importedListHeight);
        rightColumnY += importedListHeight + 16;

        startTrackingButton?.SetBounds(panelLeft, rightColumnY, buttonWidth, buttonHeight);
        stopTrackingButton?.SetBounds(secondColumnLeft, rightColumnY, buttonWidth, buttonHeight);
        rightColumnY += buttonHeight + 10;

        alwaysOnTopCheckBox?.SetBounds(panelLeft, rightColumnY, panelWidth, 24);
        rightColumnY += 28;

        trackingStatusLabel?.SetBounds(panelLeft, rightColumnY, panelWidth, 72);
        rightColumnY += 84;

        pieceMoveOptionsLabel?.SetBounds(panelLeft, rightColumnY, panelWidth, pieceOptionsLabelHeight);
        rightColumnY += pieceOptionsLabelHeight + 6;
        pieceMoveOptionsList?.SetBounds(panelLeft, rightColumnY, panelWidth, Math.Max(minimumPieceOptionsListHeight, ClientSize.Height - rightColumnY - LayoutMargin));

        int buttonRowY = boardBottom + 8;
        int undoWidth = 88;
        undoButton?.SetBounds(BoardPixelSize - LayoutMargin - undoWidth, buttonRowY, undoWidth, 30);
        rotateButton.SetBounds(
            BoardPixelSize - LayoutMargin - undoWidth - 12 - rotateButton.Width,
            buttonRowY,
            rotateButton.Width,
            rotateButton.Height);

        int suggestionWidth = Math.Max(180, rotateButton.Left - LayoutMargin - 8);
        suggestionLabel.SetBounds(LayoutMargin, buttonRowY + 2, suggestionWidth, 24);
        evaluationLabel.SetBounds(LayoutMargin, boardBottom + 42, Math.Max(240, BoardPixelSize - (LayoutMargin * 2)), 22);
        evaluationBarBackground.SetBounds(LayoutMargin, boardBottom + 68, Math.Max(220, BoardPixelSize - (LayoutMargin * 2)), 16);
        evaluationBarFill.Height = evaluationBarBackground.ClientSize.Height;
        UpdateEvaluationDisplay(currentEvaluation);
        Invalidate();
    }

    private int CalculateBoardTileSize()
    {
        int widthDrivenTileSize = (ClientSize.Width - MinimumSidePanelWidth - LayoutGap - LayoutMargin) / GridSize;
        int heightDrivenTileSize = (ClientSize.Height - MinimumBottomPanelHeight) / GridSize;
        int targetTileSize = Math.Min(widthDrivenTileSize, heightDrivenTileSize);
        return Math.Clamp(targetTileSize, MinTileSize, MaxTileSize);
    }

    private void InvalidateBoardSurface()
    {
        Invalidate(BoardBounds);
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
