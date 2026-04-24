namespace StockifhsGUI;

public sealed class OpeningGameParser
{
    public const int DefaultMaxFullMoves = 10;

    public IReadOnlyList<OpeningImportPly> Parse(ImportedGame game, int maxFullMoves = DefaultMaxFullMoves)
    {
        ArgumentNullException.ThrowIfNull(game);

        int maxPlies = checked(Math.Max(0, maxFullMoves) * 2);
        ChessGame chessGame = new();
        List<OpeningImportPly> plies = new(Math.Min(game.SanMoves.Count, maxPlies));

        for (int i = 0; i < game.SanMoves.Count && i < maxPlies; i++)
        {
            AppliedMoveInfo move = chessGame.ApplySanWithResult(game.SanMoves[i]);
            int ply = i + 1;

            plies.Add(new OpeningImportPly(
                ply,
                move.MoveNumber,
                move.WhiteMoved ? "White" : "Black",
                move.FenBefore,
                move.FenAfter,
                OpeningPositionKeyBuilder.Build(move.FenBefore),
                OpeningPositionKeyBuilder.Build(move.FenAfter),
                move.San,
                move.Uci));
        }

        return plies;
    }

    public IReadOnlyList<OpeningImportPly> Parse(
        IReadOnlyList<string> sanMoves,
        int maxFullMoves = DefaultMaxFullMoves)
    {
        ArgumentNullException.ThrowIfNull(sanMoves);

        int maxPlies = checked(Math.Max(0, maxFullMoves) * 2);
        ChessGame chessGame = new();
        List<OpeningImportPly> plies = new(Math.Min(sanMoves.Count, maxPlies));

        for (int i = 0; i < sanMoves.Count && i < maxPlies; i++)
        {
            AppliedMoveInfo move = chessGame.ApplySanWithResult(sanMoves[i]);
            int ply = i + 1;

            plies.Add(new OpeningImportPly(
                ply,
                move.MoveNumber,
                move.WhiteMoved ? "White" : "Black",
                move.FenBefore,
                move.FenAfter,
                OpeningPositionKeyBuilder.Build(move.FenBefore),
                OpeningPositionKeyBuilder.Build(move.FenAfter),
                move.San,
                move.Uci));
        }

        return plies;
    }
}
