using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MoveMentorChessServices;

/// <summary>
/// Advice model that communicates with a running llama-server via HTTP POST /completion.
/// The server process is managed by <see cref="LlamaCppServerManager"/>.
/// </summary>
public sealed class LlamaCppHttpAdviceModel : ILocalAdviceModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LlamaCppServerConfig config;
    private readonly LlamaCppServerManager serverManager;
    private readonly HttpClient httpClient;

    public LlamaCppHttpAdviceModel(LlamaCppServerConfig config, LlamaCppServerManager? serverManager = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
        this.serverManager = serverManager ?? LlamaCppServerManager.Instance;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Math.Max(5000, config.TimeoutMs))
        };
    }

    public string Name => "llama.cpp (server)";

    public bool IsAvailable => File.Exists(config.ServerPath) && File.Exists(config.ModelPath);

    public string? Generate(LocalModelAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            return null;
        }

        if (!serverManager.EnsureRunning(config))
        {
            return null;
        }

        string? baseUrl = serverManager.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        string completionUrl = $"{baseUrl}/completion";
        string grammar = LlamaCppAdviceModel.BuildJsonGrammar(request.JsonOutputKeys);

        var requestBody = new
        {
            prompt = request.Prompt ?? string.Empty,
            n_predict = Math.Max(32, config.MaxTokens),
            temperature = 0.7,
            grammar
        };

        try
        {
            string requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);
            using StringContent content = new(requestJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = httpClient.PostAsync(completionUrl, content).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ExtractContent(responseJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the "content" field from the llama-server /completion response JSON.
    /// </summary>
    public static string? ExtractContent(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseJson);
            if (document.RootElement.TryGetProperty("content", out JsonElement contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                string? content = contentElement.GetString();
                return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
