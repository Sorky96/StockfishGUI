namespace MoveMentorChessServices;

public static class AdviceRuntimeSmokeTester
{
    public static AdviceRuntimeSmokeTestResult Run()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        ILocalAdviceModel? model = CreateSmokeTestModel();

        if (!status.IsReady || model is null || !model.IsAvailable)
        {
            return new AdviceRuntimeSmokeTestResult(
                false,
                status.InstallHint is null ? status.StatusText : $"{status.StatusText}{Environment.NewLine}{Environment.NewLine}{status.InstallHint}");
        }

        LocalModelAdviceRequest request = CreateSmokeRequest();

        try
        {
            string? rawResponse = model.Generate(request);
            if (!LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? response)
                || response is null)
            {
                string diagnosticPath = AdviceRuntimeDiagnosticsLogger.Write(new AdviceRuntimeInvocationLog(
                    DateTime.UtcNow,
                    model.Name,
                    "smoke-test-parse",
                    string.Empty,
                    string.Empty,
                    "smoke-test-parse",
                    request.Prompt?.Length ?? 0,
                    request.Prompt ?? string.Empty,
                    0,
                    0,
                    0,
                    0,
                    ExitCode: null,
                    TimedOut: false,
                    Success: false,
                    rawResponse ?? string.Empty,
                    string.Empty,
                    "Runtime responded, but output could not be parsed as structured advice."));
                return new AdviceRuntimeSmokeTestResult(
                    false,
                    $"The configured advice runtime ({model.Name}) responded, but the output was not valid structured advice.{Environment.NewLine}{Environment.NewLine}Diagnostic log:{Environment.NewLine}{diagnosticPath}",
                    rawResponse,
                    diagnosticPath);
            }

            return new AdviceRuntimeSmokeTestResult(
                true,
                $"Advice runtime '{model.Name}' is ready. Sample short advice:{Environment.NewLine}{response.ShortText}",
                rawResponse);
        }
        catch (AdviceRuntimeInvocationException ex)
        {
            return new AdviceRuntimeSmokeTestResult(
                false,
                $"Advice runtime test failed: {ex.Message}{Environment.NewLine}{Environment.NewLine}Diagnostic log:{Environment.NewLine}{ex.DiagnosticPath}",
                DiagnosticPath: ex.DiagnosticPath);
        }
        catch (Exception ex)
        {
            return new AdviceRuntimeSmokeTestResult(
                false,
                $"Advice runtime test failed: {ex.Message}");
        }
    }

    private static ILocalAdviceModel? CreateSmokeTestModel()
    {
        LlamaCppAdviceRuntime? runtime = LlamaCppAdviceRuntimeResolver.Resolve();
        if (runtime is not null)
        {
            return new LlamaCppAdviceModel(runtime with
            {
                MaxTokens = Math.Min(runtime.MaxTokens, 48),
                ContextSize = Math.Min(runtime.ContextSize, 1024),
                TimeoutMs = Math.Min(runtime.TimeoutMs, 60000)
            });
        }

        return AdviceRuntimeCatalog.TryCreateConfiguredModel();
    }

    private static LocalModelAdviceRequest CreateSmokeRequest()
    {
        ReplayPly replay = new(
            1,
            1,
            PlayerSide.White,
            "e4",
            "e4",
            "e2e4",
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
            string.Empty,
            string.Empty,
            GamePhase.Opening,
            "P",
            null,
            "e2",
            "e4",
            false,
            false,
            false);

        return new LocalModelAdviceRequest(
            replay,
            MoveQualityBucket.Good,
            null,
            null,
            null,
            ExplanationLevel.Beginner,
            new AdviceGenerationContext("smoke-test", "runtime-smoke", PlayerSide.White),
            """
Return EXACTLY this JSON object and nothing else:
{"short_text":"ok","detailed_text":"ok","training_hint":"ok"}
""");
    }
}
