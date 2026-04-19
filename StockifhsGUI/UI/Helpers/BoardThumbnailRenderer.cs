using System.Drawing;
using System.Collections.Generic;

namespace StockifhsGUI.UI.Helpers;

public static class BoardThumbnailRenderer
{
    public static Bitmap Render(string fen, int size, IReadOnlyDictionary<string, Image> pieceImages)
    {
        Bitmap bmp = new(size, size);
        if (!FenPosition.TryParse(fen, out var position, out _) || position is null)
        {
            using Graphics gErr = Graphics.FromImage(bmp);
            gErr.Clear(Color.LightGray);
            gErr.DrawString("Invalid FEN", SystemFonts.DefaultFont, Brushes.Red, 10, 10);
            return bmp;
        }

        int tileSize = size / 8;
        using Graphics g = Graphics.FromImage(bmp);
        
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool lightSquare = (x + y) % 2 == 0;
                Brush brush = lightSquare ? Brushes.Beige : Brushes.Brown;
                g.FillRectangle(brush, x * tileSize, y * tileSize, tileSize, tileSize);

                string? piece = position.Board[x, y];
                if (!string.IsNullOrEmpty(piece) && pieceImages.TryGetValue(piece, out Image? pieceImage))
                {
                    g.DrawImage(pieceImage, x * tileSize, y * tileSize, tileSize, tileSize);
                }
            }
        }

        return bmp;
    }
}
