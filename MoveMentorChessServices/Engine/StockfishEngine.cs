using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MoveMentorChessServices;

public sealed class StockfishEngine : IEngineAnalyzer, IDisposable
{
    private readonly Process engineProcess;
    private readonly Queue<string> outputQueue = new();
    private readonly object processLock = new();
    private string currentFen = "startpos";

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
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (outputQueue)
            {
                outputQueue.Enqueue(e.Data);
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
            currentFen = "startpos";
            SendCommand(string.IsNullOrWhiteSpace(joinedMoves)
                ? "position startpos"
                : $"position startpos moves {joinedMoves}");
            SendCommand("isready");
            WaitForToken("readyok", 3000);
        }
    }

    public void SetPositionFen(string fen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);

        lock (processLock)
        {
            FlushQueue();
            currentFen = fen;
            SendCommand($"position fen {fen}");
            SendCommand("isready");
            WaitForToken("readyok", 3000);
        }
    }

    public EngineAnalysis AnalyzePosition(string fen, EngineAnalysisOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);
        ArgumentNullException.ThrowIfNull(options);

        lock (processLock)
        {
            FlushQueue();
            currentFen = fen;
            SendCommand($"setoption name MultiPV value {Math.Max(1, options.MultiPv)}");
            SendCommand($"position fen {fen}");
            SendCommand("isready");
            WaitForToken("readyok", 3000);

            IReadOnlyList<string> lines = Analyze(options);
            return ParseAnalysis(fen, lines);
        }
    }

    public List<string> GetTopMoves(int count)
    {
        EngineAnalysis analysis = AnalyzeCurrentPosition(new EngineAnalysisOptions(Depth: 15, MultiPv: Math.Max(1, count)));
        return analysis.Lines
            .Select(line => line.MoveUci)
            .Distinct(StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    public string GetRawEvaluation()
    {
        EngineAnalysis analysis = AnalyzeCurrentPosition(new EngineAnalysisOptions(Depth: 10, MultiPv: 3));
        StringBuilder builder = new();
        foreach (EngineLine line in analysis.Lines)
        {
            string score = line.MateIn is int mate
                ? $"mate {mate}"
                : $"cp {line.Centipawns ?? 0}";
            builder.AppendLine($"{line.MoveUci}: {score} | {string.Join(' ', line.Pv)}");
        }

        return builder.ToString().TrimEnd();
    }

    public EvaluationSummary? GetEvaluationSummary(int depth = 12)
    {
        EngineAnalysis analysis = AnalyzeCurrentPosition(new EngineAnalysisOptions(Depth: depth, MultiPv: 1));
        EngineLine? bestLine = analysis.Lines.FirstOrDefault();
        if (bestLine is null)
        {
            return null;
        }

        return new EvaluationSummary(bestLine.Centipawns, bestLine.MateIn);
    }

    public bool IsGameOver()
    {
        EngineAnalysis analysis = AnalyzeCurrentPosition(new EngineAnalysisOptions(Depth: 10, MultiPv: 1));
        return string.Equals(analysis.BestMoveUci, "(none)", StringComparison.Ordinal)
            || analysis.Lines.Any(line => line.MateIn == 0);
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

    private EngineAnalysis AnalyzeCurrentPosition(EngineAnalysisOptions options)
    {
        lock (processLock)
        {
            FlushQueue();
            SendCommand($"setoption name MultiPV value {Math.Max(1, options.MultiPv)}");
            SendCommand(currentFen == "startpos" ? "position startpos" : $"position fen {currentFen}");
            SendCommand("isready");
            WaitForToken("readyok", 3000);

            IReadOnlyList<string> lines = Analyze(options);
            return ParseAnalysis(currentFen, lines);
        }
    }

    private IReadOnlyList<string> Analyze(EngineAnalysisOptions options)
    {
        string goCommand = options.MoveTimeMs is int moveTimeMs && moveTimeMs > 0
            ? $"go movetime {moveTimeMs}"
            : $"go depth {Math.Max(1, options.Depth)}";

        SendCommand(goCommand);

        List<string> lines = new();
        DateTime startedAt = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAt).TotalSeconds < 15)
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
        while ((DateTime.UtcNow - stopWaitStarted).TotalSeconds < 2)
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

    private static EngineAnalysis ParseAnalysis(string fen, IReadOnlyList<string> lines)
    {
        SortedDictionary<int, EngineLine> parsedLines = new();
        string? bestMove = null;

        foreach (string line in lines)
        {
            if (line.StartsWith("bestmove", StringComparison.Ordinal))
            {
                string[] bestMoveParts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (bestMoveParts.Length >= 2)
                {
                    bestMove = bestMoveParts[1];
                }

                continue;
            }

            if (!line.StartsWith("info", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int scoreIndex = Array.IndexOf(parts, "score");
            int pvIndex = Array.IndexOf(parts, "pv");
            if (scoreIndex < 0 || scoreIndex + 2 >= parts.Length || pvIndex < 0 || pvIndex + 1 >= parts.Length)
            {
                continue;
            }

            int multiPv = 1;
            int multiPvIndex = Array.IndexOf(parts, "multipv");
            if (multiPvIndex >= 0 && multiPvIndex + 1 < parts.Length && int.TryParse(parts[multiPvIndex + 1], out int parsedMultiPv))
            {
                multiPv = parsedMultiPv;
            }

            string scoreType = parts[scoreIndex + 1];
            if (!int.TryParse(parts[scoreIndex + 2], out int scoreValue))
            {
                continue;
            }

            List<string> pv = parts.Skip(pvIndex + 1).ToList();
            if (pv.Count == 0)
            {
                continue;
            }

            int? centipawns = scoreType == "cp" ? scoreValue : null;
            int? mateIn = scoreType == "mate" ? scoreValue : null;
            parsedLines[multiPv] = new EngineLine(pv[0], centipawns, mateIn, pv);
        }

        return new EngineAnalysis(fen, parsedLines.Values.ToList(), bestMove);
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
        lock (outputQueue)
        {
            return outputQueue.Count > 0 ? outputQueue.Dequeue() : null;
        }
    }

    private void FlushQueue()
    {
        lock (outputQueue)
        {
            outputQueue.Clear();
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
