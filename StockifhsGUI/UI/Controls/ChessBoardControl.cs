using System.Drawing.Drawing2D;

namespace StockifhsGUI;

internal sealed class ChessBoardControl : DoubleBufferedPanel
{
    private const int GridSize = 8;

    public event EventHandler<BoardSquareClickEventArgs>? SquareClicked;

    public int TileSize { get; set; } = 64;
    public bool RotateBoard { get; set; }
    public string?[,] Board { get; set; } = new string?[GridSize, GridSize];
    public Point? SelectedSquare { get; set; }
    public Point? AnalysisTargetSquare { get; set; }
    public Point? PreviewTargetSquare { get; set; }
    public IReadOnlyCollection<Point> AvailableMoves { get; set; } = Array.Empty<Point>();
    public IReadOnlyList<BoardArrow> Arrows { get; set; } = Array.Empty<BoardArrow>();
    public IReadOnlyDictionary<string, Image> PieceImages { get; set; } = new Dictionary<string, Image>();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        using Font coordinateFont = new("Segoe UI", Math.Max(9, TileSize / 6f), FontStyle.Bold);
        using Pen boardBorderPen = new(UiTheme.BorderColor, 2);
        using SolidBrush boardBackgroundBrush = new(Color.FromArgb(234, 237, 241));

        g.FillRectangle(boardBackgroundBrush, ClientRectangle);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int drawX = RotateBoard ? 7 - x : x;
                int drawY = RotateBoard ? 7 - y : y;
                bool lightSquare = (x + y) % 2 == 0;
                Brush brush = lightSquare ? Brushes.Beige : Brushes.Brown;
                g.FillRectangle(brush, drawX * TileSize, drawY * TileSize, TileSize, TileSize);

                if (SelectedSquare.HasValue && SelectedSquare.Value.X == x && SelectedSquare.Value.Y == y)
                {
                    g.DrawRectangle(Pens.Red, drawX * TileSize, drawY * TileSize, TileSize, TileSize);
                }

                if (AnalysisTargetSquare.HasValue && AnalysisTargetSquare.Value.X == x && AnalysisTargetSquare.Value.Y == y)
                {
                    using Pen analysisPen = new(Color.Gold, 4);
                    g.DrawRectangle(
                        analysisPen,
                        drawX * TileSize + 2,
                        drawY * TileSize + 2,
                        TileSize - 4,
                        TileSize - 4);
                }

                if (PreviewTargetSquare.HasValue && PreviewTargetSquare.Value.X == x && PreviewTargetSquare.Value.Y == y)
                {
                    using SolidBrush previewBrush = new(Color.FromArgb(96, Color.DeepSkyBlue));
                    using Pen previewPen = new(Color.DeepSkyBlue, 3);
                    g.FillRectangle(previewBrush, drawX * TileSize + 4, drawY * TileSize + 4, TileSize - 8, TileSize - 8);
                    g.DrawRectangle(previewPen, drawX * TileSize + 4, drawY * TileSize + 4, TileSize - 8, TileSize - 8);
                }

                if (AvailableMoves.Contains(new Point(x, y)))
                {
                    g.FillEllipse(
                        Brushes.Gold,
                        drawX * TileSize + TileSize / 3,
                        drawY * TileSize + TileSize / 3,
                        TileSize / 3,
                        TileSize / 3);
                }

                string? piece = Board[x, y];
                if (!string.IsNullOrEmpty(piece) && PieceImages.TryGetValue(piece, out Image? pieceImage))
                {
                    g.DrawImage(pieceImage, drawX * TileSize, drawY * TileSize, TileSize, TileSize);
                }

                DrawBoardCoordinates(g, coordinateFont, x, y, drawX, drawY, lightSquare);
            }
        }

        g.DrawRectangle(boardBorderPen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

        foreach (BoardArrow arrow in Arrows)
        {
            DrawArrow(g, arrow);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (TileSize <= 0 || e.X >= TileSize * GridSize || e.Y >= TileSize * GridSize)
        {
            return;
        }

        int x = e.X / TileSize;
        int y = e.Y / TileSize;

        if (RotateBoard)
        {
            x = 7 - x;
            y = 7 - y;
        }

        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize)
        {
            return;
        }

        SquareClicked?.Invoke(this, new BoardSquareClickEventArgs(new Point(x, y)));
    }

    private void DrawArrow(Graphics g, BoardArrow arrow)
    {
        using Pen arrowPen = new(arrow.Color, 4)
        {
            CustomEndCap = new AdjustableArrowCap(6, 6)
        };

        Point drawFrom = RotateBoard ? new Point(7 - arrow.From.X, 7 - arrow.From.Y) : arrow.From;
        Point drawTo = RotateBoard ? new Point(7 - arrow.To.X, 7 - arrow.To.Y) : arrow.To;

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
}

internal sealed class BoardSquareClickEventArgs : EventArgs
{
    public BoardSquareClickEventArgs(Point square)
    {
        Square = square;
    }

    public Point Square { get; }
}

internal readonly record struct BoardArrow(Point From, Point To, Color Color);
