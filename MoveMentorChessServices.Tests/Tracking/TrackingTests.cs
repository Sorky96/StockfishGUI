using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class TrackingTests
{
    [Fact]
    public void ParseSanMoves_NormalizesCastleAndKeepsCheckSuffixes()
    {
        string text = """
1. e4 e5
2. Nf3 Nc6
3. Bb5 a6
4. 0-0 Nf6
5. Re1 Be7
6. Qe2?!
7. Bxc6+ bxc6
""";

        List<string> moves = MoveListOcrRecognizer.ParseSanMoves(text);

        Assert.Contains("O-O", moves);
        Assert.Contains("Bxc6+", moves);
        Assert.DoesNotContain("0-0", moves);
    }

    [Fact]
    public void BoardPositionRecognizer_LearnsAndRecognizesUpdatedPosition()
    {
        const string startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        const string e4Placement = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR";

        using Bitmap seedBoard = RenderBoard(startPlacement, whiteAtBottom: true);
        using Bitmap trackedBoard = RenderBoard(e4Placement, whiteAtBottom: true);

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        recognizer.LearnFromBoard(seedBoard, startPlacement, whiteAtBottom: true);

        bool recognized = recognizer.TryRecognize(trackedBoard, whiteAtBottom: true, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(e4Placement, placementFen);
        Assert.True(confidence > 0.6);
    }

    [Fact]
    public void BoardPositionRecognizer_RecognizesBoardWithoutCalibration()
    {
        const string e4Placement = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR";

        using Bitmap trackedBoard = RenderBoard(e4Placement, whiteAtBottom: true);

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        bool recognized = recognizer.TryRecognizeColdStart(trackedBoard, whiteAtBottom: true, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(e4Placement, placementFen);
        Assert.True(confidence > 0.38);
    }

    [Fact]
    public void BoardPositionRecognizer_RecognizesChessComLikeRotatedBoardWithCoordinates()
    {
        const string startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

        using Bitmap trackedBoard = RenderChessComLikeBoard(startPlacement, whiteAtBottom: false, showCoordinates: true);

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        bool recognized = recognizer.TryRecognizeColdStart(trackedBoard, whiteAtBottom: false, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(startPlacement, placementFen);
        Assert.True(confidence > 0.30);
    }

    [Fact]
    public void BoardPositionRecognizer_RecognizesRealScreenFixture()
    {
        const string d4Placement = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR";

        using Bitmap trackedBoard = LoadFixtureBitmap("TestScreen.png");

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        bool recognized = recognizer.TryRecognizeColdStart(trackedBoard, whiteAtBottom: false, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(d4Placement, placementFen);
        Assert.True(confidence > 0.30);
    }

    [Fact]
    public void BoardPositionRecognizer_RecognizesSecondRealScreenFixture()
    {
        const string bc4Placement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";

        using Bitmap trackedBoard = LoadFixtureBitmap("TestScreen2.png");

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        bool recognized = recognizer.TryRecognizeColdStart(trackedBoard, whiteAtBottom: true, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(bc4Placement, placementFen);
        Assert.True(confidence > 0.30);
    }

    [Fact]
    public void BoardPositionRecognizer_RecognizesSecondRealScreenFixtureWithPadding()
    {
        const string bc4Placement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";

        using Bitmap trackedBoard = LoadFixtureBitmap("TestScreen2.png");
        using Bitmap padded = new(trackedBoard.Width + 120, trackedBoard.Height + 100);
        using (Graphics graphics = Graphics.FromImage(padded))
        {
            graphics.Clear(Color.FromArgb(32, 32, 32));
            graphics.DrawImage(trackedBoard, 60, 40, trackedBoard.Width, trackedBoard.Height);
        }

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        bool recognized = recognizer.TryRecognizeColdStart(padded, whiteAtBottom: true, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(bc4Placement, placementFen);
        Assert.True(confidence > 0.30);
    }

    [Fact]
    public void BoardPositionRecognizer_RoundTripsRealScreenFixtureAfterLearning()
    {
        const string d4Placement = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR";

        using Bitmap trackedBoard = LoadFixtureBitmap("TestScreen.png");

        BoardPositionRecognizer recognizer = new(GetImagesDirectory());
        recognizer.LearnFromBoard(trackedBoard, d4Placement, whiteAtBottom: false);

        bool recognized = recognizer.TryRecognize(trackedBoard, whiteAtBottom: false, out string placementFen, out double confidence);

        Assert.True(recognized);
        Assert.Equal(d4Placement, placementFen);
        Assert.True(confidence > 0.30);
    }

    [Fact]
    public void TrackingCoordinator_AcceptsIncrementalBoardMoveAfterInitialSync()
    {
        const string startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string e4Placement = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR";
        const string e4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        FakeMoveListRecognizer moveListRecognizer = new(new[]
        {
            new MoveListRecognitionResult(startFen, startPlacement, Array.Empty<string>(), 0.95, DateTime.UtcNow)
        });

        TrackingCoordinator coordinator = new(
            moveListRecognizer,
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        TrackingProfile profile = new((nint)123, "Chess.com", new Rectangle(0, 0, 400, 400), new Rectangle(0, 0, 200, 300), true, false);

        using Bitmap startBoard = RenderBoard(startPlacement, whiteAtBottom: true);
        using Bitmap moveListImage = new(200, 300);

        _ = coordinator.ProcessFrame(startBoard, moveListImage, profile);
        TrackingUpdate initialSync = coordinator.ProcessFrame(startBoard, moveListImage, profile);

        Assert.NotNull(initialSync.Snapshot);
        Assert.Equal(startFen, initialSync.Snapshot!.Fen);

        using Bitmap e4Board = RenderBoard(e4Placement, whiteAtBottom: true);

        _ = coordinator.ProcessFrame(e4Board, moveListImage, profile);
        TrackingUpdate incremental = coordinator.ProcessFrame(e4Board, moveListImage, profile);

        Assert.NotNull(incremental.Snapshot);
        Assert.Equal(e4Fen, incremental.Snapshot!.Fen);
        Assert.Equal(TrackerStatusKind.Tracking, incremental.Status.Kind);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeTracksSingleLegalMove()
    {
        const string startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        const string startFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string e4Placement = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR";
        const string e4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        TrackingProfile profile = new((nint)123, "Chess.com", new Rectangle(0, 0, 400, 400), Rectangle.Empty, true, true);

        using Bitmap startBoard = RenderBoard(startPlacement, whiteAtBottom: true);
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(startBoard, startFen, whiteAtBottom: true);
        Assert.NotNull(initialization.Snapshot);

        _ = coordinator.ProcessFrame(startBoard, startBoard, profile);
        TrackingUpdate initial = coordinator.ProcessFrame(startBoard, startBoard, profile);
        Assert.NotNull(initial.Snapshot);
        Assert.Equal(startFen, initial.Snapshot!.Fen);

        using Bitmap e4Board = RenderBoard(e4Placement, whiteAtBottom: true);
        _ = coordinator.ProcessFrame(e4Board, e4Board, profile);
        TrackingUpdate update = coordinator.ProcessFrame(e4Board, e4Board, profile);

        Assert.NotNull(update.Snapshot);
        Assert.Equal(e4Fen, update.Snapshot!.Fen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeInitializesFromDifferentCurrentBoard()
    {
        const string seedFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        const string midgamePlacement = "r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        using Bitmap board = RenderBoard(midgamePlacement, whiteAtBottom: true);
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: true);

        Assert.NotNull(initialization.Snapshot);
        Assert.Equal(midgamePlacement, initialization.Snapshot!.PlacementFen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeInitializesFromChessComLikeRotatedBoard()
    {
        const string seedFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        const string startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        using Bitmap board = RenderChessComLikeBoard(startPlacement, whiteAtBottom: false, showCoordinates: true);
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: false);

        Assert.NotNull(initialization.Snapshot);
        Assert.Equal(startPlacement, initialization.Snapshot!.PlacementFen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeInitializesFromRealScreenFixture()
    {
        const string seedFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        const string d4Placement = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        using Bitmap board = LoadFixtureBitmap("TestScreen.png");
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: false);

        Assert.NotNull(initialization.Snapshot);
        Assert.Equal(d4Placement, initialization.Snapshot!.PlacementFen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeInitializesFromSecondRealScreenFixture()
    {
        const string seedFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        const string bc4Placement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        using Bitmap board = LoadFixtureBitmap("TestScreen2.png");
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: true);

        Assert.NotNull(initialization.Snapshot);
        Assert.Equal(bc4Placement, initialization.Snapshot!.PlacementFen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeKeepsSecondRealScreenFixtureStableAcrossPolls()
    {
        const string seedFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        const string bc4Placement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        TrackingProfile profile = new((nint)123, "Chess.com", new Rectangle(0, 0, 400, 400), Rectangle.Empty, true, true);

        using Bitmap board = LoadFixtureBitmap("TestScreen2.png");
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: true);
        Assert.NotNull(initialization.Snapshot);
        Assert.Equal(bc4Placement, initialization.Snapshot!.PlacementFen);

        _ = coordinator.ProcessFrame(board, board, profile);
        TrackingUpdate stable = coordinator.ProcessFrame(board, board, profile);

        Assert.NotNull(stable.Snapshot);
        Assert.Equal(bc4Placement, stable.Snapshot!.PlacementFen);
    }

    [Fact]
    public void TrackingCoordinator_BoardOnlyModeIgnoresImplausibleMultiPieceLoss()
    {
        const string seedFen = "8/8/8/8/8/8/8/8 w - - 0 1";
        const string bc4Placement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR";
        const string implausiblePlacement = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PP1P1P1P/RNBQK1NR";

        TrackingCoordinator coordinator = new(
            new FakeMoveListRecognizer(Array.Empty<MoveListRecognitionResult>()),
            new BoardPositionRecognizer(GetImagesDirectory()),
            new ScreenCaptureService());

        using Bitmap board = LoadFixtureBitmap("TestScreen2.png");
        TrackingUpdate initialization = coordinator.InitializeBoardOnly(board, seedFen, whiteAtBottom: true);
        Assert.NotNull(initialization.Snapshot);

        MethodInfo helper = typeof(TrackingCoordinator).GetMethod(
            "ProcessSyntheticBoardOnlyPlacementForTests",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        TrackingUpdate rejected = (TrackingUpdate)helper.Invoke(
            coordinator,
            new object[] { initialization.Snapshot!.Fen, implausiblePlacement })!;

        Assert.NotNull(rejected.Snapshot);
        Assert.Equal(bc4Placement, rejected.Snapshot!.PlacementFen);
        Assert.Equal(TrackerStatusKind.WaitingForStableFrame, rejected.Status.Kind);
    }

    private static Bitmap RenderBoard(string placementFen, bool whiteAtBottom)
    {
        const int tileSize = 48;
        Bitmap bitmap = new(tileSize * 8, tileSize * 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);

        Assert.True(FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? position, out _));
        Assert.NotNull(position);

        for (int boardY = 0; boardY < 8; boardY++)
        {
            for (int boardX = 0; boardX < 8; boardX++)
            {
                int screenX = whiteAtBottom ? boardX : 7 - boardX;
                int screenY = whiteAtBottom ? boardY : 7 - boardY;
                Rectangle rect = new(screenX * tileSize, screenY * tileSize, tileSize, tileSize);
                bool lightSquare = (boardX + boardY) % 2 == 0;

                using SolidBrush squareBrush = new(lightSquare ? Color.FromArgb(238, 238, 210) : Color.FromArgb(118, 150, 86));
                graphics.FillRectangle(squareBrush, rect);

                string? piece = position!.Board[boardX, boardY];
                if (!string.IsNullOrEmpty(piece))
                {
                    DrawPiece(graphics, piece, rect);
                }
            }
        }

        return bitmap;
    }

    private static Bitmap RenderChessComLikeBoard(string placementFen, bool whiteAtBottom, bool showCoordinates)
    {
        const int tileSize = 96;
        Bitmap bitmap = new(tileSize * 8, tileSize * 8);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(43, 43, 43));

        Assert.True(FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? position, out _));
        Assert.NotNull(position);

        using Font coordFont = new("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using Brush lightSquareBrush = new SolidBrush(Color.FromArgb(238, 238, 210));
        using Brush darkSquareBrush = new SolidBrush(Color.FromArgb(118, 150, 86));
        using Brush lightCoordBrush = new SolidBrush(Color.FromArgb(118, 150, 86));
        using Brush darkCoordBrush = new SolidBrush(Color.FromArgb(238, 238, 210));

        for (int boardY = 0; boardY < 8; boardY++)
        {
            for (int boardX = 0; boardX < 8; boardX++)
            {
                int screenX = whiteAtBottom ? boardX : 7 - boardX;
                int screenY = whiteAtBottom ? boardY : 7 - boardY;
                Rectangle rect = new(screenX * tileSize, screenY * tileSize, tileSize, tileSize);
                bool lightSquare = (boardX + boardY) % 2 == 0;

                graphics.FillRectangle(lightSquare ? lightSquareBrush : darkSquareBrush, rect);

                if (showCoordinates)
                {
                    if (screenX == 0)
                    {
                        string rank = (boardY + 1).ToString();
                        graphics.DrawString(
                            rank,
                            coordFont,
                            lightSquare ? darkCoordBrush : lightCoordBrush,
                            rect.Left + 4,
                            rect.Top + 2);
                    }

                    if (screenY == 7)
                    {
                        char file = (char)('a' + boardX);
                        SizeF size = graphics.MeasureString(file.ToString(), coordFont);
                        graphics.DrawString(
                            file.ToString(),
                            coordFont,
                            lightSquare ? darkCoordBrush : lightCoordBrush,
                            rect.Right - size.Width - 4,
                            rect.Bottom - size.Height - 4);
                    }
                }

                string? piece = position!.Board[boardX, boardY];
                if (!string.IsNullOrEmpty(piece))
                {
                    Rectangle pieceRect = Rectangle.Inflate(rect, -6, -6);
                    DrawPiece(graphics, piece, pieceRect);
                }
            }
        }

        return bitmap;
    }

    private static void DrawPiece(Graphics graphics, string piece, Rectangle rect)
    {
        using Image pieceImage = Image.FromFile(Path.Combine(GetImagesDirectory(), GetPieceFileName(piece)));
        graphics.DrawImage(pieceImage, rect);
    }

    private static Bitmap LoadFixtureBitmap(string fileName)
    {
        return new Bitmap(Path.Combine(GetFixturesDirectory(), fileName));
    }

    private static string GetFixturesDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "Fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "TestScreen.png")))
            {
                return candidate;
            }

            candidate = Path.Combine(current.FullName, "MoveMentorChessServices.Tests", "Fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "TestScreen.png")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the test fixture directory.");
    }

    private static string GetImagesDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "MoveMentorChessServices", "Images");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(current.FullName, "Images");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "wK.svg")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Images directory for chess piece fixtures.");
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

    private sealed class FakeMoveListRecognizer : IMoveListRecognizer
    {
        private readonly Queue<MoveListRecognitionResult> results;

        public FakeMoveListRecognizer(IEnumerable<MoveListRecognitionResult> results)
        {
            this.results = new Queue<MoveListRecognitionResult>(results);
        }

        public bool TryRecognize(Bitmap source, out MoveListRecognitionResult? result, out string? error)
        {
            if (results.Count == 0)
            {
                result = null;
                error = "No more fake OCR results.";
                return false;
            }

            result = results.Dequeue();
            error = null;
            return true;
        }
    }
}
