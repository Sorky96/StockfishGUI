using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using StockifhsGUI.Avalonia.ViewModels;

namespace StockifhsGUI.Avalonia.Controls;

public sealed class ChessBoardView : Control
{
    public static readonly StyledProperty<string?> FenProperty =
        AvaloniaProperty.Register<ChessBoardView, string?>(nameof(Fen));

    public static readonly StyledProperty<bool> RotateBoardProperty =
        AvaloniaProperty.Register<ChessBoardView, bool>(nameof(RotateBoard));

    public static readonly StyledProperty<string?> SelectedSquareProperty =
        AvaloniaProperty.Register<ChessBoardView, string?>(nameof(SelectedSquare));

    public static readonly StyledProperty<string?> PreviewTargetSquareProperty =
        AvaloniaProperty.Register<ChessBoardView, string?>(nameof(PreviewTargetSquare));

    public static readonly StyledProperty<IReadOnlyList<string>> AvailableMovesProperty =
        AvaloniaProperty.Register<ChessBoardView, IReadOnlyList<string>>(nameof(AvailableMoves), Array.Empty<string>());

    public static readonly StyledProperty<IReadOnlyList<BoardArrowViewModel>> ArrowsProperty =
        AvaloniaProperty.Register<ChessBoardView, IReadOnlyList<BoardArrowViewModel>>(nameof(Arrows), Array.Empty<BoardArrowViewModel>());

    public static readonly RoutedEvent<BoardSquarePressedEventArgs> SquarePressedEvent =
        RoutedEvent.Register<ChessBoardView, BoardSquarePressedEventArgs>(nameof(SquarePressed), RoutingStrategies.Bubble);

    static ChessBoardView()
    {
        AffectsRender<ChessBoardView>(
            FenProperty,
            RotateBoardProperty,
            SelectedSquareProperty,
            PreviewTargetSquareProperty,
            AvailableMovesProperty,
            ArrowsProperty);
    }

    public event EventHandler<BoardSquarePressedEventArgs>? SquarePressed
    {
        add => AddHandler(SquarePressedEvent, value);
        remove => RemoveHandler(SquarePressedEvent, value);
    }

    public string? Fen
    {
        get => GetValue(FenProperty);
        set => SetValue(FenProperty, value);
    }

    public bool RotateBoard
    {
        get => GetValue(RotateBoardProperty);
        set => SetValue(RotateBoardProperty, value);
    }

    public string? SelectedSquare
    {
        get => GetValue(SelectedSquareProperty);
        set => SetValue(SelectedSquareProperty, value);
    }

    public string? PreviewTargetSquare
    {
        get => GetValue(PreviewTargetSquareProperty);
        set => SetValue(PreviewTargetSquareProperty, value);
    }

    public IReadOnlyList<string> AvailableMoves
    {
        get => GetValue(AvailableMovesProperty);
        set => SetValue(AvailableMovesProperty, value);
    }

    public IReadOnlyList<BoardArrowViewModel> Arrows
    {
        get => GetValue(ArrowsProperty);
        set => SetValue(ArrowsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = new(Bounds.Size);
        double tileSize = Math.Min(bounds.Width, bounds.Height) / 8d;
        if (tileSize <= 0)
        {
            return;
        }

        context.FillRectangle(new SolidColorBrush(Color.Parse("#EAEFF1")), bounds);

        FenPosition? position = null;
        if (!string.IsNullOrWhiteSpace(Fen)
            && FenPosition.TryParse(Fen, out FenPosition? parsedPosition, out _))
        {
            position = parsedPosition;
        }

        for (int boardY = 0; boardY < 8; boardY++)
        {
            for (int boardX = 0; boardX < 8; boardX++)
            {
                Point drawPoint = ToDrawPoint(boardX, boardY);
                Rect tileRect = new(drawPoint.X * tileSize, drawPoint.Y * tileSize, tileSize, tileSize);
                bool lightSquare = (boardX + boardY) % 2 == 0;
                Color squareColor = lightSquare ? Color.Parse("#F1ECCB") : Color.Parse("#B22B2B");
                context.FillRectangle(new SolidColorBrush(squareColor), tileRect);

                string squareName = ToSquareName(boardX, boardY);
                if (string.Equals(squareName, SelectedSquare, StringComparison.OrdinalIgnoreCase))
                {
                    context.DrawRectangle(new Pen(Brushes.Gold, 3), tileRect.Deflate(2));
                }

                if (string.Equals(squareName, PreviewTargetSquare, StringComparison.OrdinalIgnoreCase))
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(110, 30, 144, 255)), tileRect.Deflate(4));
                    context.DrawRectangle(new Pen(Brushes.DeepSkyBlue, 2), tileRect.Deflate(4));
                }

                if (AvailableMoves.Any(move => string.Equals(move, squareName, StringComparison.OrdinalIgnoreCase)))
                {
                    double markerSize = tileSize / 3d;
                    Rect markerRect = new(
                        tileRect.X + ((tileSize - markerSize) / 2d),
                        tileRect.Y + ((tileSize - markerSize) / 2d),
                        markerSize,
                        markerSize);
                    context.DrawEllipse(Brushes.Gold, null, markerRect.Center, markerRect.Width / 2d, markerRect.Height / 2d);
                }

                if (position is not null)
                {
                    string? piece = position.Board[boardX, boardY];
                    if (!string.IsNullOrEmpty(piece))
                    {
                        DrawPieceGlyph(context, piece, tileRect);
                    }
                }

                DrawCoordinates(context, boardX, boardY, tileRect, lightSquare, tileSize);
            }
        }

        foreach (BoardArrowViewModel arrow in Arrows)
        {
            DrawArrow(context, arrow, tileSize);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        double tileSize = Math.Min(Bounds.Width, Bounds.Height) / 8d;
        if (tileSize <= 0)
        {
            return;
        }

        Point point = e.GetPosition(this);
        if (point.X < 0 || point.Y < 0 || point.X >= tileSize * 8 || point.Y >= tileSize * 8)
        {
            return;
        }

        int drawX = (int)(point.X / tileSize);
        int drawY = (int)(point.Y / tileSize);
        int boardX = RotateBoard ? 7 - drawX : drawX;
        int boardY = RotateBoard ? 7 - drawY : drawY;

        RaiseEvent(new BoardSquarePressedEventArgs(SquarePressedEvent, ToSquareName(boardX, boardY)));
    }

    private void DrawPieceGlyph(DrawingContext context, string piece, Rect tileRect)
    {
        string glyph = piece switch
        {
            "K" => "♔",
            "Q" => "♕",
            "R" => "♖",
            "B" => "♗",
            "N" => "♘",
            "P" => "♙",
            "k" => "♚",
            "q" => "♛",
            "r" => "♜",
            "b" => "♝",
            "n" => "♞",
            "p" => "♟",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(glyph))
        {
            return;
        }

        bool isWhitePiece = char.IsUpper(piece[0]);
        IBrush pieceBrush = isWhitePiece ? Brushes.WhiteSmoke : Brushes.Black;
        IBrush outlineBrush = isWhitePiece ? new SolidColorBrush(Color.Parse("#7A3A20")) : new SolidColorBrush(Color.Parse("#F2E8C9"));
        FormattedText text = new(
            glyph,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Symbol"),
            tileRect.Height * 0.72,
            pieceBrush);

        Point origin = new(
            tileRect.X + ((tileRect.Width - text.Width) / 2d),
            tileRect.Y + ((tileRect.Height - text.Height) / 2d) - 2);

        if (isWhitePiece)
        {
            FormattedText outlineText = new(
                glyph,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Symbol"),
                tileRect.Height * 0.72,
                outlineBrush);

            context.DrawText(outlineText, origin + new Point(-1, 0));
            context.DrawText(outlineText, origin + new Point(1, 0));
            context.DrawText(outlineText, origin + new Point(0, -1));
            context.DrawText(outlineText, origin + new Point(0, 1));
        }

        context.DrawText(text, origin);
    }

    private void DrawCoordinates(DrawingContext context, int boardX, int boardY, Rect tileRect, bool lightSquare, double tileSize)
    {
        IBrush brush = new SolidColorBrush(lightSquare ? Color.Parse("#8B4513") : Color.Parse("#F5DEB3"));
        double fontSize = Math.Max(10, tileSize / 5.5);

        Point drawPoint = ToDrawPoint(boardX, boardY);
        if (drawPoint.X == 0)
        {
            FormattedText rankText = new(
                (8 - boardY).ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush);
            context.DrawText(rankText, new Point(tileRect.X + 3, tileRect.Y + 1));
        }

        if (drawPoint.Y == 7)
        {
            FormattedText fileText = new(
                ((char)('a' + boardX)).ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush);
            context.DrawText(fileText, new Point(tileRect.Right - fileText.Width - 4, tileRect.Bottom - fileText.Height - 2));
        }
    }

    private void DrawArrow(DrawingContext context, BoardArrowViewModel arrow, double tileSize)
    {
        if (!TryParseSquare(arrow.FromSquare, out (int X, int Y) from)
            || !TryParseSquare(arrow.ToSquare, out (int X, int Y) to))
        {
            return;
        }

        Point drawFrom = ToDrawPoint(from.X, from.Y);
        Point drawTo = ToDrawPoint(to.X, to.Y);

        Point start = new((drawFrom.X * tileSize) + (tileSize / 2d), (drawFrom.Y * tileSize) + (tileSize / 2d));
        Point end = new((drawTo.X * tileSize) + (tileSize / 2d), (drawTo.Y * tileSize) + (tileSize / 2d));

        Pen pen = new(new SolidColorBrush(arrow.Color), 4);
        context.DrawLine(pen, start, end);

        Vector direction = start - end;
        if (direction.Length <= 0.001)
        {
            return;
        }

        direction = direction / direction.Length;
        Vector normal = new(-direction.Y, direction.X);
        Point arrowBase = end + (direction * 18);
        Point wing1 = arrowBase + (normal * 10);
        Point wing2 = arrowBase - (normal * 10);
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(end, true);
            geometryContext.LineTo(wing1);
            geometryContext.LineTo(wing2);
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(new SolidColorBrush(arrow.Color), null, geometry);
    }

    private Point ToDrawPoint(int boardX, int boardY)
        => RotateBoard ? new Point(7 - boardX, 7 - boardY) : new Point(boardX, boardY);

    private static string ToSquareName(int boardX, int boardY) => $"{(char)('a' + boardX)}{8 - boardY}";

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
}

public sealed class BoardSquarePressedEventArgs : RoutedEventArgs
{
    public BoardSquarePressedEventArgs(RoutedEvent routedEvent, string square)
        : base(routedEvent)
    {
        Square = square;
    }

    public string Square { get; }
}
