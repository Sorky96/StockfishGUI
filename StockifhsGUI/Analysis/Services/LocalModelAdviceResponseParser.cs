using System.Text.Json;

namespace StockifhsGUI;

public static class LocalModelAdviceResponseParser
{
    private static readonly string[] SupportedKeys = ["short_text", "detailed_text", "training_hint"];

    public static bool TryParse(string? rawResponse, out LocalModelAdviceResponse? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        string normalized = Normalize(rawResponse);
        return TryParseJson(normalized, out response)
            || TryParseKeyValue(normalized, out response);
    }

    private static string Normalize(string rawResponse)
    {
        string normalized = rawResponse.Trim();

        if (TryExtractMarkdownFence(normalized, out string fencedContent))
        {
            normalized = fencedContent;
        }
        else if (TryExtractJsonObject(normalized, out string jsonObject))
        {
            normalized = jsonObject;
        }

        return normalized.Trim();
    }

    private static bool TryParseJson(string rawResponse, out LocalModelAdviceResponse? response)
    {
        response = null;

        if (!rawResponse.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawResponse);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string shortText = ReadString(root, "short_text");
            string detailedText = ReadString(root, "detailed_text");
            string trainingHint = ReadString(root, "training_hint");

            if (!IsComplete(shortText, detailedText, trainingHint))
            {
                return false;
            }

            response = new LocalModelAdviceResponse(shortText, trainingHint, detailedText);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseKeyValue(string rawResponse, out LocalModelAdviceResponse? response)
    {
        response = null;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        List<string> currentLines = [];

        foreach (string rawLine in rawResponse.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (TrySplitField(line, out string? key, out string value))
            {
                FlushCurrent(values, currentKey, currentLines);
                currentKey = key;
                currentLines.Clear();
                currentLines.Add(value);
                continue;
            }

            if (currentKey is not null)
            {
                currentLines.Add(line);
            }
        }

        FlushCurrent(values, currentKey, currentLines);

        if (!values.TryGetValue("short_text", out string? shortText)
            || !values.TryGetValue("detailed_text", out string? detailedText)
            || !values.TryGetValue("training_hint", out string? trainingHint)
            || !IsComplete(shortText, detailedText, trainingHint))
        {
            return false;
        }

        response = new LocalModelAdviceResponse(shortText, trainingHint, detailedText);
        return true;
    }

    private static bool TryExtractMarkdownFence(string rawResponse, out string content)
    {
        content = string.Empty;
        if (!rawResponse.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        int firstNewLine = rawResponse.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return false;
        }

        int closingFence = rawResponse.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence <= firstNewLine)
        {
            return false;
        }

        content = rawResponse[(firstNewLine + 1)..closingFence].Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static bool TryExtractJsonObject(string rawResponse, out string content)
    {
        content = string.Empty;

        // When stdout contains multiple JSON objects (e.g. echoed prompt + actual response),
        // prefer the last complete JSON object — the model's actual output.
        int lastClose = rawResponse.LastIndexOf('}');
        while (lastClose >= 0)
        {
            int depth = 0;
            int matchStart = -1;
            for (int i = lastClose; i >= 0; i--)
            {
                if (rawResponse[i] == '}')
                {
                    depth++;
                }
                else if (rawResponse[i] == '{')
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchStart = i;
                        break;
                    }
                }
            }

            if (matchStart >= 0)
            {
                content = rawResponse[matchStart..(lastClose + 1)];
                return true;
            }

            lastClose = lastClose > 0
                ? rawResponse.LastIndexOf('}', lastClose - 1)
                : -1;
        }

        return false;
    }

    private static bool TrySplitField(string line, out string? key, out string value)
    {
        key = null;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        int separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        string candidateKey = line[..separatorIndex].Trim();
        if (!SupportedKeys.Contains(candidateKey, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        key = candidateKey;
        value = line[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static void FlushCurrent(IDictionary<string, string> values, string? currentKey, List<string> currentLines)
    {
        if (currentKey is null)
        {
            return;
        }

        string text = string.Join(Environment.NewLine, currentLines)
            .Trim();

        values[currentKey] = text;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static bool IsComplete(string shortText, string detailedText, string trainingHint)
    {
        return !string.IsNullOrWhiteSpace(shortText)
            && !string.IsNullOrWhiteSpace(detailedText)
            && !string.IsNullOrWhiteSpace(trainingHint);
    }
}
