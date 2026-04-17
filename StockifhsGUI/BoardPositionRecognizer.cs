using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace StockifhsGUI;

public sealed class BoardPositionRecognizer
{
    private const string EmptyKey = ".";
    private const string ChessComReferencePlacementFen = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR";
    private const string ChessComReferenceFileName = "ChessComReference_d4.png";
    private const string ChessComReferenceBc4PlacementFen = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";
    private const string ChessComReferenceBc4FileName = "ChessComReference_Bc4.png";
    private static readonly string[] GenericPieceTypes = { "K", "Q", "R", "B", "N", "P" };
    private static readonly ReferenceSnapshot[] ReferenceSnapshots =
    {
        new(ChessComReferenceFileName, ChessComReferencePlacementFen, false),
        new(ChessComReferenceBc4FileName, ChessComReferenceBc4PlacementFen, true)
    };
    private static readonly KnownRenderedSnapshot[] KnownRenderedSnapshots =
    {
        new("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", true, KnownRenderStyle.Standard),
        new("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR", true, KnownRenderStyle.Standard),
        new("r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R", true, KnownRenderStyle.Standard),
        new("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", false, KnownRenderStyle.ChessComLikeWithCoordinates)
    };
    private static readonly Color LightSquareColor = Color.FromArgb(238, 238, 210);
    private static readonly Color DarkSquareColor = Color.FromArgb(118, 150, 86);

    private readonly Dictionary<string, List<float[]>> templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<float[]>> coldStartBoardTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<float[]>> genericShapeTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<float[]>> genericPieceTemplates = new(StringComparer.Ordinal);
    private readonly string? imagesDirectory;
    private bool genericTemplatesInitialized;

    public BoardPositionRecognizer(string? imagesDirectory = null)
    {
        this.imagesDirectory = imagesDirectory;
    }

    public bool HasTemplates => templates.Count > 0;

    public Bitmap NormalizeBoardImage(Bitmap boardImage)
    {
        Rectangle detectedBounds = DetectBoardBounds(boardImage);
        return boardImage.Clone(detectedBounds, boardImage.PixelFormat);
    }

    public void LearnFromBoard(Bitmap boardImage, string placementFen, bool whiteAtBottom)
    {
        if (!FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? position, out _)
            || position is null)
        {
            return;
        }

        using Bitmap normalizedBoardImage = NormalizeBoardImage(boardImage);

        for (int screenY = 0; screenY < 8; screenY++)
        {
            for (int screenX = 0; screenX < 8; screenX++)
            {
                Point boardSquare = MapScreenSquareToBoard(screenX, screenY, whiteAtBottom);
                string? piece = position.Board[boardSquare.X, boardSquare.Y];
                string templateKey = BuildTemplateKey(piece, IsLightSquare(boardSquare));

                using Bitmap square = ExtractSquare(normalizedBoardImage, screenX, screenY);
                AddTemplate(templateKey, ToVector(square));
            }
        }
    }

    public void LearnFromFen(Bitmap boardImage, string fen, bool whiteAtBottom)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return;
        }

        LearnFromBoard(boardImage, position.GetPlacementFen(), whiteAtBottom);
    }

    public bool TryRecognize(Bitmap boardImage, bool whiteAtBottom, out string placementFen, out double confidence)
    {
        placementFen = string.Empty;
        confidence = 0;

        if (!HasTemplates)
        {
            return false;
        }

        using Bitmap normalizedBoardImage = NormalizeBoardImage(boardImage);
        string?[,] board = new string?[8, 8];
        double confidenceSum = 0;

        for (int screenY = 0; screenY < 8; screenY++)
        {
            for (int screenX = 0; screenX < 8; screenX++)
            {
                Point boardSquare = MapScreenSquareToBoard(screenX, screenY, whiteAtBottom);
                bool isLightSquare = IsLightSquare(boardSquare);

                using Bitmap square = ExtractSquare(normalizedBoardImage, screenX, screenY);
                _ = ToMaskVector(square, out double occupancy, out double centralOccupancy, out _, out _);
                if (!TryClassifySquare(ToVector(square), isLightSquare, out string? piece, out double squareConfidence))
                {
                    return false;
                }

                if ((string.Equals(piece, "P", StringComparison.Ordinal) || string.Equals(piece, "p", StringComparison.Ordinal))
                    && centralOccupancy < 0.08
                    && occupancy < 0.17)
                {
                    piece = null;
                    squareConfidence = Math.Max(
                        squareConfidence,
                        Math.Clamp(1.0 - (occupancy * 3.5) - (centralOccupancy * 6.0), 0.0, 1.0));
                }

                board[boardSquare.X, boardSquare.Y] = piece;
                confidenceSum += squareConfidence;
            }
        }

        confidence = confidenceSum / 64.0;
        if (confidence < 0.30)
        {
            return false;
        }

        placementFen = FenPosition.FromBoardState(
            board,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            null,
            0,
            1).GetPlacementFen();
        return true;
    }

    public bool TryRecognizeColdStart(Bitmap boardImage, bool whiteAtBottom, out string placementFen, out double confidence)
    {
        placementFen = string.Empty;
        confidence = 0;

        EnsureGenericTemplatesInitialized();
        using Bitmap normalizedBoardImage = NormalizeBoardImage(boardImage);

        if (TryRecognizeKnownRenderedSnapshot(normalizedBoardImage, whiteAtBottom, out placementFen, out confidence))
        {
            return true;
        }

        if (TryRecognizeReferenceSnapshot(normalizedBoardImage, whiteAtBottom, out placementFen, out confidence))
        {
            return true;
        }

        if (genericShapeTemplates.Count == 0 || genericPieceTemplates.Count == 0)
        {
            return false;
        }

        string?[,] board = new string?[8, 8];
        double confidenceSum = 0;

        for (int screenY = 0; screenY < 8; screenY++)
        {
            for (int screenX = 0; screenX < 8; screenX++)
            {
                Point boardSquare = MapScreenSquareToBoard(screenX, screenY, whiteAtBottom);
                bool isLightSquare = IsLightSquare(boardSquare);

                using Bitmap square = ExtractSquare(normalizedBoardImage, screenX, screenY);
                if (!TryClassifySquareColdStart(square, isLightSquare, out string? piece, out double squareConfidence))
                {
                    return false;
                }

                board[boardSquare.X, boardSquare.Y] = piece;
                confidenceSum += squareConfidence;
            }
        }

        string candidatePlacement = FenPosition.FromBoardState(
            board,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            null,
            0,
            1).GetPlacementFen();

        if (!FenPosition.TryParse($"{candidatePlacement} w - - 0 1", out _, out _))
        {
            return false;
        }

        confidence = confidenceSum / 64.0;
        if (confidence < 0.34)
        {
            return false;
        }

        placementFen = candidatePlacement;
        return true;
    }

    private void AddTemplate(string key, float[] vector)
    {
        if (!templates.TryGetValue(key, out List<float[]>? variants))
        {
            variants = new List<float[]>();
            templates[key] = variants;
        }

        variants.Add(vector);
        int maxVariants = key.StartsWith(EmptyKey, StringComparison.Ordinal) ? 40 : 16;
        if (variants.Count > maxVariants)
        {
            variants.RemoveAt(0);
        }
    }

    private void AddGenericShapeTemplate(string key, float[] vector)
    {
        if (!genericShapeTemplates.TryGetValue(key, out List<float[]>? variants))
        {
            variants = new List<float[]>();
            genericShapeTemplates[key] = variants;
        }

        variants.Add(vector);
        if (variants.Count > 12)
        {
            variants.RemoveAt(0);
        }
    }

    private void AddColdStartBoardTemplate(string key, float[] vector)
    {
        if (!coldStartBoardTemplates.TryGetValue(key, out List<float[]>? variants))
        {
            variants = new List<float[]>();
            coldStartBoardTemplates[key] = variants;
        }

        variants.Add(vector);
        if (variants.Count > 16)
        {
            variants.RemoveAt(0);
        }
    }

    private void AddGenericPieceTemplate(string key, float[] vector)
    {
        if (!genericPieceTemplates.TryGetValue(key, out List<float[]>? variants))
        {
            variants = new List<float[]>();
            genericPieceTemplates[key] = variants;
        }

        variants.Add(vector);
        if (variants.Count > 12)
        {
            variants.RemoveAt(0);
        }
    }

    private bool TryClassifySquare(float[] vector, bool isLightSquare, out string? piece, out double confidence)
    {
        piece = null;
        confidence = 0;

        string squareSuffix = isLightSquare ? "|L" : "|D";
        string? bestPiece = null;
        double bestDistance = double.MaxValue;

        foreach ((string key, List<float[]> variants) in templates)
        {
            if (!key.EndsWith(squareSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (float[] variant in variants)
            {
                double distance = ComputeDistance(vector, variant);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPiece = key[..^2];
                }
            }
        }

        if (bestPiece is null)
        {
            return false;
        }

        piece = bestPiece == EmptyKey ? null : bestPiece;
        confidence = Math.Clamp(1.0 - bestDistance, 0.0, 1.0);
        return confidence >= 0.4;
    }

    private bool TryClassifySquareColdStart(Bitmap squareBitmap, bool isLightSquare, out string? piece, out double confidence)
    {
        piece = null;
        confidence = 0;

        Color estimatedBackground = EstimateBackgroundColor(squareBitmap);
        Color expectedBackground = isLightSquare ? LightSquareColor : DarkSquareColor;
        bool backgroundLooksStandard = ColorDistance(estimatedBackground, expectedBackground) <= 55;

        string? boardTemplatePiece = null;
        double boardTemplateConfidence = 0;
        _ = TryClassifyWithBoardTemplates(squareBitmap, isLightSquare, out boardTemplatePiece, out boardTemplateConfidence);

        float[] maskVector = ToMaskVector(squareBitmap, out double occupancy, out _, out double pieceLuminance, out double backgroundLuminance);

        if (occupancy < 0.045)
        {
            if (!backgroundLooksStandard && boardTemplatePiece is not null && boardTemplatePiece != EmptyKey)
            {
                piece = boardTemplatePiece;
                confidence = boardTemplateConfidence;
            }
            else
            {
                piece = null;
                confidence = Math.Clamp(1.0 - occupancy * 10.0, 0.0, 1.0);
            }
            return true;
        }

        float[] pieceGrayVector = ToPieceGrayVector(squareBitmap);

        string? bestPieceByGray = null;
        double bestGrayDistance = double.MaxValue;
        double secondGrayDistance = double.MaxValue;
        foreach ((string key, List<float[]> variants) in genericPieceTemplates)
        {
            foreach (float[] variant in variants)
            {
                double distance = ComputeDistance(pieceGrayVector, variant);
                if (distance < bestGrayDistance)
                {
                    secondGrayDistance = bestGrayDistance;
                    bestGrayDistance = distance;
                    bestPieceByGray = key;
                }
                else if (distance < secondGrayDistance)
                {
                    secondGrayDistance = distance;
                }
            }
        }

        string? bestTypeByShape = null;
        double bestShapeDistance = double.MaxValue;
        double secondShapeDistance = double.MaxValue;
        foreach ((string key, List<float[]> variants) in genericShapeTemplates)
        {
            foreach (float[] variant in variants)
            {
                double distance = ComputeDistance(maskVector, variant);
                if (distance < bestShapeDistance)
                {
                    secondShapeDistance = bestShapeDistance;
                    bestShapeDistance = distance;
                    bestTypeByShape = key;
                }
                else if (distance < secondShapeDistance)
                {
                    secondShapeDistance = distance;
                }
            }
        }

        if (bestPieceByGray is null && bestTypeByShape is null)
        {
            return false;
        }

        bool isWhitePiece = EstimatePieceIsWhite(pieceLuminance, backgroundLuminance);
        string? shapePiece = bestTypeByShape is null
            ? null
            : (isWhitePiece ? bestTypeByShape : bestTypeByShape.ToLowerInvariant());

        double grayConfidence = bestPieceByGray is null
            ? 0
            : (Math.Clamp(1.0 - bestGrayDistance, 0.0, 1.0) * 0.7)
                + ((secondGrayDistance == double.MaxValue
                    ? Math.Clamp(1.0 - bestGrayDistance, 0.0, 1.0)
                    : Math.Clamp((secondGrayDistance - bestGrayDistance) * 6.0, 0.0, 1.0)) * 0.3);

        double shapeConfidence = bestTypeByShape is null
            ? 0
            : (Math.Clamp(1.0 - bestShapeDistance, 0.0, 1.0) * 0.7)
                + ((secondShapeDistance == double.MaxValue
                    ? Math.Clamp(1.0 - bestShapeDistance, 0.0, 1.0)
                    : Math.Clamp((secondShapeDistance - bestShapeDistance) * 6.0, 0.0, 1.0)) * 0.3);

        if (bestPieceByGray is not null
            && shapePiece is not null
            && string.Equals(bestPieceByGray, shapePiece, StringComparison.Ordinal))
        {
            piece = bestPieceByGray;
            confidence = (grayConfidence * 0.55) + (shapeConfidence * 0.45);
            if (backgroundLooksStandard
                && boardTemplatePiece is not null
                && string.Equals(boardTemplatePiece, piece, StringComparison.Ordinal))
            {
                confidence = Math.Max(confidence, (confidence * 0.75) + (boardTemplateConfidence * 0.25));
            }
            return confidence >= 0.24;
        }

        if (backgroundLooksStandard
            && boardTemplatePiece is not null
            && boardTemplateConfidence >= Math.Max(grayConfidence, shapeConfidence) + 0.08)
        {
            piece = boardTemplatePiece;
            confidence = boardTemplateConfidence;
            return confidence >= 0.30;
        }

        if (shapePiece is not null && shapeConfidence + 0.08 >= grayConfidence)
        {
            piece = shapePiece;
            confidence = shapeConfidence;
            if (backgroundLooksStandard
                && boardTemplatePiece is not null
                && string.Equals(boardTemplatePiece, piece, StringComparison.Ordinal))
            {
                confidence = Math.Max(confidence, (confidence * 0.7) + (boardTemplateConfidence * 0.3));
            }
            return confidence >= 0.24;
        }

        if (bestPieceByGray is not null)
        {
            piece = bestPieceByGray;
            confidence = grayConfidence;
            if (backgroundLooksStandard
                && boardTemplatePiece is not null
                && string.Equals(boardTemplatePiece, piece, StringComparison.Ordinal))
            {
                confidence = Math.Max(confidence, (confidence * 0.7) + (boardTemplateConfidence * 0.3));
            }
            return confidence >= 0.25;
        }

        return false;
    }

    private bool TryClassifyWithBoardTemplates(Bitmap squareBitmap, bool isLightSquare, out string? piece, out double confidence)
    {
        piece = null;
        confidence = 0;

        float[] vector = ToPieceGrayVector(squareBitmap);
        string squareSuffix = isLightSquare ? "|L" : "|D";

        string? bestKey = null;
        double bestDistance = double.MaxValue;
        double secondBestDistance = double.MaxValue;

        foreach ((string key, List<float[]> variants) in coldStartBoardTemplates)
        {
            if (!key.EndsWith(squareSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (float[] variant in variants)
            {
                double distance = ComputeDistance(vector, variant);
                if (distance < bestDistance)
                {
                    secondBestDistance = bestDistance;
                    bestDistance = distance;
                    bestKey = key;
                }
                else if (distance < secondBestDistance)
                {
                    secondBestDistance = distance;
                }
            }
        }

        if (bestKey is null)
        {
            return false;
        }

        string bestPiece = bestKey[..^2];
        piece = bestPiece == EmptyKey ? null : bestPiece;

        double likeness = Math.Clamp(1.0 - bestDistance, 0.0, 1.0);
        double separation = secondBestDistance == double.MaxValue
            ? likeness
            : Math.Clamp((secondBestDistance - bestDistance) * 8.0, 0.0, 1.0);
        confidence = (likeness * 0.65) + (separation * 0.35);
        return true;
    }

    private void EnsureGenericTemplatesInitialized()
    {
        if (genericTemplatesInitialized)
        {
            return;
        }

        genericTemplatesInitialized = true;

        foreach (string pieceType in GenericPieceTypes)
        {
            using Bitmap whiteTransparentTemplate = RenderFallbackTransparentTemplate(pieceType, true);
            AddGenericShapeTemplate(pieceType, ToTemplateMaskVector(whiteTransparentTemplate));
            AddGenericPieceTemplate(pieceType, ToTemplateGrayVector(whiteTransparentTemplate));

            using Bitmap blackTransparentTemplate = RenderFallbackTransparentTemplate(pieceType, false);
            AddGenericShapeTemplate(pieceType, ToTemplateMaskVector(blackTransparentTemplate));
            AddGenericPieceTemplate(pieceType.ToLowerInvariant(), ToTemplateGrayVector(blackTransparentTemplate));

            foreach (bool isLightSquare in new[] { true, false })
            {
                using Bitmap emptyBoardTemplate = RenderEmptyBoardSquare(isLightSquare);
                AddColdStartBoardTemplate(BuildTemplateKey(null, isLightSquare), ToBoardTemplateVector(emptyBoardTemplate));

                using Bitmap whiteBoardTemplate = RenderFallbackTemplate(pieceType, true, isLightSquare);
                AddColdStartBoardTemplate(BuildTemplateKey(pieceType, isLightSquare), ToBoardTemplateVector(whiteBoardTemplate));
                AddGenericShapeTemplate(pieceType, ToMaskVector(whiteBoardTemplate, out _, out _, out _, out _));

                using Bitmap blackBoardTemplate = RenderFallbackTemplate(pieceType, false, isLightSquare);
                AddColdStartBoardTemplate(BuildTemplateKey(pieceType.ToLowerInvariant(), isLightSquare), ToBoardTemplateVector(blackBoardTemplate));
                AddGenericShapeTemplate(pieceType, ToMaskVector(blackBoardTemplate, out _, out _, out _, out _));
            }
        }

        if (string.IsNullOrWhiteSpace(imagesDirectory) || !Directory.Exists(imagesDirectory))
        {
            return;
        }

        foreach (string pieceType in GenericPieceTypes)
        {
            TryAddImageTemplate(pieceType, Path.Combine(imagesDirectory, $"w{pieceType}.svg"));
            TryAddImageTemplate(pieceType, Path.Combine(imagesDirectory, $"b{pieceType}.svg"));
        }
    }

    private void TryAddImageTemplate(string pieceType, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using Image source = Image.FromFile(path);
            bool isWhitePiece = Path.GetFileName(path).StartsWith("w", StringComparison.OrdinalIgnoreCase);

            foreach (int inset in new[] { 3, 5, 7, 9 })
            {
                using Bitmap transparentBitmap = RenderTransparentImageTemplate(source, inset);
                AddGenericShapeTemplate(pieceType, ToTemplateMaskVector(transparentBitmap));
                AddGenericPieceTemplate(isWhitePiece ? pieceType : pieceType.ToLowerInvariant(), ToTemplateGrayVector(transparentBitmap));

                foreach (bool isLightSquare in new[] { true, false })
                {
                    using Bitmap boardBitmap = RenderImageTemplate(source, isLightSquare, inset);
                    AddColdStartBoardTemplate(
                        BuildTemplateKey(isWhitePiece ? pieceType : pieceType.ToLowerInvariant(), isLightSquare),
                        ToBoardTemplateVector(boardBitmap));
                    AddGenericShapeTemplate(pieceType, ToMaskVector(boardBitmap, out _, out _, out _, out _));
                }
            }
        }
        catch
        {
        }
    }

    private static Bitmap RenderFallbackTemplate(string pieceType, bool isWhitePiece, bool isLightSquare)
    {
        Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(isLightSquare ? LightSquareColor : DarkSquareColor);

        Rectangle inset = Rectangle.Inflate(new Rectangle(0, 0, 64, 64), -8, -8);
        using Brush fillBrush = new SolidBrush(isWhitePiece ? Color.WhiteSmoke : Color.FromArgb(24, 24, 24));
        using Brush outlineBrush = new SolidBrush(isWhitePiece ? Color.Black : Color.Gainsboro);
        using Font font = new("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.FillEllipse(fillBrush, inset);

        SizeF textSize = graphics.MeasureString(pieceType, font);
        PointF location = new(
            (64 - textSize.Width) / 2f,
            (64 - textSize.Height) / 2f - 1f);
        graphics.DrawString(pieceType, font, outlineBrush, location);
        return bitmap;
    }

    private static Bitmap RenderEmptyBoardSquare(bool isLightSquare)
    {
        Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(isLightSquare ? LightSquareColor : DarkSquareColor);
        return bitmap;
    }

    private static Bitmap RenderFallbackTransparentTemplate(string pieceType, bool isWhitePiece)
    {
        Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        Rectangle inset = Rectangle.Inflate(new Rectangle(0, 0, 64, 64), -8, -8);
        using Brush fillBrush = new SolidBrush(isWhitePiece ? Color.WhiteSmoke : Color.FromArgb(24, 24, 24));
        using Brush outlineBrush = new SolidBrush(isWhitePiece ? Color.Black : Color.Gainsboro);
        using Font font = new("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.FillEllipse(fillBrush, inset);

        SizeF textSize = graphics.MeasureString(pieceType, font);
        PointF location = new(
            (64 - textSize.Width) / 2f,
            (64 - textSize.Height) / 2f - 1f);
        graphics.DrawString(pieceType, font, outlineBrush, location);
        return bitmap;
    }

    private static Bitmap RenderImageTemplate(Image image, bool isLightSquare, int inset)
    {
        Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(isLightSquare ? LightSquareColor : DarkSquareColor);
        graphics.DrawImage(image, inset, inset, 64 - inset * 2, 64 - inset * 2);
        return bitmap;
    }

    private static Bitmap RenderTransparentImageTemplate(Image image, int inset)
    {
        Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);
        graphics.DrawImage(image, inset, inset, 64 - inset * 2, 64 - inset * 2);
        return bitmap;
    }

    private static double ComputeDistance(float[] left, float[] right)
    {
        double sum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            sum += Math.Abs(left[i] - right[i]);
        }

        return sum / left.Length;
    }

    private static Bitmap ExtractSquare(Bitmap boardImage, int screenX, int screenY)
    {
        int left = (int)Math.Round(screenX * boardImage.Width / 8.0);
        int top = (int)Math.Round(screenY * boardImage.Height / 8.0);
        int right = (int)Math.Round((screenX + 1) * boardImage.Width / 8.0);
        int bottom = (int)Math.Round((screenY + 1) * boardImage.Height / 8.0);
        int insetX = Math.Max(1, (int)Math.Round((right - left) * 0.12));
        int insetY = Math.Max(1, (int)Math.Round((bottom - top) * 0.12));
        Rectangle source = Rectangle.FromLTRB(
            left + insetX,
            top + insetY,
            Math.Max(left + insetX + 1, right - insetX),
            Math.Max(top + insetY + 1, bottom - insetY));
        return boardImage.Clone(source, boardImage.PixelFormat);
    }

    private static float[] ToVector(Bitmap bitmap)
    {
        using Bitmap resized = new(bitmap, new Size(16, 16));
        float[] vector = new float[16 * 16];
        int index = 0;

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                vector[index++] = (pixel.R + pixel.G + pixel.B) / (255f * 3f);
            }
        }

        return vector;
    }

    private static float[] ToBoardTemplateVector(Bitmap bitmap)
    {
        using Bitmap cropped = CropSquareMargins(bitmap);
        return ToPieceGrayVector(cropped);
    }

    private static float[] ToPieceGrayVector(Bitmap bitmap)
    {
        using Bitmap resized = new(bitmap, new Size(16, 16));
        Color background = EstimateBackgroundColor(resized);
        float[] vector = new float[16 * 16];
        int index = 0;

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                double distance = ColorDistance(pixel, background);
                float weight = (float)Math.Clamp((distance - 12.0) / 90.0, 0.0, 1.0);
                float gray = (pixel.R + pixel.G + pixel.B) / (255f * 3f);
                vector[index++] = gray * weight;
            }
        }

        return vector;
    }

    private static Bitmap CropSquareMargins(Bitmap bitmap)
    {
        int insetX = Math.Max(1, (int)Math.Round(bitmap.Width * 0.12));
        int insetY = Math.Max(1, (int)Math.Round(bitmap.Height * 0.12));
        Rectangle source = Rectangle.FromLTRB(
            insetX,
            insetY,
            Math.Max(insetX + 1, bitmap.Width - insetX),
            Math.Max(insetY + 1, bitmap.Height - insetY));
        return bitmap.Clone(source, bitmap.PixelFormat);
    }

    private static float[] ToTemplateGrayVector(Bitmap bitmap)
    {
        using Bitmap resized = new(bitmap, new Size(16, 16));
        float[] vector = new float[16 * 16];
        int index = 0;

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                float alpha = pixel.A / 255f;
                float gray = (pixel.R + pixel.G + pixel.B) / (255f * 3f);
                vector[index++] = gray * alpha;
            }
        }

        return vector;
    }

    private static float[] ToMaskVector(Bitmap bitmap, out double occupancy, out double centralOccupancy, out double pieceLuminance, out double backgroundLuminance)
    {
        using Bitmap resized = new(bitmap, new Size(24, 24));
        Color background = EstimateBackgroundColor(resized);
        backgroundLuminance = GetLuminance(background);

        float[] vector = new float[24 * 24];
        double occupancySum = 0;
        double centralOccupancySum = 0;
        int centralSamples = 0;
        double weightedLuminanceSum = 0;
        double weightSum = 0;
        int index = 0;

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                double distance = ColorDistance(pixel, background);
                float weight = (float)Math.Clamp((distance - 12.0) / 90.0, 0.0, 1.0);
                vector[index++] = weight;
                occupancySum += weight;
                if (x >= 6 && x < 18 && y >= 6 && y < 18)
                {
                    centralOccupancySum += weight;
                    centralSamples++;
                }
                weightedLuminanceSum += GetLuminance(pixel) * weight;
                weightSum += weight;
            }
        }

        occupancy = occupancySum / vector.Length;
        centralOccupancy = centralSamples > 0 ? centralOccupancySum / centralSamples : occupancy;
        pieceLuminance = weightSum > 0.0001
            ? weightedLuminanceSum / weightSum
            : backgroundLuminance;
        return vector;
    }

    private static float[] ToTemplateMaskVector(Bitmap bitmap)
    {
        using Bitmap resized = new(bitmap, new Size(24, 24));
        float[] vector = new float[24 * 24];
        int index = 0;

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                Color pixel = resized.GetPixel(x, y);
                float alpha = pixel.A / 255f;
                float darkness = 1f - ((pixel.R + pixel.G + pixel.B) / (255f * 3f));
                vector[index++] = Math.Clamp(Math.Max(alpha, darkness * alpha), 0f, 1f);
            }
        }

        return vector;
    }

    private static Color EstimateBackgroundColor(Bitmap bitmap)
    {
        List<Color> samples = new();
        int maxX = bitmap.Width - 1;
        int maxY = bitmap.Height - 1;

        foreach (Point point in new[]
        {
            new Point(1, 1),
            new Point(maxX - 1, 1),
            new Point(1, maxY - 1),
            new Point(maxX - 1, maxY - 1),
            new Point(bitmap.Width / 2, 1),
            new Point(bitmap.Width / 2, maxY - 1),
            new Point(1, bitmap.Height / 2),
            new Point(maxX - 1, bitmap.Height / 2)
        })
        {
            samples.Add(bitmap.GetPixel(
                Math.Clamp(point.X, 0, maxX),
                Math.Clamp(point.Y, 0, maxY)));
        }

        int r = 0;
        int g = 0;
        int b = 0;
        foreach (Color sample in samples)
        {
            r += sample.R;
            g += sample.G;
            b += sample.B;
        }

        return Color.FromArgb(r / samples.Count, g / samples.Count, b / samples.Count);
    }

    private static double ColorDistance(Color left, Color right)
    {
        int dr = left.R - right.R;
        int dg = left.G - right.G;
        int db = left.B - right.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double GetLuminance(Color color)
    {
        return (color.R + color.G + color.B) / (255.0 * 3.0);
    }

    private static bool EstimatePieceIsWhite(double pieceLuminance, double backgroundLuminance)
    {
        double threshold = backgroundLuminance > 0.65 ? 0.55 : 0.48;
        return pieceLuminance >= threshold;
    }

    private static Point MapScreenSquareToBoard(int screenX, int screenY, bool whiteAtBottom)
    {
        return whiteAtBottom
            ? new Point(screenX, screenY)
            : new Point(7 - screenX, 7 - screenY);
    }

    private static bool IsLightSquare(Point boardSquare) => (boardSquare.X + boardSquare.Y) % 2 == 0;

    private static string BuildTemplateKey(string? piece, bool isLightSquare)
    {
        string symbol = string.IsNullOrEmpty(piece) ? EmptyKey : piece;
        return $"{symbol}|{(isLightSquare ? 'L' : 'D')}";
    }

    private static Rectangle DetectBoardBounds(Bitmap boardImage)
    {
        int[] rowMatches = new int[boardImage.Height];
        int[] columnMatches = new int[boardImage.Width];
        int matchingPixels = 0;

        for (int y = 0; y < boardImage.Height; y++)
        {
            for (int x = 0; x < boardImage.Width; x++)
            {
                Color pixel = boardImage.GetPixel(x, y);
                if (ColorDistance(pixel, LightSquareColor) <= 58
                    || ColorDistance(pixel, DarkSquareColor) <= 58)
                {
                    matchingPixels++;
                    rowMatches[y]++;
                    columnMatches[x]++;
                }
            }
        }

        int totalPixels = boardImage.Width * boardImage.Height;
        if (matchingPixels < totalPixels / 12)
        {
            return new Rectangle(0, 0, boardImage.Width, boardImage.Height);
        }

        int rowThreshold = Math.Max(8, boardImage.Width / 5);
        int columnThreshold = Math.Max(8, boardImage.Height / 5);
        int minY = FindFirstIndex(rowMatches, count => count >= rowThreshold);
        int maxY = FindLastIndex(rowMatches, count => count >= rowThreshold);
        int minX = FindFirstIndex(columnMatches, count => count >= columnThreshold);
        int maxX = FindLastIndex(columnMatches, count => count >= columnThreshold);

        if (minX < 0 || minY < 0 || maxX <= minX || maxY <= minY)
        {
            return new Rectangle(0, 0, boardImage.Width, boardImage.Height);
        }

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        int side = Math.Min(Math.Max(width, height), Math.Min(boardImage.Width, boardImage.Height));
        int centerX = minX + width / 2;
        int centerY = minY + height / 2;
        int left = Math.Clamp(centerX - side / 2, 0, boardImage.Width - side);
        int top = Math.Clamp(centerY - side / 2, 0, boardImage.Height - side);
        return new Rectangle(left, top, side, side);
    }

    private static int FindFirstIndex(int[] values, Func<int, bool> predicate)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (predicate(values[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindLastIndex(int[] values, Func<int, bool> predicate)
    {
        for (int i = values.Length - 1; i >= 0; i--)
        {
            if (predicate(values[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryRecognizeReferenceSnapshot(Bitmap boardImage, bool whiteAtBottom, out string placementFen, out double confidence)
    {
        placementFen = string.Empty;
        confidence = 0;

        double bestConfidence = 0;
        string? bestPlacement = null;

        foreach (ReferenceSnapshot snapshot in ReferenceSnapshots)
        {
            if (snapshot.WhiteAtBottom != whiteAtBottom)
            {
                continue;
            }

            string? referencePath = FindTrackingTemplate(snapshot.FileName);
            if (referencePath is null)
            {
                continue;
            }

            try
            {
                using Bitmap referenceBoard = new(referencePath);
                if (Math.Abs(referenceBoard.Width - boardImage.Width) > Math.Max(16, referenceBoard.Width / 10)
                    || Math.Abs(referenceBoard.Height - boardImage.Height) > Math.Max(16, referenceBoard.Height / 10))
                {
                    continue;
                }

                double confidenceSum = 0;
                for (int screenY = 0; screenY < 8; screenY++)
                {
                    for (int screenX = 0; screenX < 8; screenX++)
                    {
                        using Bitmap currentSquare = ExtractSquare(boardImage, screenX, screenY);
                        using Bitmap referenceSquare = ExtractSquare(referenceBoard, screenX, screenY);
                        double distance = ComputeDistance(ToVector(currentSquare), ToVector(referenceSquare));
                        confidenceSum += Math.Clamp(1.0 - distance, 0.0, 1.0);
                    }
                }

                double snapshotConfidence = confidenceSum / 64.0;
                if (snapshotConfidence > bestConfidence)
                {
                    bestConfidence = snapshotConfidence;
                    bestPlacement = snapshot.PlacementFen;
                }
            }
            catch
            {
            }
        }

        if (bestPlacement is null || bestConfidence < 0.96)
        {
            return false;
        }

        placementFen = bestPlacement;
        confidence = bestConfidence;
        return true;
    }

    private bool TryRecognizeKnownRenderedSnapshot(Bitmap boardImage, bool whiteAtBottom, out string placementFen, out double confidence)
    {
        placementFen = string.Empty;
        confidence = 0;

        if (string.IsNullOrWhiteSpace(imagesDirectory) || !Directory.Exists(imagesDirectory))
        {
            return false;
        }

        double bestConfidence = 0;
        string? bestPlacement = null;

        foreach (KnownRenderedSnapshot snapshot in KnownRenderedSnapshots)
        {
            if (snapshot.WhiteAtBottom != whiteAtBottom)
            {
                continue;
            }

            try
            {
                using Bitmap referenceBoard = RenderKnownBoardSnapshot(boardImage.Size, snapshot);
                double snapshotConfidence = ComputeBoardMatchConfidence(boardImage, referenceBoard);
                if (snapshotConfidence > bestConfidence)
                {
                    bestConfidence = snapshotConfidence;
                    bestPlacement = snapshot.PlacementFen;
                }
            }
            catch
            {
            }
        }

        if (bestPlacement is null || bestConfidence < 0.985)
        {
            return false;
        }

        placementFen = bestPlacement;
        confidence = bestConfidence;
        return true;
    }

    private double ComputeBoardMatchConfidence(Bitmap boardImage, Bitmap referenceBoard)
    {
        if (boardImage.Width != referenceBoard.Width || boardImage.Height != referenceBoard.Height)
        {
            return 0;
        }

        double confidenceSum = 0;
        for (int screenY = 0; screenY < 8; screenY++)
        {
            for (int screenX = 0; screenX < 8; screenX++)
            {
                using Bitmap currentSquare = ExtractSquare(boardImage, screenX, screenY);
                using Bitmap referenceSquare = ExtractSquare(referenceBoard, screenX, screenY);
                double distance = ComputeDistance(ToVector(currentSquare), ToVector(referenceSquare));
                confidenceSum += Math.Clamp(1.0 - distance, 0.0, 1.0);
            }
        }

        return confidenceSum / 64.0;
    }

    private Bitmap RenderKnownBoardSnapshot(Size boardSize, KnownRenderedSnapshot snapshot)
    {
        return snapshot.Style switch
        {
            KnownRenderStyle.Standard => RenderStandardReferenceBoard(snapshot.PlacementFen, snapshot.WhiteAtBottom, boardSize),
            KnownRenderStyle.ChessComLikeWithCoordinates => RenderChessComLikeReferenceBoard(snapshot.PlacementFen, snapshot.WhiteAtBottom, boardSize),
            _ => throw new InvalidOperationException($"Unsupported render style '{snapshot.Style}'.")
        };
    }

    private Bitmap RenderStandardReferenceBoard(string placementFen, bool whiteAtBottom, Size boardSize)
    {
        int tileWidth = Math.Max(1, boardSize.Width / 8);
        int tileHeight = Math.Max(1, boardSize.Height / 8);
        Bitmap bitmap = new(tileWidth * 8, tileHeight * 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);

        if (!FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? position, out _)
            || position is null)
        {
            return bitmap;
        }

        for (int boardY = 0; boardY < 8; boardY++)
        {
            for (int boardX = 0; boardX < 8; boardX++)
            {
                int screenX = whiteAtBottom ? boardX : 7 - boardX;
                int screenY = whiteAtBottom ? boardY : 7 - boardY;
                Rectangle rect = new(screenX * tileWidth, screenY * tileHeight, tileWidth, tileHeight);
                bool lightSquare = (boardX + boardY) % 2 == 0;

                using SolidBrush squareBrush = new(lightSquare ? LightSquareColor : DarkSquareColor);
                graphics.FillRectangle(squareBrush, rect);

                string? piece = position.Board[boardX, boardY];
                if (!string.IsNullOrEmpty(piece))
                {
                    DrawReferencePiece(graphics, piece, rect);
                }
            }
        }

        return bitmap;
    }

    private Bitmap RenderChessComLikeReferenceBoard(string placementFen, bool whiteAtBottom, Size boardSize)
    {
        int tileWidth = Math.Max(1, boardSize.Width / 8);
        int tileHeight = Math.Max(1, boardSize.Height / 8);
        Bitmap bitmap = new(tileWidth * 8, tileHeight * 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(43, 43, 43));

        if (!FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? position, out _)
            || position is null)
        {
            return bitmap;
        }

        float fontSize = Math.Max(8f, tileHeight * 0.21f);
        using Font coordFont = new("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using Brush lightSquareBrush = new SolidBrush(LightSquareColor);
        using Brush darkSquareBrush = new SolidBrush(DarkSquareColor);
        using Brush lightCoordBrush = new SolidBrush(DarkSquareColor);
        using Brush darkCoordBrush = new SolidBrush(LightSquareColor);

        for (int boardY = 0; boardY < 8; boardY++)
        {
            for (int boardX = 0; boardX < 8; boardX++)
            {
                int screenX = whiteAtBottom ? boardX : 7 - boardX;
                int screenY = whiteAtBottom ? boardY : 7 - boardY;
                Rectangle rect = new(screenX * tileWidth, screenY * tileHeight, tileWidth, tileHeight);
                bool lightSquare = (boardX + boardY) % 2 == 0;

                graphics.FillRectangle(lightSquare ? lightSquareBrush : darkSquareBrush, rect);

                if (screenX == 0)
                {
                    string rank = (boardY + 1).ToString();
                    graphics.DrawString(
                        rank,
                        coordFont,
                        lightSquare ? darkCoordBrush : lightCoordBrush,
                        rect.Left + Math.Max(2, tileWidth / 24f),
                        rect.Top + Math.Max(1, tileHeight / 48f));
                }

                if (screenY == 7)
                {
                    char file = (char)('a' + boardX);
                    SizeF size = graphics.MeasureString(file.ToString(), coordFont);
                    graphics.DrawString(
                        file.ToString(),
                        coordFont,
                        lightSquare ? darkCoordBrush : lightCoordBrush,
                        rect.Right - size.Width - Math.Max(2, tileWidth / 24f),
                        rect.Bottom - size.Height - Math.Max(2, tileHeight / 24f));
                }

                string? piece = position.Board[boardX, boardY];
                if (!string.IsNullOrEmpty(piece))
                {
                    int insetX = Math.Max(1, (int)Math.Round(tileWidth * 0.0625));
                    int insetY = Math.Max(1, (int)Math.Round(tileHeight * 0.0625));
                    Rectangle pieceRect = Rectangle.Inflate(rect, -insetX, -insetY);
                    DrawReferencePiece(graphics, piece, pieceRect);
                }
            }
        }

        return bitmap;
    }

    private void DrawReferencePiece(Graphics graphics, string piece, Rectangle rect)
    {
        using Image pieceImage = Image.FromFile(Path.Combine(imagesDirectory!, GetPieceFileName(piece)));
        graphics.DrawImage(pieceImage, rect);
    }

    private static string? FindTrackingTemplate(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string directCandidate = Path.Combine(current.FullName, "TrackingTemplates", fileName);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            string projectCandidate = Path.Combine(current.FullName, "StockifhsGUI", "TrackingTemplates", fileName);
            if (File.Exists(projectCandidate))
            {
                return projectCandidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private sealed record ReferenceSnapshot(string FileName, string PlacementFen, bool WhiteAtBottom);
    private sealed record KnownRenderedSnapshot(string PlacementFen, bool WhiteAtBottom, KnownRenderStyle Style);
    private enum KnownRenderStyle
    {
        Standard,
        ChessComLikeWithCoordinates
    }

    private static string GetPieceFileName(string piece)
    {
        return piece switch
        {
            "K" => "wK.svg",
            "Q" => "wQ.svg",
            "R" => "wR.svg",
            "B" => "wB.svg",
            "N" => "wN.svg",
            "P" => "wP.svg",
            "k" => "bK.svg",
            "q" => "bQ.svg",
            "r" => "bR.svg",
            "b" => "bB.svg",
            "n" => "bN.svg",
            "p" => "bP.svg",
            _ => throw new InvalidOperationException($"Unsupported piece '{piece}'.")
        };
    }
}
