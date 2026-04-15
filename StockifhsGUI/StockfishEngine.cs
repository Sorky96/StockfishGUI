using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public class StockfishEngine : IDisposable
{
    private readonly Process engineProcess;
    private readonly Queue<string> moveQueue = new();
    private readonly object processLock = new();

    public StockfishEngine(string enginePath)
    {
        if (!File.Exists(enginePath))
        {
            throw new FileNotFoundException("Could not find stockfish.exe next to the application.", enginePath);
        }

        engineProcess = new Process
        {
            StartInfo =
            {
                FileName = enginePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        engineProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (moveQueue)
                {
                    moveQueue.Enqueue(e.Data);
                }
            }
        };

        engineProcess.Start();
        engineProcess.BeginOutputReadLine();

        lock (processLock)
        {
            SendCommand("uci");
            WaitForToken("uciok", 3000);
            SendCommand("isready");
            WaitForToken("readyok", 3000);
        }
    }

    public void SendCommand(string command)
    {
        lock (processLock)
        {
            engineProcess.StandardInput.WriteLine(command);
            engineProcess.StandardInput.Flush();
        }
    }

    public void AddMove(IEnumerable<string> allMoves)
    {
        lock (processLock)
        {
            FlushQueue();
            string joinedMoves = string.Join(" ", allMoves);
            SendCommand(string.IsNullOrWhiteSpace(joinedMoves)
                ? "position startpos"
                : $"position startpos moves {joinedMoves}");
            SendCommand("isready");
            WaitForToken("readyok", 3000);
        }
    }

    public List<string> GetTopMoves(int count)
    {
        IReadOnlyList<string> lines = Analyze(depth: 15);
        List<string> results = new();

        foreach (string line in lines)
        {
            if (!line.StartsWith("info", StringComparison.Ordinal) || !line.Contains(" multipv ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int pvIndex = Array.IndexOf(parts, "pv");
            if (pvIndex == -1 || pvIndex + 1 >= parts.Length)
            {
                continue;
            }

            string move = parts[pvIndex + 1];
            if (!results.Contains(move))
            {
                results.Add(move);
            }

            if (results.Count >= count)
            {
                break;
            }
        }

        return results;
    }

    public string GetRawEvaluation()
    {
        return string.Join(Environment.NewLine, Analyze(depth: 10));
    }

    public EvaluationSummary? GetEvaluationSummary(int depth = 12)
    {
        IReadOnlyList<string> lines = Analyze(depth);

        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string line = lines[i];
            if (!line.StartsWith("info", StringComparison.Ordinal) || !line.Contains(" score ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int scoreIndex = Array.IndexOf(parts, "score");
            if (scoreIndex == -1 || scoreIndex + 2 >= parts.Length)
            {
                continue;
            }

            string scoreType = parts[scoreIndex + 1];
            if (!int.TryParse(parts[scoreIndex + 2], out int value))
            {
                continue;
            }

            return scoreType switch
            {
                "cp" => new EvaluationSummary(value, null),
                "mate" => new EvaluationSummary(null, value),
                _ => null
            };
        }

        return null;
    }

    public bool IsGameOver()
    {
        IReadOnlyList<string> lines = Analyze(depth: 10);
        return lines.Any(line => line.Contains(" mate 0", StringComparison.Ordinal) || line.StartsWith("bestmove (none)", StringComparison.Ordinal));
    }

    public string ConvertSANToUCI(string movesHistory, string sanMove)
    {
        lock (processLock)
        {
            FlushQueue();
            SendCommand("ucinewgame");
            SendCommand($"position startpos moves {movesHistory}");
            SendCommand($"go searchmoves {sanMove}");
        }

        return string.Empty;
    }

    private IReadOnlyList<string> Analyze(int depth)
    {
        lock (processLock)
        {
            FlushQueue();
            SendCommand($"go depth {depth}");

            List<string> lines = new();
            DateTime startedAt = DateTime.UtcNow;

            while ((DateTime.UtcNow - startedAt).TotalSeconds < 5)
            {
                string? line = TryDequeueLine();
                if (line is null)
                {
                    Thread.Sleep(10);
                    continue;
                }

                lines.Add(line);
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                {
                    return lines;
                }
            }

            SendCommand("stop");
            DateTime stopWaitStarted = DateTime.UtcNow;
            while ((DateTime.UtcNow - stopWaitStarted).TotalSeconds < 1)
            {
                string? line = TryDequeueLine();
                if (line is null)
                {
                    Thread.Sleep(10);
                    continue;
                }

                lines.Add(line);
                if (line.StartsWith("bestmove", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return lines;
        }
    }

    private void WaitForToken(string token, int timeoutMs)
    {
        DateTime startedAt = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
        {
            string? line = TryDequeueLine();
            if (line is null)
            {
                Thread.Sleep(10);
                continue;
            }

            if (line.Contains(token, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new TimeoutException($"Stockfish did not return '{token}' within {timeoutMs} ms.");
    }

    private string? TryDequeueLine()
    {
        lock (moveQueue)
        {
            return moveQueue.Count > 0 ? moveQueue.Dequeue() : null;
        }
    }

    private void FlushQueue()
    {
        lock (moveQueue)
        {
            moveQueue.Clear();
        }
    }

    public void Dispose()
    {
        lock (processLock)
        {
            if (engineProcess.HasExited)
            {
                return;
            }

            try
            {
                engineProcess.StandardInput.WriteLine("quit");
                engineProcess.StandardInput.Flush();
                if (!engineProcess.WaitForExit(1000))
                {
                    engineProcess.Kill(true);
                }
            }
            catch
            {
                if (!engineProcess.HasExited)
                {
                    engineProcess.Kill(true);
                }
            }
        }

        engineProcess.Dispose();
    }
}

public sealed record EvaluationSummary(int? Centipawns, int? MateIn);
