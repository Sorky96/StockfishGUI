using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;

public class Tracker
{
    private static readonly Regex SanRegex = new(
        @"\b(?:O-O-O|O-O|0-0-0|0-0|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?[+#]?|[a-h]x[a-h][1-8](?:=[QRBN])?[+#]?|[a-h][1-8](?:=[QRBN])?[+#]?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string langPath;

    public Tracker(string tessdataPath)
    {
        langPath = tessdataPath;
    }

    public List<string> TryReadMoves()
    {
        if (!IsFirefoxActive())
        {
            return new List<string>();
        }

        Rectangle rect = new(1740, 310, 260, 750);
        using Bitmap bmp = new(rect.Width, rect.Height);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
            MaskHighlight(bmp);
        }

        return ReadMovesFromBitmap(bmp);
    }

    public List<string> ReadMovesFromImage(string filePath)
    {
        using Bitmap bitmap = new(filePath);
        return ReadMovesFromBitmap(bitmap);
    }

    private List<string> ReadMovesFromBitmap(Bitmap source)
    {
        using Bitmap prepared = PrepareImage(source);
        string text = ReadText(prepared);
        return ParseSanMoves(text);
    }

    private string ReadText(Bitmap bitmap)
    {
        using TesseractEngine engine = new(langPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_pageseg_mode", "6");
        engine.SetVariable("tessedit_char_whitelist", "KQRBNOXabcdefgh12345678=+#-.!?0 ");

        using Pix pix = PixConverter.ToPix(bitmap);
        using Page page = engine.Process(pix);
        return page.GetText();
    }

    private static List<string> ParseSanMoves(string text)
    {
        string normalized = text.Replace('\r', ' ').Replace('\n', ' ');
        normalized = normalized.Replace('|', ' ');
        normalized = normalized.Replace("§", "5", StringComparison.Ordinal);
        normalized = normalized.Replace("0-0-0", "O-O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("0-0", "O-O", StringComparison.OrdinalIgnoreCase);

        List<string> moves = new();
        foreach (Match match in SanRegex.Matches(normalized))
        {
            string token = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                moves.Add(token);
            }
        }

        return moves;
    }

    private static Bitmap PrepareImage(Bitmap source)
    {
        Bitmap clone = new(source.Width, source.Height);
        using (Graphics g = Graphics.FromImage(clone))
        {
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        MaskHighlight(clone);
        using Bitmap gray = ToGray(clone);
        Threshold(gray, 140);
        return ScaleBicubic(gray, 2);
    }

    private static Bitmap ToGray(Bitmap src)
    {
        Bitmap dst = new(src.Width, src.Height);
        using Graphics g = Graphics.FromImage(dst);
        ColorMatrix cm = new(new float[][]
        {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });
        using ImageAttributes attributes = new();
        attributes.SetColorMatrix(cm);
        g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
        return dst;
    }

    private static void Threshold(Bitmap img, int threshold)
    {
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int value = img.GetPixel(x, y).R;
                img.SetPixel(x, y, value > threshold ? Color.White : Color.Black);
            }
        }
    }

    private static Bitmap ScaleBicubic(Bitmap src, int factor)
    {
        Bitmap dst = new(src.Width * factor, src.Height * factor);
        using Graphics g = Graphics.FromImage(dst);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, dst.Width, dst.Height);
        return dst;
    }

    private static void MaskHighlight(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color color = bmp.GetPixel(x, y);
                if (color.R > 50 && color.R < 120 && color.G == color.R && color.B == color.R)
                {
                    bmp.SetPixel(x, y, Color.Black);
                }
            }
        }
    }

    private static bool IsFirefoxActive()
    {
        IntPtr handle = GetForegroundWindow();
        StringBuilder titleBuilder = new(256);
        GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
        string title = titleBuilder.ToString().ToLowerInvariant();
        return title.Contains("firefox", StringComparison.Ordinal) || title.Contains("chess.com", StringComparison.Ordinal);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
