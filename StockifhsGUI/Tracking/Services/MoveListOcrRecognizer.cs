using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace StockifhsGUI;

public interface IMoveListRecognizer
{
    bool TryRecognize(Bitmap source, out MoveListRecognitionResult? result, out string? error);
}

public sealed class MoveListOcrRecognizer : IMoveListRecognizer
{
    private static readonly Regex SanRegex = new(
        @"(?<!\S)(?:O-O-O|O-O|0-0-0|0-0|[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=[QRBN])?[+#]?|[a-h]x[a-h][1-8](?:=[QRBN])?[+#]?|[a-h][1-8](?:=[QRBN])?[+#]?)(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string tesseractDataPath;

    public MoveListOcrRecognizer(string tesseractDataPath)
    {
        this.tesseractDataPath = tesseractDataPath;
    }

    public bool TryRecognize(Bitmap source, out MoveListRecognitionResult? result, out string? error)
    {
        result = null;
        error = null;

        if (!TesseractDataResolver.TryGetReadyDataPath(out string resolvedDataPath, out error))
        {
            return false;
        }

        try
        {
            using Bitmap prepared = PrepareImage(source);
            string text = ReadText(prepared, resolvedDataPath);
            List<string> recognizedMoves = ParseSanMoves(text);
            if (recognizedMoves.Count == 0)
            {
                error = "No SAN moves were recognized from the move list.";
                return false;
            }

            if (!TryReplayRecognizedMoves(recognizedMoves, out List<string> moves, out string fen, out string placementFen, out error))
            {
                return false;
            }

            result = new MoveListRecognitionResult(
                fen,
                placementFen,
                moves,
                0.92,
                DateTime.UtcNow);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static List<string> ParseSanMoves(string text)
    {
        string normalized = text.Replace('\r', ' ').Replace('\n', ' ');
        normalized = normalized.Replace('|', ' ');
        normalized = normalized.Replace("§", "5", StringComparison.Ordinal);
        normalized = normalized.Replace("0-0-0", "O-O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("0-0", "O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("—", "-", StringComparison.Ordinal);

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

    public static bool TryReplayRecognizedMoves(
        IReadOnlyList<string> recognizedMoves,
        out List<string> resolvedMoves,
        out string fen,
        out string placementFen,
        out string? error)
    {
        ChessGame initialGame = new();
        if (TryReplayRecognizedMovesRecursive(initialGame, recognizedMoves, 0, new List<string>(), 0, out resolvedMoves, out ChessGame? resolvedGame, out _, out error)
            && resolvedGame is not null)
        {
            fen = resolvedGame.GetFen();
            placementFen = resolvedGame.GetPlacementFen();
            return true;
        }

        resolvedMoves = new List<string>();
        fen = string.Empty;
        placementFen = string.Empty;
        return false;
    }

    private static bool TryReplayRecognizedMovesRecursive(
        ChessGame game,
        IReadOnlyList<string> recognizedMoves,
        int index,
        List<string> currentMoves,
        int currentScore,
        out List<string> resolvedMoves,
        out ChessGame? resolvedGame,
        out int resolvedScore,
        out string? error)
    {
        if (index >= recognizedMoves.Count)
        {
            resolvedMoves = new List<string>(currentMoves);
            resolvedGame = game;
            resolvedScore = currentScore;
            error = null;
            return true;
        }

        string token = SanNotation.NormalizeSan(recognizedMoves[index]);
        List<string> candidates = game.GetLegalSanMoves()
            .Where(legalSan => MatchesRecognizedToken(token, legalSan))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(legalSan => SanNotation.NormalizeSan(legalSan) == token)
            .ThenBy(legalSan => legalSan, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            resolvedMoves = new List<string>();
            resolvedGame = null;
            resolvedScore = int.MinValue;
            error = $"No legal move matches SAN '{recognizedMoves[index]}' in the current position.";
            return false;
        }

        List<string>? bestMoves = null;
        ChessGame? bestGame = null;
        int bestScore = int.MinValue;
        string? bestError = null;

        foreach (string candidate in candidates)
        {
            ChessGame nextGame = new();
            if (!nextGame.TryLoadFen(game.GetFen(), out error))
            {
                resolvedMoves = new List<string>();
                resolvedGame = null;
                resolvedScore = int.MinValue;
                return false;
            }

            nextGame.ApplySan(candidate);
            currentMoves.Add(candidate);
            int nextScore = currentScore + GetMatchScore(token, candidate);

            if (TryReplayRecognizedMovesRecursive(nextGame, recognizedMoves, index + 1, currentMoves, nextScore, out List<string> childMoves, out ChessGame? childGame, out int childScore, out string? childError))
            {
                if (childScore > bestScore)
                {
                    bestScore = childScore;
                    bestMoves = childMoves;
                    bestGame = childGame;
                    bestError = null;
                }
            }
            else if (bestError is null)
            {
                bestError = childError;
            }

            currentMoves.RemoveAt(currentMoves.Count - 1);
        }

        if (bestMoves is not null && bestGame is not null)
        {
            resolvedMoves = bestMoves;
            resolvedGame = bestGame;
            resolvedScore = bestScore;
            error = null;
            return true;
        }

        resolvedMoves = new List<string>();
        resolvedGame = null;
        resolvedScore = int.MinValue;
        error = bestError ?? $"No legal continuation matches recognized move '{recognizedMoves[index]}'.";
        return false;
    }

    private static bool MatchesRecognizedToken(string normalizedRecognizedToken, string legalSan)
    {
        string normalizedLegalSan = SanNotation.NormalizeSan(legalSan);
        if (normalizedLegalSan == normalizedRecognizedToken)
        {
            return true;
        }

        if (SanNotation.HasExplicitPiecePrefix(normalizedRecognizedToken))
        {
            return false;
        }

        if (normalizedLegalSan.StartsWith("O-O", StringComparison.Ordinal))
        {
            return false;
        }

        if (!SanNotation.HasExplicitPiecePrefix(normalizedLegalSan))
        {
            return false;
        }

        return normalizedLegalSan[1..] == normalizedRecognizedToken;
    }

    private static int GetMatchScore(string normalizedRecognizedToken, string legalSan)
    {
        string normalizedLegalSan = SanNotation.NormalizeSan(legalSan);
        bool tokenHasPiecePrefix = SanNotation.HasExplicitPiecePrefix(normalizedRecognizedToken);
        bool legalHasPiecePrefix = SanNotation.HasExplicitPiecePrefix(normalizedLegalSan);

        if (normalizedLegalSan == normalizedRecognizedToken)
        {
            if (tokenHasPiecePrefix)
            {
                return 100;
            }

            return legalHasPiecePrefix ? 40 : 25;
        }

        if (!tokenHasPiecePrefix && legalHasPiecePrefix && normalizedLegalSan.Length > 1 && normalizedLegalSan[1..] == normalizedRecognizedToken)
        {
            return 60;
        }

        return 0;
    }

    private string ReadText(Bitmap bitmap, string dataPath)
    {
        using TesseractEngine engine = new(dataPath, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_pageseg_mode", "6");
        engine.SetVariable("tessedit_char_whitelist", "KQRBNOXabcdefgh12345678=+#-.!?0 ");

        using Pix pix = PixConverter.ToPix(bitmap);
        using Page page = engine.Process(pix);
        return page.GetText();
    }

    private static Bitmap PrepareImage(Bitmap source)
    {
        Bitmap clone = new(source.Width, source.Height);
        using (Graphics graphics = Graphics.FromImage(clone))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        MaskHighlight(clone);
        using Bitmap gray = ToGray(clone);
        Threshold(gray, 140);
        return ScaleBicubic(gray, 2);
    }

    private static Bitmap ToGray(Bitmap source)
    {
        Bitmap destination = new(source.Width, source.Height);
        using Graphics graphics = Graphics.FromImage(destination);
        ColorMatrix matrix = new(new float[][]
        {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new float[] { 0, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });

        using ImageAttributes attributes = new();
        attributes.SetColorMatrix(matrix);
        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return destination;
    }

    private static void Threshold(Bitmap image, int threshold)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int value = image.GetPixel(x, y).R;
                image.SetPixel(x, y, value > threshold ? Color.White : Color.Black);
            }
        }
    }

    private static Bitmap ScaleBicubic(Bitmap source, int factor)
    {
        Bitmap destination = new(source.Width * factor, source.Height * factor);
        using Graphics graphics = Graphics.FromImage(destination);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, destination.Width, destination.Height);
        return destination;
    }

    private static void MaskHighlight(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color color = bitmap.GetPixel(x, y);
                if (color.R > 50 && color.R < 120 && color.G == color.R && color.B == color.R)
                {
                    bitmap.SetPixel(x, y, Color.Black);
                }
            }
        }
    }
}
