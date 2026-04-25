using System;
using System.Collections.Generic;
using System.Drawing;

namespace MoveMentorChessServices;

public sealed class TrackingCoordinator
{
    private readonly IMoveListRecognizer moveListRecognizer;
    private readonly BoardPositionRecognizer boardRecognizer;
    private readonly ScreenCaptureService captureService;

    private string? lastObservedHash;
    private int stableFrameCount;
    private string? lastAcceptedHash;
    private TrackedPositionSnapshot? lastAcceptedSnapshot;
    private string? boardOnlySeedFen;

    public TrackingCoordinator(
        IMoveListRecognizer moveListRecognizer,
        BoardPositionRecognizer boardRecognizer,
        ScreenCaptureService captureService)
    {
        this.moveListRecognizer = moveListRecognizer;
        this.boardRecognizer = boardRecognizer;
        this.captureService = captureService;
    }

    public TrackingUpdate ProcessFrame(Bitmap boardImage, Bitmap moveListImage, TrackingProfile profile)
    {
        using Bitmap normalizedBoardImage = boardRecognizer.NormalizeBoardImage(boardImage);
        string combinedHash = $"{captureService.ComputeHash(normalizedBoardImage)}|{captureService.ComputeHash(moveListImage)}";
        if (!string.Equals(combinedHash, lastObservedHash, StringComparison.Ordinal))
        {
            lastObservedHash = combinedHash;
            stableFrameCount = 1;
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Waiting for a stable board frame..."),
                null);
        }

        stableFrameCount++;
        if (stableFrameCount < 2)
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Stabilizing the current frame..."),
                null);
        }

        if (string.Equals(combinedHash, lastAcceptedHash, StringComparison.Ordinal))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Tracking, "Tracking active. Waiting for a new move..."),
                lastAcceptedSnapshot);
        }

        if (profile.BoardOnly)
        {
            if (lastAcceptedSnapshot is null)
            {
                return TryInitializeBoardOnlyFromFrame(normalizedBoardImage, combinedHash, profile.WhiteAtBottom);
            }

            return ProcessBoardOnlyResolvedFrame(normalizedBoardImage, combinedHash, profile);
        }

        if (boardRecognizer.HasTemplates
            && boardRecognizer.TryRecognize(normalizedBoardImage, profile.WhiteAtBottom, out string incrementalPlacementFen, out double boardConfidence)
            && lastAcceptedSnapshot is not null
            && TryResolveNextFen(lastAcceptedSnapshot.Fen, incrementalPlacementFen, out string incrementalFen))
        {
            TrackedPositionSnapshot incrementalSnapshot = new(
                incrementalFen,
                incrementalPlacementFen,
                boardConfidence,
                DateTime.UtcNow,
                lastAcceptedSnapshot.Moves);

            lastAcceptedHash = combinedHash;
            lastAcceptedSnapshot = incrementalSnapshot;
            boardRecognizer.LearnFromBoard(normalizedBoardImage, incrementalPlacementFen, profile.WhiteAtBottom);

            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Tracking, "Tracked a new position from the board."),
                incrementalSnapshot);
        }

        if (!moveListRecognizer.TryRecognize(moveListImage, out MoveListRecognitionResult? moveListResult, out string? ocrError)
            || moveListResult is null)
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Unsupported, $"Move-list OCR unavailable: {ocrError ?? "unknown error"}"),
                null);
        }

        boardRecognizer.LearnFromBoard(normalizedBoardImage, moveListResult.PlacementFen, profile.WhiteAtBottom);

        bool boardMatched = boardRecognizer.TryRecognize(normalizedBoardImage, profile.WhiteAtBottom, out string recognizedPlacementFen, out double recognizedConfidence)
            && string.Equals(recognizedPlacementFen, moveListResult.PlacementFen, StringComparison.Ordinal);

        if (!boardMatched && lastAcceptedSnapshot is not null)
        {
            return new TrackingUpdate(
                new TrackerStatus(
                    TrackerStatusKind.Mismatch,
                    $"Board and OCR disagree for window '{profile.WindowTitle}'."),
                null);
        }

        TrackedPositionSnapshot acceptedSnapshot = new(
            moveListResult.Fen,
            moveListResult.PlacementFen,
            boardMatched ? (moveListResult.Confidence + recognizedConfidence) / 2.0 : moveListResult.Confidence,
            moveListResult.SourceTimestamp,
            moveListResult.Moves);

        lastAcceptedHash = combinedHash;
        lastAcceptedSnapshot = acceptedSnapshot;

        return new TrackingUpdate(
            new TrackerStatus(TrackerStatusKind.Tracking, "Tracking active. Position synchronized."),
            acceptedSnapshot);
    }

    public TrackingUpdate InitializeBoardOnly(Bitmap boardImage, string metadataSeedFen, bool whiteAtBottom)
    {
        boardOnlySeedFen = metadataSeedFen;
        using Bitmap normalizedBoardImage = boardRecognizer.NormalizeBoardImage(boardImage);
        if (!boardRecognizer.TryRecognizeColdStart(normalizedBoardImage, whiteAtBottom, out string placementFen, out double confidence))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracking is calibrating. Keep the board visible for a moment."),
                null);
        }

        string synchronizedFen = BuildInitialBoardOnlyFen(metadataSeedFen, placementFen);
        if (!FenPosition.TryParse(synchronizedFen, out FenPosition? position, out _)
            || position is null)
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Unsupported, "Board-only tracking recognized a board, but could not build a valid FEN."),
                null);
        }

        boardRecognizer.LearnFromBoard(normalizedBoardImage, placementFen, whiteAtBottom);

        TrackedPositionSnapshot snapshot = new(
            position.GetFen(),
            placementFen,
            confidence,
            DateTime.UtcNow,
            Array.Empty<string>());
        lastAcceptedSnapshot = snapshot;
        lastAcceptedHash = null;
        lastObservedHash = null;
        stableFrameCount = 0;

        return new TrackingUpdate(
            new TrackerStatus(TrackerStatusKind.Tracking, "Board-only tracking synchronized the current board from screen."),
            snapshot);
    }

    internal TrackingUpdate ProcessSyntheticBoardOnlyPlacementForTests(string currentFen, string placementFen)
    {
        lastAcceptedSnapshot = new TrackedPositionSnapshot(
            currentFen,
            FenPosition.TryParse(currentFen, out FenPosition? position, out _) && position is not null
                ? position.GetPlacementFen()
                : placementFen,
            1.0,
            DateTime.UtcNow,
            Array.Empty<string>());

        if (TryResolveNextFen(lastAcceptedSnapshot.Fen, placementFen, out string inferredFen))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Tracking, "Board-only tracking inferred the next legal move."),
                new TrackedPositionSnapshot(inferredFen, placementFen, 1.0, DateTime.UtcNow, Array.Empty<string>()));
        }

        if (!IsPlausibleSingleFrameLayoutUpdate(lastAcceptedSnapshot.PlacementFen, placementFen))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracker ignored an implausible board change and is waiting for a cleaner frame."),
                lastAcceptedSnapshot);
        }

        return new TrackingUpdate(
            new TrackerStatus(TrackerStatusKind.Tracking, "Board-only tracking updated the piece layout. Side to move and castling rights were preserved."),
            new TrackedPositionSnapshot(
                ReplacePlacementPreservingState(lastAcceptedSnapshot.Fen, placementFen),
                placementFen,
                1.0,
                DateTime.UtcNow,
                Array.Empty<string>()));
    }

    private TrackingUpdate TryInitializeBoardOnlyFromFrame(Bitmap boardImage, string combinedHash, bool whiteAtBottom)
    {
        string seedFen = boardOnlySeedFen ?? "8/8/8/8/8/8/8/8 w - - 0 1";
        if (!boardRecognizer.TryRecognizeColdStart(boardImage, whiteAtBottom, out string placementFen, out double confidence))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracking is still looking for a recognizable board layout..."),
                null);
        }

        string synchronizedFen = BuildInitialBoardOnlyFen(seedFen, placementFen);
        if (!FenPosition.TryParse(synchronizedFen, out FenPosition? position, out _)
            || position is null)
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Unsupported, "Board-only tracking found the board, but could not build a valid FEN."),
                null);
        }

        boardRecognizer.LearnFromBoard(boardImage, placementFen, whiteAtBottom);

        TrackedPositionSnapshot snapshot = new(
            position.GetFen(),
            placementFen,
            confidence,
            DateTime.UtcNow,
            Array.Empty<string>());

        lastAcceptedHash = combinedHash;
        lastAcceptedSnapshot = snapshot;

        return new TrackingUpdate(
            new TrackerStatus(TrackerStatusKind.Tracking, "Board-only tracking synchronized the current board from screen."),
            snapshot);
    }

    private TrackingUpdate ProcessBoardOnlyResolvedFrame(Bitmap boardImage, string combinedHash, TrackingProfile profile)
    {
        if (lastAcceptedSnapshot is null)
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracking is waiting for the first synchronized position..."),
                null);
        }

        if (!boardRecognizer.TryRecognize(boardImage, profile.WhiteAtBottom, out string placementFen, out double confidence))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracker is still learning the piece templates."),
                null);
        }

        if (placementFen == lastAcceptedSnapshot.PlacementFen)
        {
            lastAcceptedHash = combinedHash;
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.Tracking, "Board-only tracking active. Waiting for a board change..."),
                lastAcceptedSnapshot);
        }

        string nextFen;
        string statusMessage;

        if (TryResolveNextFen(lastAcceptedSnapshot.Fen, placementFen, out string inferredFen))
        {
            nextFen = inferredFen;
            statusMessage = "Board-only tracking inferred the next legal move.";
        }
        else if (!IsPlausibleSingleFrameLayoutUpdate(lastAcceptedSnapshot.PlacementFen, placementFen))
        {
            return new TrackingUpdate(
                new TrackerStatus(TrackerStatusKind.WaitingForStableFrame, "Board-only tracker ignored an implausible board change and is waiting for a cleaner frame."),
                lastAcceptedSnapshot);
        }
        else
        {
            nextFen = ReplacePlacementPreservingState(lastAcceptedSnapshot.Fen, placementFen);
            statusMessage = "Board-only tracking updated the piece layout. Side to move and castling rights were preserved.";
        }

        TrackedPositionSnapshot snapshot = new(
            nextFen,
            placementFen,
            confidence,
            DateTime.UtcNow,
            Array.Empty<string>());

        lastAcceptedHash = combinedHash;
        lastAcceptedSnapshot = snapshot;
        boardRecognizer.LearnFromBoard(boardImage, placementFen, profile.WhiteAtBottom);

        return new TrackingUpdate(
            new TrackerStatus(TrackerStatusKind.Tracking, statusMessage),
            snapshot);
    }

    private static bool IsPlausibleSingleFrameLayoutUpdate(string previousPlacementFen, string currentPlacementFen)
    {
        if (!FenPosition.TryParse($"{previousPlacementFen} w - - 0 1", out FenPosition? previousPosition, out _)
            || previousPosition is null
            || !FenPosition.TryParse($"{currentPlacementFen} w - - 0 1", out FenPosition? currentPosition, out _)
            || currentPosition is null)
        {
            return false;
        }

        int previousWhite = 0;
        int previousBlack = 0;
        int currentWhite = 0;
        int currentBlack = 0;

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? previousPiece = previousPosition.Board[x, y];
                if (!string.IsNullOrEmpty(previousPiece))
                {
                    if (char.IsUpper(previousPiece[0]))
                    {
                        previousWhite++;
                    }
                    else
                    {
                        previousBlack++;
                    }
                }

                string? currentPiece = currentPosition.Board[x, y];
                if (!string.IsNullOrEmpty(currentPiece))
                {
                    if (char.IsUpper(currentPiece[0]))
                    {
                        currentWhite++;
                    }
                    else
                    {
                        currentBlack++;
                    }
                }
            }
        }

        return Math.Abs(previousWhite - currentWhite) <= 1
            && Math.Abs(previousBlack - currentBlack) <= 1;
    }

    private static bool TryResolveSingleLegalMove(string startFen, string targetPlacementFen, out string nextFen)
    {
        nextFen = string.Empty;

        ChessGame game = new();
        if (!game.TryLoadFen(startFen, out _))
        {
            return false;
        }

        List<string> matchingFens = new();
        foreach (string san in game.GetLegalSanMoves())
        {
            ChessGame candidate = new();
            if (!candidate.TryLoadFen(startFen, out _))
            {
                return false;
            }

            candidate.ApplySan(san);
            if (candidate.GetPlacementFen() == targetPlacementFen)
            {
                matchingFens.Add(candidate.GetFen());
            }
        }

        if (matchingFens.Count != 1)
        {
            return false;
        }

        nextFen = matchingFens[0];
        return true;
    }

    private static bool TryResolveNextFen(string startFen, string targetPlacementFen, out string nextFen)
    {
        if (TryResolveSingleLegalMove(startFen, targetPlacementFen, out nextFen))
        {
            return true;
        }

        if (!FenPosition.TryParse(startFen, out FenPosition? position, out _)
            || position is null)
        {
            return false;
        }

        string toggledFen = FenPosition.FromBoardState(
            position.Board,
            !position.WhiteToMove,
            position.WhiteKingMoved,
            position.BlackKingMoved,
            position.WhiteRookLeftMoved,
            position.WhiteRookRightMoved,
            position.BlackRookLeftMoved,
            position.BlackRookRightMoved,
            position.EnPassantTargetSquare,
            position.HalfmoveClock,
            position.FullmoveNumber).GetFen();

        return TryResolveSingleLegalMove(toggledFen, targetPlacementFen, out nextFen);
    }

    private static string BuildInitialBoardOnlyFen(string metadataSeedFen, string placementFen)
    {
        bool whiteToMove = true;
        int halfmoveClock = 0;
        int fullmoveNumber = 1;
        string? enPassantTargetSquare = null;

        if (FenPosition.TryParse(metadataSeedFen, out FenPosition? seed, out _)
            && seed is not null)
        {
            whiteToMove = seed.WhiteToMove;
            halfmoveClock = seed.HalfmoveClock;
            fullmoveNumber = seed.FullmoveNumber;
            enPassantTargetSquare = seed.EnPassantTargetSquare;
        }

        if (!FenPosition.TryParse($"{placementFen} w - - 0 1", out FenPosition? placement, out _)
            || placement is null)
        {
            return metadataSeedFen;
        }

        bool whiteKingMoved = placement.Board[4, 7] != "K";
        bool blackKingMoved = placement.Board[4, 0] != "k";
        bool whiteRookLeftMoved = placement.Board[0, 7] != "R";
        bool whiteRookRightMoved = placement.Board[7, 7] != "R";
        bool blackRookLeftMoved = placement.Board[0, 0] != "r";
        bool blackRookRightMoved = placement.Board[7, 0] != "r";

        return FenPosition.FromBoardState(
            placement.Board,
            whiteToMove,
            whiteKingMoved,
            blackKingMoved,
            whiteRookLeftMoved,
            whiteRookRightMoved,
            blackRookLeftMoved,
            blackRookRightMoved,
            enPassantTargetSquare,
            halfmoveClock,
            fullmoveNumber).GetFen();
    }

    private static string ReplacePlacementPreservingState(string baseFen, string placementFen)
    {
        if (!FenPosition.TryParse(baseFen, out FenPosition? position, out _)
            || position is null
            || !FenPosition.TryParse($"{placementFen} {(position.WhiteToMove ? "w" : "b")} - - {position.HalfmoveClock} {position.FullmoveNumber}", out FenPosition? placementOnly, out _)
            || placementOnly is null)
        {
            return baseFen;
        }

        FenPosition updated = FenPosition.FromBoardState(
            placementOnly.Board,
            position.WhiteToMove,
            position.WhiteKingMoved,
            position.BlackKingMoved,
            position.WhiteRookLeftMoved,
            position.WhiteRookRightMoved,
            position.BlackRookLeftMoved,
            position.BlackRookRightMoved,
            null,
            position.HalfmoveClock,
            position.FullmoveNumber);
        return updated.GetFen();
    }
}
