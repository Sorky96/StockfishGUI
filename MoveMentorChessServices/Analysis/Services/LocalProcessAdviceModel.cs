using System.Diagnostics;
using System.Text;

namespace MoveMentorChessServices;

public sealed class LocalProcessAdviceModel : ILocalAdviceModel
{
    private readonly LocalAdviceModelOptions options;

    public LocalProcessAdviceModel(LocalAdviceModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Command))
        {
            throw new ArgumentException("Local advice model command must be provided.", nameof(options));
        }

        this.options = options;
    }

    public string Name => Path.GetFileNameWithoutExtension(options.Command) is { Length: > 0 } fileName
        ? fileName
        : options.Command;

    public bool IsAvailable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(options.WorkingDirectory) is false
                && !Directory.Exists(options.WorkingDirectory))
            {
                return false;
            }

            return HasExplicitPath(options.Command)
                ? File.Exists(options.Command)
                : true;
        }
    }

    public string? Generate(LocalModelAdviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using Process process = new();
        process.StartInfo = CreateStartInfo();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start local advice model '{options.Command}'.");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        process.StandardInput.Write(request.Prompt);
        process.StandardInput.Flush();
        process.StandardInput.Close();

        if (!process.WaitForExit(options.TimeoutMs))
        {
            TryKillProcess(process);
            throw new TimeoutException($"Local advice model '{Name}' exceeded timeout of {options.TimeoutMs} ms.");
        }

        Task.WaitAll([stdoutTask, stderrTask], options.TimeoutMs);

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Local advice model '{Name}' exited with code {process.ExitCode}: {Shorten(stderr)}");
        }

        if (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidOperationException(
                $"Local advice model '{Name}' produced no stdout. Stderr: {Shorten(stderr)}");
        }

        return NormalizeOutput(stdout);
    }

    private ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new(options.Command)
        {
            Arguments = options.Arguments ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (string.IsNullOrWhiteSpace(options.WorkingDirectory) is false)
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        return startInfo;
    }

    private static bool HasExplicitPath(string command)
        => command.IndexOf(Path.DirectorySeparatorChar) >= 0
            || command.IndexOf(Path.AltDirectorySeparatorChar) >= 0;

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

    private static string NormalizeOutput(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return string.Empty;
        }

        return stdout.Replace("\u0000", string.Empty).Trim();
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
