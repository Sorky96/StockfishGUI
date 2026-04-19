using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public partial class MainForm : MaterialForm, IImportedGamePlaybackHost, ITrackingWorkflowHost, IAnalysisNavigationHost, IBoardInteractionHost, IBoardPresentationHost
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
    private readonly ChessBoardControl boardSurface;
    private readonly ImportedGamePlaybackCoordinator importedPlayback;
    private readonly TrackingWorkflowCoordinator trackingWorkflow;
    private readonly AnalysisNavigationCoordinator analysisNavigation;
    private readonly BoardInteractionCoordinator boardInteraction;
    private readonly BoardPresentationCoordinator boardPresentation;
    private StockfishEngine? engine;
    private readonly List<string> moveHistory = new();
    private readonly MaterialLabel suggestionLabel;
    private readonly MaterialButton rotateButton;
    private readonly MaterialLabel evaluationLabel;
    private readonly Panel evaluationBarBackground;
    private readonly Panel evaluationBarFill;
    private readonly List<BoardArrow> bestMoveArrows = new();
    private readonly List<BoardArrow> analysisArrows = new();
    private Point? analysisTargetSquare;
    private readonly List<Point> availableMoves = new();
    private readonly Dictionary<string, Image> pieceImages = new();
    private EvaluationSummary? currentEvaluation;

    private readonly TableLayoutPanel sidebarLayout;
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

    public MainForm(bool rotate = false)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        ClientSize = new Size(DefaultTileSize * GridSize + MinimumSidePanelWidth + LayoutGap + LayoutMargin, DefaultTileSize * GridSize + 260);
        MinimumSize = SizeFromClientSize(new Size(MinTileSize * GridSize + MinimumSidePanelWidth + LayoutGap + LayoutMargin, MinTileSize * GridSize + 260));
        Text = "Manual Chess (Player vs Player)";
        rotateBoard = rotate;

        MaterialSkinManager materialSkinManager = MaterialSkinManager.Instance;
        materialSkinManager.AddFormToManage(this);
        materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
        materialSkinManager.ColorScheme = new ColorScheme(
            Primary.BlueGrey800, Primary.BlueGrey900,
            Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

        importedPlayback = new ImportedGamePlaybackCoordinator(this);
        trackingWorkflow = new TrackingWorkflowCoordinator(this);
        analysisNavigation = new AnalysisNavigationCoordinator(this);
        boardInteraction = new BoardInteractionCoordinator(this);
        boardPresentation = new BoardPresentationCoordinator(this);

        boardSurface = new ChessBoardControl
        {
            Location = Point.Empty,
            Size = new Size(DefaultTileSize * GridSize, DefaultTileSize * GridSize),
            BackColor = System.Drawing.Color.Transparent,
            TabStop = false
        };
        boardSurface.SquareClicked += (_, args) => HandleBoardSquareClick(args.Square);
        Controls.Add(boardSurface);

        suggestionLabel = new MaterialLabel
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 12),
            AutoEllipsis = true,
            Text = "Stockfish suggests:"
        };
        Controls.Add(suggestionLabel);

        evaluationLabel = new MaterialLabel
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

        rotateButton = new MaterialButton
        {
            Text = "Rotate Board",
            AutoSize = false,
            Size = new Size(120, 36)
        };
        rotateButton.Click += (_, _) =>
        {
            rotateBoard = !rotateBoard;
            InvalidateBoardSurface();
        };
        Controls.Add(rotateButton);

        sidebarLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        // Setup column styles
        sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        
        // Setup row styles
        for (int i = 0; i < 3; i++) sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Buttons 1-3
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Saved Analyses
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // Imported Label
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));  // Imported List
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f)); // Tracking Buttons
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Always on top
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f)); // Status Label
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Piece Label
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));  // Piece List

        Controls.Add(sidebarLayout);

        InitializeExtendedControls();
        Resize += (_, _) => UpdateResponsiveLayout();

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

    private void HandleBoardSquareClick(Point square) => boardInteraction.HandleBoardSquareClick(square);

    private void ClearSelection() => boardInteraction.ClearSelection();

    private void RefreshEngineSuggestions()
        => boardPresentation.RefreshEngineSuggestions();

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

    private void UpdateEvaluationDisplay(EvaluationSummary? evaluation)
        => boardPresentation.UpdateEvaluationDisplay(evaluation);

    private int BoardPixelSize => boardTileSize * GridSize;

    private void UpdateResponsiveLayout()
    {
        boardTileSize = CalculateBoardTileSize();

        int boardBottom = BoardPixelSize;
        int panelLeft = BoardPixelSize + LayoutGap;
        int panelWidth = Math.Max(MinimumSidePanelWidth, ClientSize.Width - panelLeft - LayoutMargin);

        int sidebarTop = 16;
        int sidebarHeight = ClientSize.Height - sidebarTop - LayoutMargin;
        sidebarLayout.SetBounds(panelLeft, sidebarTop, panelWidth, sidebarHeight);

        int buttonRowY = boardBottom + 8;
        int undoWidth = 88;
        boardSurface.SetBounds(0, 0, BoardPixelSize, BoardPixelSize);
        boardPresentation.InvalidateBoardSurface();
        undoButton?.SetBounds(BoardPixelSize - LayoutMargin - undoWidth, buttonRowY, undoWidth, 30);
        rotateButton.SetBounds(
            BoardPixelSize - LayoutMargin - undoWidth - 12 - rotateButton.Width,
            buttonRowY,
            rotateButton.Width,
            rotateButton.Height);
        
        // Trigger layout update for sidebar
        sidebarLayout.PerformLayout();

        int suggestionWidth = Math.Max(180, rotateButton.Left - LayoutMargin - 8);
        suggestionLabel.SetBounds(LayoutMargin, buttonRowY + 2, suggestionWidth, 24);
        evaluationLabel.SetBounds(LayoutMargin, boardBottom + 42, Math.Max(240, BoardPixelSize - (LayoutMargin * 2)), 22);
        evaluationBarBackground.SetBounds(LayoutMargin, boardBottom + 68, Math.Max(220, BoardPixelSize - (LayoutMargin * 2)), 16);
        evaluationBarFill.Height = evaluationBarBackground.ClientSize.Height;
        UpdateEvaluationDisplay(currentEvaluation);
        InvalidateBoardSurface();
    }

    private int CalculateBoardTileSize()
    {
        int widthDrivenTileSize = (ClientSize.Width - MinimumSidePanelWidth - LayoutGap - LayoutMargin) / GridSize;
        int heightDrivenTileSize = (ClientSize.Height - MinimumBottomPanelHeight) / GridSize;
        int targetTileSize = Math.Min(widthDrivenTileSize, heightDrivenTileSize);
        return Math.Clamp(targetTileSize, MinTileSize, MaxTileSize);
    }

    private void InvalidateBoardSurface()
        => boardPresentation.InvalidateBoardSurface();



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

    int IBoardInteractionHost.GridSize => GridSize;

    string?[,] IBoardInteractionHost.Board => board;

    Point? IBoardInteractionHost.SelectedSquare
    {
        get => selectedSquare;
        set => selectedSquare = value;
    }

    IList<Point> IBoardInteractionHost.AvailableMoves => availableMoves;

    bool IBoardInteractionHost.WhiteToMove => whiteToMove;

    bool IBoardInteractionHost.IsPieceWhite(string piece) => IsPieceWhite(piece);

    string IBoardInteractionHost.GetCurrentFen() => GetCurrentFen();

    string IBoardInteractionHost.ToUci(Point point) => ToUCI(point);

    bool IBoardInteractionHost.TryCreateGameFromCurrentPosition(out ChessGame? game, out string? error)
        => TryCreateGameFromCurrentPosition(out game, out error);

    bool IBoardInteractionHost.TryExecuteMove(Point from, Point to, string piece, bool advanceImportedCursor)
        => TryExecuteMove(from, to, piece, advanceImportedCursor);

    void IBoardInteractionHost.UpdateSelectedPieceMoveOptions(string currentFen, Point selectedPoint, IReadOnlyList<LegalMoveInfo> movesForPiece)
        => UpdateSelectedPieceMoveOptions(currentFen, selectedPoint, movesForPiece);

    void IBoardInteractionHost.ClearPieceMoveOptions() => ClearPieceMoveOptions();

    void IBoardInteractionHost.InvalidateBoardSurface() => InvalidateBoardSurface();

    ChessBoardControl IBoardPresentationHost.BoardSurface => boardSurface;

    string?[,] IBoardPresentationHost.Board => board;

    IDictionary<string, Image> IBoardPresentationHost.PieceImages => pieceImages;

    IList<BoardArrow> IBoardPresentationHost.BestMoveArrows => bestMoveArrows;

    IList<BoardArrow> IBoardPresentationHost.AnalysisArrows => analysisArrows;

    IList<Point> IBoardPresentationHost.AvailableMoves => availableMoves;

    MaterialLabel IBoardPresentationHost.SuggestionLabel => suggestionLabel;

    MaterialLabel IBoardPresentationHost.EvaluationLabel => evaluationLabel;

    Panel IBoardPresentationHost.EvaluationBarBackground => evaluationBarBackground;

    Panel IBoardPresentationHost.EvaluationBarFill => evaluationBarFill;

    StockfishEngine? IBoardPresentationHost.Engine => engine;

    EvaluationSummary? IBoardPresentationHost.CurrentEvaluation
    {
        get => currentEvaluation;
        set => currentEvaluation = value;
    }

    string IBoardPresentationHost.MissingEngineMessage => MissingEngineMessage;

    int IBoardPresentationHost.BoardTileSize => boardTileSize;

    bool IBoardPresentationHost.RotateBoard => rotateBoard;

    bool IBoardPresentationHost.WhiteToMove => whiteToMove;

    Point? IBoardPresentationHost.SelectedSquare => selectedSquare;

    Point? IBoardPresentationHost.AnalysisTargetSquare => analysisTargetSquare;

    Point? IBoardPresentationHost.PreviewTargetSquare => pieceMoveOptionTargetSquare;

    string IBoardPresentationHost.GetCurrentFen() => GetCurrentFen();

    bool IBoardPresentationHost.IsSuggestionLegal(string move) => IsSuggestionLegal(move);

    void IBoardPresentationHost.UpdateExtendedControls() => UpdateExtendedControls();
}
