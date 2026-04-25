// Standalone quality evaluation: sends real prompts to llama-server and captures outputs.
// Run with: dotnet run --project MoveMentorChessServices -- --eval-advice
// Or compile and run directly.

using System.Text;
using System.Text.Json;

namespace MoveMentorChessServices;

public static class AdviceQualityEvaluator
{
    private static readonly (string Name, ReplayPly Replay, MoveQualityBucket Quality, MistakeTag Tag, string BestMoveUci, int CentipawnLoss, ExplanationLevel Level)[] TestCases =
    [
        (
            "Hanging knight in middlegame (Beginner)",
            new ReplayPly(12, 6, PlayerSide.White, "Nd4", "Nd4", "d2d4",
                "r1bqkb1r/pppp1ppp/2n2n2/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R w KQkq - 2 4",
                "r1bqkb1r/pppp1ppp/2n2n2/4p3/3NP3/8/PPP2PPP/RNBQKB1R b KQkq - 3 4",
                string.Empty, string.Empty, GamePhase.Opening, "N", null, "f3", "d4", false, false, false),
            MoveQualityBucket.Mistake,
            new MistakeTag("hanging_piece", 0.85, ["piece_lost_or_underdefended"]),
            "d4d5",
            145,
            ExplanationLevel.Beginner
        ),
        (
            "Missed tactic Nxe5 (Intermediate)",
            new ReplayPly(20, 10, PlayerSide.White, "Be2", "Be2", "f1e2",
                "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
                "r1bqkb1r/pppp1ppp/2n2n2/4p3/4P3/5N2/PPPPBPPP/RNBQK2R b KQkq - 5 4",
                string.Empty, string.Empty, GamePhase.Middlegame, "B", null, "f1", "e2", false, false, false),
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("missed_tactic", 0.78, ["missed_capture_sequence"]),
            "f3e5",
            95,
            ExplanationLevel.Intermediate
        ),
        (
            "King safety g4 push (Advanced)",
            new ReplayPly(30, 15, PlayerSide.White, "g4", "g4", "g2g4",
                "r2q1rk1/ppp2ppp/2np1n2/2b1p1B1/2B1P1b1/2NP1N2/PPP2PPP/R2Q1RK1 w - - 6 8",
                "r2q1rk1/ppp2ppp/2np1n2/2b1p1B1/2B1P1Pb/2NP1N2/PPP2P1P/R2Q1RK1 b - g3 0 8",
                string.Empty, string.Empty, GamePhase.Middlegame, "P", null, "g2", "g4", false, false, false),
            MoveQualityBucket.Blunder,
            new MistakeTag("king_safety", 0.92, ["material_swing_detected"]),
            "h2h3",
            320,
            ExplanationLevel.Advanced
        ),
        (
            "Early queen Qa5 (Intermediate)",
            new ReplayPly(8, 4, PlayerSide.Black, "Qa5", "Qa5", "d8a5",
                "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2",
                "rnb1kbnr/pppp1ppp/8/q3p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
                string.Empty, string.Empty, GamePhase.Opening, "Q", null, "d8", "a5", false, false, false),
            MoveQualityBucket.Inaccuracy,
            new MistakeTag("opening_principles", 0.80, ["early_queen_move"]),
            "g8f6",
            75,
            ExplanationLevel.Intermediate
        ),
        (
            "Passive endgame Ke2 (Intermediate)",
            new ReplayPly(60, 30, PlayerSide.White, "Ke2", "Ke2", "e1e2",
                "8/5ppp/8/8/4k3/8/5PPP/4K3 w - - 0 30",
                "8/5ppp/8/8/4k3/8/4KPPP/8 b - - 1 30",
                string.Empty, string.Empty, GamePhase.Endgame, "K", null, "e1", "e2", false, false, false),
            MoveQualityBucket.Mistake,
            new MistakeTag("endgame_technique", 0.75, ["missed_king_centralization"]),
            "e1d2",
            130,
            ExplanationLevel.Intermediate
        )
    ];

    public static void RunEvaluation()
    {
        Console.WriteLine("=== Advice Quality Evaluation ===");
        Console.WriteLine();

        LlamaCppServerConfig? config = LlamaCppServerResolver.Resolve();
        if (config is null)
        {
            Console.WriteLine("ERROR: llama-server not configured. Set MoveMentorChessServices_LLAMA_CPP_SERVER_PATH.");
            return;
        }

        LlamaCppHttpAdviceModel model = new(config);
        if (!model.IsAvailable)
        {
            Console.WriteLine("ERROR: model files not found.");
            return;
        }

        Console.WriteLine($"Server: {config.ServerPath}");
        Console.WriteLine($"Model: {config.ModelPath}");
        Console.WriteLine();

        StringBuilder report = new();
        report.AppendLine("# Advice Quality Evaluation Report");
        report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"Model: {Path.GetFileName(config.ModelPath)}");
        report.AppendLine();

        int passed = 0;
        int failed = 0;

        foreach (var testCase in TestCases)
        {
            Console.Write($"[{testCase.Level}] {testCase.Name}... ");

            LocalModelAdviceRequest request = new(
                testCase.Replay,
                testCase.Quality,
                testCase.Tag,
                testCase.BestMoveUci,
                testCase.CentipawnLoss,
                testCase.Level,
                null,
                string.Empty);
            request = request with { Prompt = AdvicePromptFormatter.BuildPrompt(request) };

            string? rawResponse = model.Generate(request);

            report.AppendLine($"## {testCase.Name}");
            report.AppendLine($"- Quality: {testCase.Quality}, Label: {testCase.Tag.Label}, CPL: {testCase.CentipawnLoss}");
            report.AppendLine($"- Played: {testCase.Replay.San}, Best: {testCase.BestMoveUci}");
            report.AppendLine($"- Level: {testCase.Level}");
            report.AppendLine();

            if (rawResponse is null)
            {
                Console.WriteLine("FAIL (null response)");
                report.AppendLine("**Result: FAIL** — null response from model");
                report.AppendLine();
                failed++;
                continue;
            }

            if (!LocalModelAdviceResponseParser.TryParse(rawResponse, out LocalModelAdviceResponse? parsed) || parsed is null)
            {
                Console.WriteLine("FAIL (parse error)");
                report.AppendLine("**Result: FAIL** — could not parse response");
                report.AppendLine($"```\n{rawResponse}\n```");
                report.AppendLine();
                failed++;
                continue;
            }

            Console.WriteLine("OK");
            report.AppendLine("**Result: OK**");
            report.AppendLine();
            report.AppendLine($"**short_text**: {parsed.ShortText}");
            report.AppendLine();
            report.AppendLine($"**detailed_text**: {parsed.DetailedText}");
            report.AppendLine();
            report.AppendLine($"**training_hint**: {parsed.TrainingHint}");
            report.AppendLine();
            report.AppendLine("---");
            report.AppendLine();
            passed++;
        }

        report.AppendLine($"## Summary: {passed} passed, {failed} failed out of {TestCases.Length}");

        string reportPath = Path.Combine(AppContext.BaseDirectory, "advice-quality-report.md");
        File.WriteAllText(reportPath, report.ToString());

        Console.WriteLine();
        Console.WriteLine($"Results: {passed}/{TestCases.Length} passed");
        Console.WriteLine($"Report saved to: {reportPath}");
    }
}
