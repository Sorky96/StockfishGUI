namespace StockifhsGUI;

internal static class PositionInspector
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
        if (!TryParseSquare(square, out BoardPoint target)
            || !FenPosition.TryParse(fen, out FenPosition? position, out _)
            || position is null)
        {
            return false;
        }

        string? piece = position.Board[target.X, target.Y];
        if (string.IsNullOrEmpty(piece) || IsWhite(piece) != (side == PlayerSide.White))
        {
            return false;
        }

        int attackers = CountAttackers(position.Board, target, Opponent(side));
        int defenders = CountAttackers(position.Board, target, side);
        return attackers > defenders;
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

    private static int CountAttackers(string?[,] board, BoardPoint target, PlayerSide bySide)
    {
        int count = 0;
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

    private static bool IsWhite(string piece) => char.IsUpper(piece[0]);
    private static PlayerSide Opponent(PlayerSide side) => side == PlayerSide.White ? PlayerSide.Black : PlayerSide.White;

    private readonly record struct BoardPoint(int X, int Y);
}
