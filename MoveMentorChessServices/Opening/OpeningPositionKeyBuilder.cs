namespace MoveMentorChessServices;

public static class OpeningPositionKeyBuilder
{
    public static string Build(string fen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        string[] parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            throw new ArgumentException("FEN must contain at least placement, side, castling and en passant fields.", nameof(fen));
        }

        return string.Join(' ', parts.Take(4));
    }
}
