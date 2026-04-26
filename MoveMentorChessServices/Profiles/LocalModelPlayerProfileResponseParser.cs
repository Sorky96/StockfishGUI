using System.Text.Json;

namespace MoveMentorChessServices;

public static class LocalModelPlayerProfileResponseParser
{
    public static bool TryParse(string? rawResponse, out PlayerProfileFormattedOutput? output)
    {
        output = null;
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        string normalized = Normalize(rawResponse);
        if (!normalized.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(normalized);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string profileSummary = ReadString(root, "profile_summary");
            string strengthsAndWeaknesses = ReadString(root, "strengths_and_weaknesses");
            string whatToFocusNext = ReadString(root, "what_to_focus_next");
            string toneAdaptedVersion = ReadString(root, "tone_adapted_version");
            string deepDive = ReadString(root, "deep_dive");

            if (string.IsNullOrWhiteSpace(profileSummary)
                || string.IsNullOrWhiteSpace(strengthsAndWeaknesses)
                || string.IsNullOrWhiteSpace(whatToFocusNext)
                || string.IsNullOrWhiteSpace(toneAdaptedVersion))
            {
                return false;
            }

            output = new PlayerProfileFormattedOutput(
                profileSummary.Trim(),
                strengthsAndWeaknesses.Trim(),
                whatToFocusNext.Trim(),
                toneAdaptedVersion.Trim(),
                string.IsNullOrWhiteSpace(deepDive) ? null : deepDive.Trim());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Normalize(string rawResponse)
    {
        string normalized = rawResponse.Trim();

        if (TryExtractMarkdownFence(normalized, out string fenced))
        {
            normalized = fenced;
        }
        else if (TryExtractJsonObject(normalized, out string jsonObject))
        {
            normalized = jsonObject;
        }

        return normalized.Trim();
    }

    private static bool TryExtractMarkdownFence(string rawResponse, out string content)
    {
        content = string.Empty;
        if (!rawResponse.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        int firstNewLine = rawResponse.IndexOf('\n');
        int closingFence = rawResponse.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || closingFence <= firstNewLine)
        {
            return false;
        }

        content = rawResponse[(firstNewLine + 1)..closingFence].Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static bool TryExtractJsonObject(string rawResponse, out string content)
    {
        content = string.Empty;
        int start = rawResponse.IndexOf('{');
        int end = rawResponse.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        content = rawResponse[start..(end + 1)];
        return true;
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
}
