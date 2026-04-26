using System.Diagnostics;
using System.Text;

namespace MoveMentorChessServices;

public sealed class LlamaCppAdviceModel : ILocalAdviceModel
{
    private readonly LlamaCppAdviceRuntime runtime;

    public LlamaCppAdviceModel(LlamaCppAdviceRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        this.runtime = runtime;
    }

    public string Name => "llama.cpp";

    public bool IsAvailable => File.Exists(runtime.CliPath) && File.Exists(runtime.ModelPath);

    public string? Generate(LocalModelAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
        {
            return null;
        }

        string workingDirectory = Path.GetDirectoryName(runtime.CliPath) ?? AppContext.BaseDirectory;
        string modelArgument = ResolveModelArgument(runtime.ModelPath, workingDirectory);
        IReadOnlyList<string> arguments = BuildArguments(modelArgument, request.Prompt, runtime.MaxTokens, runtime.ContextSize, runtime.GpuLayersArgument, request.JsonOutputKeys);
        string commandLine = BuildCommandLine(runtime.CliPath, arguments);
        string promptPreview = BuildPromptPreview(request.Prompt);
        int promptLength = request.Prompt?.Length ?? 0;
        DateTime startedUtc = DateTime.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();

        using Process process = new();
        process.StartInfo = CreateStartInfo(workingDirectory, arguments);

        if (!process.Start())
        {
            throw CreateInvocationException(
                "Could not start llama.cpp advice process.",
                startedUtc,
                stopwatch.ElapsedMilliseconds,
                workingDirectory,
                modelArgument,
                commandLine,
                promptLength,
                promptPreview,
                exitCode: null,
                timedOut: false,
                stdout: string.Empty,
                stderr: string.Empty);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(runtime.TimeoutMs))
        {
            TryKillProcess(process);
            process.WaitForExit(5000);
            string partialStdout = TryGetTaskResult(stdoutTask);
            string partialStderr = TryGetTaskResult(stderrTask);
            throw CreateInvocationException(
                $"llama.cpp advice generation exceeded timeout of {runtime.TimeoutMs} ms.",
                startedUtc,
                stopwatch.ElapsedMilliseconds,
                workingDirectory,
                modelArgument,
                commandLine,
                promptLength,
                promptPreview,
                process.HasExited ? process.ExitCode : null,
                timedOut: true,
                partialStdout,
                partialStderr);
        }

        if (!Task.WaitAll([stdoutTask, stderrTask], runtime.TimeoutMs))
        {
            TryKillProcess(process);
            process.WaitForExit(5000);
            string partialStdout = TryGetTaskResult(stdoutTask);
            string partialStderr = TryGetTaskResult(stderrTask);
            throw CreateInvocationException(
                $"llama.cpp advice generation did not flush output within {runtime.TimeoutMs} ms.",
                startedUtc,
                stopwatch.ElapsedMilliseconds,
                workingDirectory,
                modelArgument,
                commandLine,
                promptLength,
                promptPreview,
                process.HasExited ? process.ExitCode : null,
                timedOut: true,
                partialStdout,
                partialStderr);
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw CreateInvocationException(
                $"llama.cpp exited with code {process.ExitCode}: {Shorten(stderr)}",
                startedUtc,
                stopwatch.ElapsedMilliseconds,
                workingDirectory,
                modelArgument,
                commandLine,
                promptLength,
                promptPreview,
                process.ExitCode,
                timedOut: false,
                stdout,
                stderr);
        }

        return stdout.Replace("\u0000", string.Empty).Trim();
    }

    private ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new(runtime.CliPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static IReadOnlyList<string> BuildArguments(
        string modelPath,
        string prompt,
        int maxTokens,
        int contextSize,
        string? gpuLayersArgument = LlamaGpuSettingsResolver.BalancedGpuLayersArgument,
        IReadOnlyList<string>? jsonOutputKeys = null)
    {
        return
        [
            "-m",
            modelPath,
            "-c",
            Math.Max(512, contextSize).ToString(),
            "-n",
            Math.Max(32, maxTokens).ToString(),
            "--single-turn",
            "--simple-io",
            "--no-display-prompt",
            "--log-disable",
            "-ngl",
            NormalizeGpuLayersArgument(gpuLayersArgument),
            "--grammar",
            BuildJsonGrammar(jsonOutputKeys),
            "-p",
            prompt
        ];
    }

    public static string BuildJsonGrammar(IReadOnlyList<string>? keys = null)
    {
        IReadOnlyList<string> outputKeys = keys is { Count: > 0 }
            ? keys
            : ["short_text", "detailed_text", "training_hint"];
        string root = string.Join(
            " \",\" ",
            outputKeys.Select(key => $"\"\\\"{EscapeGrammarLiteral(key)}\\\"\" \":\" string"));

        return $$"""
root ::= "{" {{root}} "}"
string ::= "\"" characters "\""
characters ::= "" | character characters
character ::= [^"\\\x00-\x1F] | "\\" escape
escape ::= ["\\/bfnrt] | "u" hex hex hex hex
hex ::= [0-9a-fA-F]
""";
    }

    private static string EscapeGrammarLiteral(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    public static string ResolveModelArgument(string modelPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return modelPath;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return modelPath;
        }

        string relativePath = Path.GetRelativePath(workingDirectory, modelPath);
        return IsSafeRelativePath(relativePath)
            ? relativePath
            : modelPath;
    }

    private static string NormalizeGpuLayersArgument(string? gpuLayersArgument)
    {
        if (string.IsNullOrWhiteSpace(gpuLayersArgument))
        {
            return LlamaGpuSettingsResolver.BalancedGpuLayersArgument;
        }

        string normalized = gpuLayersArgument.Trim();
        return normalized.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : normalized;
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
            && !Path.IsPathRooted(relativePath)
            && !relativePath.StartsWith("..", StringComparison.Ordinal);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private AdviceRuntimeInvocationException CreateInvocationException(
        string message,
        DateTime startedUtc,
        long durationMs,
        string workingDirectory,
        string modelArgument,
        string commandLine,
        int promptLength,
        string promptPreview,
        int? exitCode,
        bool timedOut,
        string stdout,
        string stderr)
    {
        AdviceRuntimeInvocationLog log = new(
            startedUtc,
            Name,
            runtime.CliPath,
            workingDirectory,
            modelArgument,
            commandLine,
            promptLength,
            promptPreview,
            runtime.MaxTokens,
            runtime.ContextSize,
            runtime.TimeoutMs,
            durationMs,
            exitCode,
            timedOut,
            false,
            stdout,
            stderr,
            message);
        string diagnosticPath = AdviceRuntimeDiagnosticsLogger.Write(log);
        return new AdviceRuntimeInvocationException(message, log, diagnosticPath);
    }

    private static string TryGetTaskResult(Task<string> task)
    {
        if (!task.IsCompleted)
        {
            return string.Empty;
        }

        return task.GetAwaiter().GetResult();
    }

    private static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        IEnumerable<string> quotedArguments = arguments.Select(QuoteArgument);
        return $"{QuoteArgument(executablePath)} {string.Join(' ', quotedArguments)}".Trim();
    }

    private static string QuoteArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;
    }

    private static string BuildPromptPreview(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        string normalized = prompt.Replace("\r\n", "\n").Trim();
        return normalized.Length <= 1200
            ? normalized
            : $"{normalized[..1200]}...";
    }

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "no error output";
        }

        string normalized = text.Trim();
        return normalized.Length <= 220
            ? normalized
            : $"{normalized[..217]}...";
    }
}
