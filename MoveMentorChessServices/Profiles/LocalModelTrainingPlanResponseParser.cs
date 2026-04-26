using System.Text.Json;

namespace MoveMentorChessServices;

public static class LocalModelTrainingPlanResponseParser
{
    public static bool TryParse(string? rawResponse, out TrainingPlanFormattedOutput? output)
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

            string shortWeeklyPlan = ReadString(root, "short_weekly_plan");
            string detailedWeeklyPlan = ReadString(root, "detailed_weekly_plan");
            string priorityRationale = ReadString(root, "priority_rationale");
            string toneAdaptedVersion = ReadString(root, "tone_adapted_version");

            if (string.IsNullOrWhiteSpace(shortWeeklyPlan)
                || string.IsNullOrWhiteSpace(detailedWeeklyPlan)
                || string.IsNullOrWhiteSpace(priorityRationale)
                || string.IsNullOrWhiteSpace(toneAdaptedVersion))
            {
                return false;
            }

            output = new TrainingPlanFormattedOutput(
                shortWeeklyPlan.Trim(),
                detailedWeeklyPlan.Trim(),
                priorityRationale.Trim(),
                toneAdaptedVersion.Trim());
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
