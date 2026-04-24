using System.Text.RegularExpressions;
using StockifhsGUI;
using Xunit;

namespace StockifhsGUI.Tests;

public sealed class ChessGameTests
{
    private const string FullGamePgn = """
[Event "Live Chess"]
[Site "Chess.com"]
[Date "2026.04.14"]
[Round "-"]
[White "Anandh4497"]
[Black "Sorky1996"]
[Result "1-0"]
[CurrentPosition "8/1R6/8/8/8/8/kQ5K/8 b - - 1 62"]
[Timezone "UTC"]
[ECO "A00"]
[ECOUrl "https://www.chess.com/openings/Van-t-Kruijs-Opening"]
[UTCDate "2026.04.14"]
[UTCTime "14:23:07"]
[WhiteElo "703"]
[BlackElo "666"]
[TimeControl "600"]
[Termination "Anandh4497 won by checkmate"]
[StartTime "14:23:07"]
[EndDate "2026.04.14"]
[EndTime "14:40:39"]
[Link "https://www.chess.com/analysis/game/live/167302407244/analysis?flip=true"]
[WhiteUrl "https://www.chess.com/bundles/web/images/noavatar_l.84a92436.gif"]
[WhiteCountry "69"]
[WhiteTitle ""]
[BlackUrl "https://www.chess.com/bundles/web/images/noavatar_l.84a92436.gif"]
[BlackCountry "112"]
[BlackTitle ""]

1. e3 g5 $6 2. Qf3 $6 g4 $6 3. Qxg4 d5 4. Qf3 Nf6 5. h3 Nc6 6. c3 $6 Bh6 $6 7. Bb5
Bd7 8. Bxc6 Bxc6 9. Ne2 $2 d4 $1 10. Qg3 dxe3 11. dxe3 Ne4 $2 12. Qg4 Ba4 $2 13. b3 $6
Bc6 $1 14. O-O $6 Qd6 $6 15. Nd4 Nf6 16. Ba3 Qe5 $2 17. Qg3 $9 Qh5 $4 18. Nxc6 $1 Rg8 19.
Qf3 $4 Qb5 $9 20. Nd4 Qd3 21. Nf5 Bf8 22. Qxb7 Rd8 23. Qc6+ Nd7 24. Ng3 Rxg3 25.
fxg3 Qxe3+ 26. Rf2 Qe1+ 27. Rf1 $1 Qxg3 28. Bc5 Bh6 29. Bf2 Qe5 $6 30. Qxh6 Nf6 31.
Na3 Ne4 32. Rae1 Qf5 33. Nc2 Nd2 $6 34. Re2 $6 Nxf1 35. Kxf1 Rd1+ 36. Re1 Qd3+ 37.
Kg1 Rd2 $6 38. Qe3 Qd6 39. Bg3 Rxg2+ 40. Kxg2 Qd5+ 41. Qf3 Qd2+ 42. Re2 Qd7 43.
Qg4 Qd5+ 44. Kh2 e6 $6 45. Qg8+ Ke7 46. Bh4+ f6 47. Qg7+ Kd8 48. Qxf6+ Kc8 49.
Qd8+ Qxd8 50. Bxd8 Kxd8 51. Rxe6 Kd7 52. Rh6 c6 53. Rxh7+ Kd6 54. Rxa7 c5 55. h4
Kd5 $6 56. h5 c4 57. bxc4+ Kxc4 58. h6 Kxc3 59. h7 Kxc2 60. h8=Q Kb1 61. Rb7+
Kxa2 $6 62. Qb2# 1-0
""";

    [Fact]
    public void ParsePgnMoves_KeepsCheckSuffixes()
    {
        List<string> moves = SanNotation.ParsePgnMoves(FullGamePgn);

        Assert.Contains("Qc6+", moves);
        Assert.Contains("Qb2#", moves);
        Assert.DoesNotContain("Qc6", moves);
        Assert.DoesNotContain("Qb2", moves);
    }

    [Fact]
    public void ApplyPgn_ReplaysEntireGameToExpectedFinalPosition()
    {
        Match match = Regex.Match(FullGamePgn, @"\[CurrentPosition ""([^""]+)""\]");
        Assert.True(match.Success, "CurrentPosition header not found.");

        ChessGame game = new();
        game.ApplyPgn(FullGamePgn);

        Assert.Equal(match.Groups[1].Value, game.GetFen());
    }

    [Fact]
    public void TryLoadFen_RoundTripsLoadedPosition()
    {
        const string fen = "r3k2r/pppq1ppp/2npbn2/4p3/2BPP3/2N2N2/PPP2PPP/R2Q1RK1 w kq - 4 9";

        ChessGame game = new();

        bool loaded = game.TryLoadFen(fen, out string? error);

        Assert.True(loaded, error);
        Assert.Equal(fen, game.GetFen());
    }

    [Fact]
    public void TryLoadFen_RejectsInvalidPlacement()
    {
        ChessGame game = new();

        bool loaded = game.TryLoadFen("8/8/8/8/8/8/8/8 w - - 0 1", out string? error);

        Assert.False(loaded);
        Assert.Contains("exactly one white king", error);
    }

    [Fact]
    public void TryLoadFen_RoundTripsEnPassantTarget()
    {
        const string fen = "4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1";

        ChessGame game = new();

        bool loaded = game.TryLoadFen(fen, out string? error);

        Assert.True(loaded, error);
        Assert.Equal(fen, game.GetFen());
    }

    [Fact]
    public void ApplySan_ExecutesEnPassantCapture()
    {
        const string fen = "4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1";

        ChessGame game = new();
        Assert.True(game.TryLoadFen(fen, out string? error), error);

        game.ApplySan("exd6");

        Assert.Equal("4k3/8/3P4/8/8/8/8/4K3 b - - 0 1", game.GetFen());
    }

    [Fact]
    public void GetLegalMoves_ReturnsStructuredMovesForCurrentPosition()
    {
        ChessGame game = new();

        IReadOnlyList<LegalMoveInfo> legalMoves = game.GetLegalMoves();

        Assert.Contains(legalMoves, move => move.Uci == "e2e4" && move.San == "e4");
        Assert.Contains(legalMoves, move => move.Uci == "g1f3" && move.San == "Nf3");
    }

    [Fact]
    public void TryApplyUci_AppliesLegalMoveAndReturnsSnapshots()
    {
        ChessGame game = new();

        bool applied = game.TryApplyUci("e2e4", out AppliedMoveInfo? move, out string? error);

        Assert.True(applied, error);
        Assert.NotNull(move);
        Assert.Equal("e4", move!.San);
        Assert.Equal("e2e4", move.Uci);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", move.FenBefore);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", move.FenAfter);
    }

    [Fact]
    public void TryApplyUci_RejectsIllegalMove()
    {
        ChessGame game = new();

        bool applied = game.TryApplyUci("e2e5", out AppliedMoveInfo? move, out string? error);

        Assert.False(applied);
        Assert.Null(move);
        Assert.Contains("No legal move matches", error);
    }

    [Fact]
    public void GetLegalMoves_AllowsKc7InStauntonMemPosition()
    {
        ChessGame game = new();
        string[] moves =
        {
            "d4", "Nf6", "c4", "g6", "Nc3", "Bg7", "e4", "d6", "f3", "e5",
            "dxe5", "dxe5", "Qxd8+", "Kxd8", "Be3", "c6", "O-O-O+"
        };

        foreach (string move in moves)
        {
            game.ApplySan(move);
        }

        IReadOnlyList<LegalMoveInfo> legalMoves = game.GetLegalMoves();

        Assert.Contains(legalMoves, move => move.San == "Kc7");
    }

    [Fact]
    public void GetLegalMoves_AllowsKe7InHunChampionshipPosition()
    {
        ChessGame game = new();
        string[] moves =
        {
            "e4", "e5", "Bc4", "Nc6", "Nc3", "Bc5", "d3", "d6", "Na4", "Na5",
            "Nxc5", "Nxc4", "dxc4", "dxc5", "Qxd8+", "Kxd8", "Be3", "b6", "O-O-O+"
        };

        foreach (string move in moves)
        {
            game.ApplySan(move);
        }

        IReadOnlyList<LegalMoveInfo> legalMoves = game.GetLegalMoves();

        Assert.Contains(legalMoves, move => move.San == "Ke7");
    }

    [Fact]
    public void GetLegalMoves_AllowsNf6CheckInNewOrleansPosition()
    {
        ChessGame game = new();
        string[] moves =
        {
            "e4", "e5", "f4", "exf4", "Nf3", "d5", "Nc3", "dxe4", "Nxe4", "Bg4", "Qe2", "Bxf3"
        };

        foreach (string move in moves)
        {
            game.ApplySan(move);
        }

        IReadOnlyList<LegalMoveInfo> legalMoves = game.GetLegalMoves();

        LegalMoveInfo knightMove = Assert.Single(legalMoves, move => move.Uci == "e4f6");
        Assert.Equal("Nf6#", knightMove.San);
    }

    [Fact]
    public void ApplySan_AcceptsCheckSuffixWhenEngineEvaluatesMate()
    {
        ChessGame game = new();
        string[] moves =
        {
            "e4", "e5", "f4", "exf4", "Nf3", "d5", "Nc3", "dxe4", "Nxe4", "Bg4", "Qe2", "Bxf3", "Nf6+"
        };

        foreach (string san in moves)
        {
            game.ApplySan(san);
        }

        Assert.Equal("rn1qkbnr/ppp2ppp/5N2/8/5p2/5b2/PPPPQ1PP/R1B1KB1R b KQkq - 1 7", game.GetFen());
    }

}
