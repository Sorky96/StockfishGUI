namespace StockifhsGUI;

public sealed class GameReplayService
{
    public IReadOnlyList<ReplayPly> Replay(ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        ChessGame chessGame = new();
        List<ReplayPly> replay = new(game.SanMoves.Count);

        for (int i = 0; i < game.SanMoves.Count; i++)
        {
            string san = game.SanMoves[i];
            AppliedMoveInfo appliedMove = chessGame.ApplySanWithResult(san);
            int ply = i + 1;

            replay.Add(new ReplayPly(
                ply,
                appliedMove.MoveNumber,
                appliedMove.WhiteMoved ? PlayerSide.White : PlayerSide.Black,
                appliedMove.San,
                appliedMove.NormalizedSan,
                appliedMove.Uci,
                appliedMove.FenBefore,
                appliedMove.FenAfter,
                appliedMove.PlacementFenBefore,
                appliedMove.PlacementFenAfter,
                DeterminePhase(appliedMove.FenBefore, ply),
                appliedMove.MovingPiece,
                appliedMove.PromotionPiece,
                appliedMove.FromSquare,
                appliedMove.ToSquare,
                appliedMove.IsCapture,
                appliedMove.IsEnPassant,
                appliedMove.IsCastle));
        }

        return replay;
    }

    private static GamePhase DeterminePhase(string fenBefore, int ply)
    {
        if (!FenPosition.TryParse(fenBefore, out FenPosition? position, out _)
            || position is null)
        {
            return ply <= 16 ? GamePhase.Opening : GamePhase.Middlegame;
        }

        int nonPawnNonKingPieces = 0;
        int queens = 0;
        int rooks = 0;

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                string? piece = position.Board[x, y];
                if (string.IsNullOrEmpty(piece))
                {
                    continue;
                }

                switch (char.ToLowerInvariant(piece[0]))
                {
                    case 'q':
                        queens++;
                        nonPawnNonKingPieces++;
                        break;
                    case 'r':
                        rooks++;
                        nonPawnNonKingPieces++;
                        break;
                    case 'b':
                    case 'n':
                        nonPawnNonKingPieces++;
                        break;
                }
            }
        }

        if (ply <= 16 && nonPawnNonKingPieces >= 10)
        {
            return GamePhase.Opening;
        }

        if (queens == 0 || (queens <= 1 && rooks <= 2) || nonPawnNonKingPieces <= 6)
        {
            return GamePhase.Endgame;
        }

        return GamePhase.Middlegame;
    }
}
