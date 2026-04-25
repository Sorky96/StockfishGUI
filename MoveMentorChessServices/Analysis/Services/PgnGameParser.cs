using System.Text;
using System.Text.RegularExpressions;

namespace MoveMentorChessServices;

public static class PgnGameParser
{
    private static readonly Regex HeaderRegex = new(@"^\[(?<key>[A-Za-z0-9_]+)\s+""(?<value>.*)""\]\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    public static ImportedGame Parse(string pgn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pgn);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HeaderRegex.Matches(pgn))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;
            headers[key] = value;
        }

        List<string> sanMoves = ChessGame.ParsePgnMoves(pgn);

        return new ImportedGame(
            pgn,
            sanMoves,
            GetValue(headers, "White"),
            GetValue(headers, "Black"),
            ParseNullableInt(headers, "WhiteElo"),
            ParseNullableInt(headers, "BlackElo"),
            GetValue(headers, "Date"),
            GetValue(headers, "Result"),
            GetValue(headers, "ECO"),
            GetValue(headers, "Site"));
    }

    public static PgnBatchParseResult ParseMany(string pgnText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pgnText);

        List<ImportedGame> games = new();
        List<PgnBatchParseError> errors = new();
        int ordinal = 0;

        using StringReader reader = new(pgnText);
        foreach (string gameText in SplitGames(reader))
        {
            if (string.IsNullOrWhiteSpace(gameText))
            {
                continue;
            }

            ordinal++;
            try
            {
                games.Add(Parse(gameText));
            }
            catch (Exception ex)
            {
                errors.Add(new PgnBatchParseError(ordinal, ex.Message));
            }
        }

        return new PgnBatchParseResult(games, errors);
    }

    private static IEnumerable<string> SplitGames(TextReader reader)
    {
        StringBuilder currentGame = new();
        bool sawMoveText = false;

        while (reader.ReadLine() is { } line)
        {
            string trimmedStart = line.TrimStart();
            bool isHeaderLine = IsHeaderLine(trimmedStart);
            bool startsNewGame = isHeaderLine
                && currentGame.Length > 0
                && (trimmedStart.StartsWith("[Event ", StringComparison.Ordinal) || sawMoveText);

            if (startsNewGame)
            {
                yield return currentGame.ToString().Trim();
                currentGame.Clear();
                sawMoveText = false;
            }

            currentGame.AppendLine(line);

            if (!string.IsNullOrWhiteSpace(trimmedStart) && !isHeaderLine)
            {
                sawMoveText = true;
            }
        }

        if (currentGame.Length > 0)
        {
            yield return currentGame.ToString().Trim();
        }
    }

    private static bool IsHeaderLine(string trimmedLine)
    {
        return trimmedLine.StartsWith("[", StringComparison.Ordinal)
            && trimmedLine.EndsWith("]", StringComparison.Ordinal);
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out string? value) ? value : null;
    }

    private static int? ParseNullableInt(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
            ? parsed
            : null;
    }
}

public sealed record PgnBatchParseResult(
    IReadOnlyList<ImportedGame> Games,
    IReadOnlyList<PgnBatchParseError> Errors);

public sealed record PgnBatchParseError(int GameOrdinal, string Message);
