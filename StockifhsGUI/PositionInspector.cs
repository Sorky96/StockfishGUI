namespace StockifhsGUI;

public static class PositionInspector
{
    private static readonly Dictionary<char, int> PieceValues = new()
    {
        ['p'] = 100,
        ['n'] = 320,
        ['b'] = 330,
        ['r'] = 500,
        ['q'] = 900,
        ['k'] = 0
    };

    public static int MaterialScore(string fen, PlayerSide perspective)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return 0;
        }

        int white = 0;
        int black = 0;
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = position.Board[x, y];
                if (string.IsNullOrEmpty(piece))
                {
                    continue;
                }

                int value = PieceValues[char.ToLowerInvariant(piece[0])];
                if (char.IsUpper(piece[0]))
                {
                    white += value;
                }
                else
                {
                    black += value;
                }
            }
        }

        return perspective == PlayerSide.White ? white - black : black - white;
    }

    public static bool IsMovedPieceHanging(string fen, string square, PlayerSide side)
    {
        SquareSafetySummary? safety = AnalyzeSquareSafety(fen, square, side);
        return safety?.IsHanging == true;
    }

    public static bool IsKingOnCastledWing(string fen, PlayerSide side)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return false;
        }

        string king = side == PlayerSide.White ? "K" : "k";
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                if (position.Board[x, y] == king)
                {
                    return side == PlayerSide.White
                        ? y == 7 && (x == 6 || x == 2)
                        : y == 0 && (x == 6 || x == 2);
                }
            }
        }

        return false;
    }

    public static int CountDevelopedMinorPieces(string fen, PlayerSide side)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return 0;
        }

        return CountDevelopedMinorPieces(position.Board, side);
    }

    public static bool IsKingCentralized(string fen, PlayerSide side)
    {
        if (!FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return false;
        }

        string king = side == PlayerSide.White ? "K" : "k";
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                if (position.Board[x, y] == king)
                {
                    return x is >= 2 and <= 5
                        && y is >= 2 and <= 5;
                }
            }
        }

        return false;
    }

    public static MaterialSwingSummary? AnalyzeMaterialSwingAlongLine(
        string fen,
        PlayerSide perspective,
        IReadOnlyList<string>? pv,
        int maxPlies = 6)
    {
        if (string.IsNullOrWhiteSpace(fen) || pv is null || pv.Count == 0 || maxPlies <= 0)
        {
            return null;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fen, out _))
        {
            return null;
        }

        int baseline = MaterialScore(fen, perspective);
        int finalDelta = 0;
        int worstDelta = 0;
        int bestDelta = 0;
        int appliedPlies = 0;

        foreach (string uciMove in pv.Take(maxPlies))
        {
            if (!game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _) || appliedMove is null)
            {
                return appliedPlies == 0
                    ? null
                    : new MaterialSwingSummary(finalDelta, worstDelta, bestDelta, appliedPlies);
            }

            appliedPlies++;
            int currentDelta = MaterialScore(appliedMove.FenAfter, perspective) - baseline;
            finalDelta = currentDelta;
            worstDelta = Math.Min(worstDelta, currentDelta);
            bestDelta = Math.Max(bestDelta, currentDelta);
        }

        return new MaterialSwingSummary(finalDelta, worstDelta, bestDelta, appliedPlies);
    }

    public static SquareSafetySummary? AnalyzeSquareSafety(string fen, string square, PlayerSide side)
    {
        if (!TryParseSquare(square, out BoardPoint target)
            || !FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return null;
        }

        string? piece = position.Board[target.X, target.Y];
        if (string.IsNullOrEmpty(piece) || IsWhite(piece) != (side == PlayerSide.White))
        {
            return null;
        }

        int pieceValueCp = PieceValues[char.ToLowerInvariant(piece[0])];
        List<int> attackerValues = CollectAttackerValues(position.Board, target, Opponent(side));
        List<int> defenderValues = CollectAttackerValues(position.Board, target, side);
        int attackers = attackerValues.Count;
        int defenders = defenderValues.Count;
        int? cheapestAttackerValueCp = attackerValues.Count == 0 ? null : attackerValues.Min();
        bool isHanging = attackers > defenders;
        bool isFreeToTake = attackers > 0 && defenders == 0;
        bool likelyLosesExchange = attackers > 0
            && (defenders == 0
                || (cheapestAttackerValueCp is int cheapest
                    && cheapest < pieceValueCp
                    && attackers >= defenders));

        return new SquareSafetySummary(
            pieceValueCp,
            attackers,
            defenders,
            cheapestAttackerValueCp,
            isHanging,
            isFreeToTake,
            likelyLosesExchange);
    }

    public static int? CountPieceMobility(string fen, string square, PlayerSide side)
    {
        if (!TryParseSquare(square, out BoardPoint origin)
            || !FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return null;
        }

        string? piece = position.Board[origin.X, origin.Y];
        if (string.IsNullOrEmpty(piece) || IsWhite(piece) != (side == PlayerSide.White))
        {
            return null;
        }

        return char.ToLowerInvariant(piece[0]) switch
        {
            'p' => CountPawnMobility(position.Board, origin, side),
            'n' => CountKnightMobility(position.Board, origin, side),
            'b' => CountSlidingMobility(position.Board, origin, side, [(1, 1), (1, -1), (-1, 1), (-1, -1)]),
            'r' => CountSlidingMobility(position.Board, origin, side, [(1, 0), (-1, 0), (0, 1), (0, -1)]),
            'q' => CountSlidingMobility(position.Board, origin, side, [(1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1)]),
            'k' => CountKingMobility(position.Board, origin, side),
            _ => null
        };
    }

    public static bool IsEdgeSquare(string square)
    {
        return TryParseSquare(square, out BoardPoint point)
            && (point.X is 0 or 7 || point.Y is 0 or 7);
    }

    private static List<int> CollectAttackerValues(string?[,] board, BoardPoint target, PlayerSide bySide)
    {
        List<int> values = new();
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece) || IsWhite(piece) != (bySide == PlayerSide.White))
                {
                    continue;
                }

                if (AttacksSquare(board, new BoardPoint(x, y), target, piece))
                {
                    values.Add(PieceValues[char.ToLowerInvariant(piece[0])]);
                }
            }
        }

        return values;
    }

    private static int CountDevelopedMinorPieces(string?[,] board, PlayerSide side)
    {
        int count = 0;
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = board[x, y];
                if (string.IsNullOrEmpty(piece)
                    || IsWhite(piece) != (side == PlayerSide.White)
                    || char.ToLowerInvariant(piece[0]) is not ('n' or 'b'))
                {
                    continue;
                }

                if (!IsMinorPieceOnStartingSquare(piece, x, y, side))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsMinorPieceOnStartingSquare(string piece, int x, int y, PlayerSide side)
    {
        char normalizedPiece = char.ToLowerInvariant(piece[0]);
        return (side, normalizedPiece, x, y) switch
        {
            (PlayerSide.White, 'n', 1, 7) => true,
            (PlayerSide.White, 'n', 6, 7) => true,
            (PlayerSide.White, 'b', 2, 7) => true,
            (PlayerSide.White, 'b', 5, 7) => true,
            (PlayerSide.Black, 'n', 1, 0) => true,
            (PlayerSide.Black, 'n', 6, 0) => true,
            (PlayerSide.Black, 'b', 2, 0) => true,
            (PlayerSide.Black, 'b', 5, 0) => true,
            _ => false
        };
    }

    private static int CountPawnMobility(string?[,] board, BoardPoint origin, PlayerSide side)
    {
        int count = 0;
        int direction = side == PlayerSide.White ? -1 : 1;
        int startRank = side == PlayerSide.White ? 6 : 1;
        int oneStepY = origin.Y + direction;

        if (IsOnBoard(oneStepY, origin.X) && string.IsNullOrEmpty(board[origin.X, oneStepY]))
        {
            count++;

            int twoStepY = origin.Y + (2 * direction);
            if (origin.Y == startRank && IsOnBoard(twoStepY, origin.X) && string.IsNullOrEmpty(board[origin.X, twoStepY]))
            {
                count++;
            }
        }

        foreach (int dx in new[] { -1, 1 })
        {
            int targetX = origin.X + dx;
            int targetY = origin.Y + direction;
            if (!IsOnBoard(targetY, targetX))
            {
                continue;
            }

            string? targetPiece = board[targetX, targetY];
            if (!string.IsNullOrEmpty(targetPiece) && IsWhite(targetPiece) != (side == PlayerSide.White))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountKnightMobility(string?[,] board, BoardPoint origin, PlayerSide side)
    {
        int count = 0;
        (int Dx, int Dy)[] offsets =
        [
            (1, 2), (2, 1), (2, -1), (1, -2),
            (-1, -2), (-2, -1), (-2, 1), (-1, 2)
        ];

        foreach ((int dx, int dy) in offsets)
        {
            int targetX = origin.X + dx;
            int targetY = origin.Y + dy;
            if (!IsOnBoard(targetY, targetX))
            {
                continue;
            }

            string? targetPiece = board[targetX, targetY];
            if (string.IsNullOrEmpty(targetPiece) || IsWhite(targetPiece) != (side == PlayerSide.White))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountSlidingMobility(string?[,] board, BoardPoint origin, PlayerSide side, IReadOnlyList<(int Dx, int Dy)> directions)
    {
        int count = 0;
        foreach ((int dx, int dy) in directions)
        {
            int x = origin.X + dx;
            int y = origin.Y + dy;
            while (IsOnBoard(y, x))
            {
                string? targetPiece = board[x, y];
                if (string.IsNullOrEmpty(targetPiece))
                {
                    count++;
                    x += dx;
                    y += dy;
                    continue;
                }

                if (IsWhite(targetPiece) != (side == PlayerSide.White))
                {
                    count++;
                }

                break;
            }
        }

        return count;
    }

    private static int CountKingMobility(string?[,] board, BoardPoint origin, PlayerSide side)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int targetX = origin.X + dx;
                int targetY = origin.Y + dy;
                if (!IsOnBoard(targetY, targetX))
                {
                    continue;
                }

                string? targetPiece = board[targetX, targetY];
                if (string.IsNullOrEmpty(targetPiece) || IsWhite(targetPiece) != (side == PlayerSide.White))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool AttacksSquare(string?[,] board, BoardPoint from, BoardPoint to, string piece)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;

        return char.ToLowerInvariant(piece[0]) switch
        {
            'p' => Math.Abs(dx) == 1 && dy == (IsWhite(piece) ? -1 : 1),
            'n' => (Math.Abs(dx) == 2 && Math.Abs(dy) == 1) || (Math.Abs(dx) == 1 && Math.Abs(dy) == 2),
            'b' => Math.Abs(dx) == Math.Abs(dy) && IsPathClear(board, from, to),
            'r' => (dx == 0 || dy == 0) && IsPathClear(board, from, to),
            'q' => (dx == 0 || dy == 0 || Math.Abs(dx) == Math.Abs(dy)) && IsPathClear(board, from, to),
            'k' => Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
            _ => false
        };
    }

    private static bool IsPathClear(string?[,] board, BoardPoint from, BoardPoint to)
    {
        int dx = Math.Sign(to.X - from.X);
        int dy = Math.Sign(to.Y - from.Y);
        int x = from.X + dx;
        int y = from.Y + dy;

        while (x != to.X || y != to.Y)
        {
            if (!string.IsNullOrEmpty(board[x, y]))
            {
                return false;
            }

            x += dx;
            y += dy;
        }

        return true;
    }

    private static bool TryParseSquare(string square, out BoardPoint point)
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

        point = new BoardPoint(file - 'a', 8 - (rank - '0'));
        return true;
    }

    private static bool IsOnBoard(int y, int x) => x is >= 0 and < 8 && y is >= 0 and < 8;
    private static bool IsWhite(string piece) => char.IsUpper(piece[0]);
    private static PlayerSide Opponent(PlayerSide side) => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;

    public readonly record struct MaterialSwingSummary(int FinalDeltaCp, int WorstDeltaCp, int BestDeltaCp, int AppliedPlies);
    public readonly record struct SquareSafetySummary(
        int PieceValueCp,
        int Attackers,
        int Defenders,
        int? CheapestAttackerValueCp,
        bool IsHanging,
        bool IsFreeToTake,
        bool LikelyLosesExchange);
    private readonly record struct BoardPoint(int X, int Y);
}
