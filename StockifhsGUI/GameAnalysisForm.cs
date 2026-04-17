using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class GameAnalysisForm : Form
{
    private static readonly EngineAnalysisOptions DefaultAnalysisOptions = new();

    private readonly ImportedGame importedGame;
    private readonly GameAnalysisService analysisService;
    private readonly ExplanationGenerator explanationGenerator = new();
    private readonly Action<MoveAnalysisResult>? navigateToMove;
    private readonly ComboBox sideComboBox;
    private readonly ComboBox qualityFilterComboBox;
    private readonly ComboBox explanationLevelComboBox;
    private readonly Button analyzeButton;
    private readonly Button showOnBoardButton;
    private readonly Label summaryLabel;
    private readonly ListBox mistakesListBox;
    private readonly TextBox detailsTextBox;

    private GameAnalysisResult? currentResult;
    private bool currentResultIsCached;

    public GameAnalysisForm(ImportedGame importedGame, IEngineAnalyzer engineAnalyzer, Action<MoveAnalysisResult>? navigateToMove = null)
    {
        this.importedGame = importedGame ?? throw new ArgumentNullException(nameof(importedGame));
        this.navigateToMove = navigateToMove;
        analysisService = new GameAnalysisService(engineAnalyzer ?? throw new ArgumentNullException(nameof(engineAnalyzer)));

        Text = "Imported Game Analysis";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 620);
        MinimumSize = new Size(920, 620);

        Label headerLabel = new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(16, 16),
            Text = $"{importedGame.WhitePlayer ?? "White"} vs {importedGame.BlackPlayer ?? "Black"}"
        };
        Controls.Add(headerLabel);

        sideComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(16, 52),
            Size = new Size(220, 28)
        };
        sideComboBox.Items.Add(new SideOption(PlayerSide.White, $"Analyze White ({importedGame.WhitePlayer ?? "White"})"));
        sideComboBox.Items.Add(new SideOption(PlayerSide.Black, $"Analyze Black ({importedGame.BlackPlayer ?? "Black"})"));
        sideComboBox.SelectedIndex = 0;
        sideComboBox.SelectedIndexChanged += (_, _) => HandleSelectedSideChanged();
        Controls.Add(sideComboBox);

        qualityFilterComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(392, 52),
            Size = new Size(180, 28)
        };
        qualityFilterComboBox.Items.AddRange(
        [
            new FilterOption("All highlights", null),
            new FilterOption("Blunders only", MoveQualityBucket.Blunder),
            new FilterOption("Mistakes only", MoveQualityBucket.Mistake),
            new FilterOption("Inaccuracies only", MoveQualityBucket.Inaccuracy)
        ]);
        qualityFilterComboBox.SelectedIndex = 0;
        qualityFilterComboBox.SelectedIndexChanged += (_, _) => ApplyFilter();
        Controls.Add(qualityFilterComboBox);

        explanationLevelComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(744, 52),
            Size = new Size(152, 28)
        };
        explanationLevelComboBox.Items.AddRange(
        [
            new ExplanationLevelOption(ExplanationLevel.Beginner, "Beginner"),
            new ExplanationLevelOption(ExplanationLevel.Intermediate, "Intermediate"),
            new ExplanationLevelOption(ExplanationLevel.Advanced, "Advanced")
        ]);
        explanationLevelComboBox.SelectedIndex = 1;
        explanationLevelComboBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        Controls.Add(explanationLevelComboBox);

        analyzeButton = new Button
        {
            Location = new Point(252, 50),
            Size = new Size(120, 32),
            Text = "Run Analysis"
        };
        analyzeButton.Click += async (_, _) => await RunAnalysisAsync();
        Controls.Add(analyzeButton);

        showOnBoardButton = new Button
        {
            Location = new Point(588, 50),
            Size = new Size(140, 32),
            Text = "Show On Board",
            Enabled = false
        };
        showOnBoardButton.Click += (_, _) => ShowSelectedMistakeOnBoard();
        Controls.Add(showOnBoardButton);

        summaryLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 92),
            Size = new Size(880, 44),
            Text = "Choose a side and run the analysis."
        };
        Controls.Add(summaryLabel);

        mistakesListBox = new ListBox
        {
            Location = new Point(16, 148),
            Size = new Size(360, 452),
            Font = new Font("Consolas", 10)
        };
        mistakesListBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateDetails();
            ShowSelectedMistakeOnBoard();
        };
        mistakesListBox.DoubleClick += (_, _) => ShowSelectedMistakeOnBoard();
        Controls.Add(mistakesListBox);

        detailsTextBox = new TextBox
        {
            Location = new Point(392, 148),
            Size = new Size(504, 452),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10)
        };
        Controls.Add(detailsTextBox);

        RestoreWindowState();
        TryLoadCachedResultForSelectedSide();
    }

    private async Task RunAnalysisAsync()
    {
        if (sideComboBox.SelectedItem is not SideOption selectedSide)
        {
            return;
        }

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            return;
        }

        analyzeButton.Enabled = false;
        sideComboBox.Enabled = false;
        UseWaitCursor = true;
        summaryLabel.Text = "Analyzing imported game with Stockfish. This may take a moment...";
        mistakesListBox.Items.Clear();
        detailsTextBox.Clear();
        showOnBoardButton.Enabled = false;

        try
        {
            GameAnalysisResult result = await Task.Run(() => analysisService.AnalyzeGame(
                importedGame,
                selectedSide.Side,
                DefaultAnalysisOptions));

            GameAnalysisCache.StoreResult(cacheKey, result);
            currentResult = result;
            currentResultIsCached = false;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            currentResult = null;
            currentResultIsCached = false;
            summaryLabel.Text = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
            analyzeButton.Enabled = true;
            sideComboBox.Enabled = true;
        }
    }

    private void ApplyFilter()
    {
        mistakesListBox.Items.Clear();
        detailsTextBox.Clear();
        showOnBoardButton.Enabled = false;

        if (currentResult is null)
        {
            summaryLabel.Text = "Choose a side and run the analysis.";
            return;
        }

        FilterOption? filter = qualityFilterComboBox.SelectedItem as FilterOption;
        List<SelectedMistake> visibleMistakes = currentResult.HighlightedMistakes
            .Where(mistake => filter?.QualityFilter is null || mistake.Quality == filter.QualityFilter.Value)
            .ToList();

        summaryLabel.Text = BuildSummaryText(currentResult, visibleMistakes.Count, filter, currentResultIsCached);

        foreach (SelectedMistake mistake in visibleMistakes)
        {
            mistakesListBox.Items.Add(new SelectedMistakeViewItem(mistake, BuildListLabel(mistake)));
        }

        if (mistakesListBox.Items.Count > 0)
        {
            mistakesListBox.SelectedIndex = 0;
        }
    }

    private void UpdateDetails()
    {
        if (mistakesListBox.SelectedItem is not SelectedMistakeViewItem item)
        {
            detailsTextBox.Clear();
            showOnBoardButton.Enabled = false;
            return;
        }

        showOnBoardButton.Enabled = navigateToMove is not null;

        SelectedMistake mistake = item.Mistake;
        MoveAnalysisResult lead = mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();
        ExplanationLevel explanationLevel = explanationLevelComboBox.SelectedItem is ExplanationLevelOption levelOption
            ? levelOption.Level
            : ExplanationLevel.Intermediate;
        MoveExplanation explanation = explanationGenerator.Generate(
            lead.Replay,
            lead.Quality,
            lead.MistakeTag,
            lead.BeforeAnalysis.BestMoveUci,
            lead.CentipawnLoss,
            explanationLevel);

        StringBuilder builder = new();
        builder.AppendLine($"Moves: {BuildMoveRange(mistake)}");
        builder.AppendLine($"Quality: {mistake.Quality}");
        builder.AppendLine($"Label: {mistake.Tag?.Label ?? "unclassified"}");
        builder.AppendLine($"Confidence: {(mistake.Tag?.Confidence ?? 0):0.00}");
        builder.AppendLine($"Phase: {lead.Replay.Phase}");
        builder.AppendLine($"Played move: {FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)}");
        builder.AppendLine($"Best move: {FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci)}");
        builder.AppendLine($"Eval before: {FormatScore(lead.EvalBeforeCp, lead.BestMateIn)}");
        builder.AppendLine($"Eval after: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
        builder.AppendLine($"Centipawn loss: {(lead.CentipawnLoss?.ToString() ?? "n/a")}");
        builder.AppendLine($"Material delta: {lead.MaterialDeltaCp}");
        builder.AppendLine();
        builder.AppendLine($"Explanation ({explanationLevel}):");
        builder.AppendLine(explanation.ShortText);

        if (!string.IsNullOrWhiteSpace(explanation.DetailedText))
        {
            builder.AppendLine();
            builder.AppendLine("Detailed explanation:");
            builder.AppendLine(explanation.DetailedText);
        }

        builder.AppendLine();
        builder.AppendLine("Training hint:");
        builder.AppendLine(explanation.TrainingHint);

        if (mistake.Tag?.Evidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Evidence:");
            foreach (string evidence in mistake.Tag.Evidence)
            {
                builder.AppendLine($"- {evidence}");
            }
        }

        if (lead.BeforeAnalysis.Lines.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Engine candidates:");
            builder.AppendLine(FormatEngineCandidates(lead.Replay.FenBefore, lead.BeforeAnalysis.Lines));
        }

        EngineLine? playedContinuation = lead.AfterAnalysis.Lines.FirstOrDefault();
        if (playedContinuation is not null && playedContinuation.Pv.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Likely continuation after played move:");
            builder.AppendLine($"Score: {FormatEngineScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
            builder.AppendLine(FormatPrincipalVariation(lead.Replay.FenAfter, playedContinuation.Pv, maxHalfMoves: 8));
        }

        builder.AppendLine();
        builder.AppendLine("Board navigation:");
        builder.AppendLine("Use 'Show On Board' to jump to this position in the main window.");

        detailsTextBox.Text = builder.ToString();
    }

    private void ShowSelectedMistakeOnBoard()
    {
        if (navigateToMove is null || mistakesListBox.SelectedItem is not SelectedMistakeViewItem item)
        {
            return;
        }

        MoveAnalysisResult lead = item.Mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();

        navigateToMove(lead);
    }

    private static string BuildListLabel(SelectedMistake mistake)
    {
        MoveAnalysisResult lead = mistake.Moves
            .OrderByDescending(move => move.Quality)
            .ThenByDescending(move => move.CentipawnLoss ?? 0)
            .First();

        string moveRange = BuildMoveRange(mistake);
        string label = mistake.Tag?.Label ?? "unclassified";
        string cpl = lead.CentipawnLoss?.ToString() ?? "n/a";
        return $"{moveRange,-22} {mistake.Quality,-10} {label,-18} CPL {cpl}";
    }

    private static string BuildMoveRange(SelectedMistake mistake)
    {
        MoveAnalysisResult first = mistake.Moves.First();
        MoveAnalysisResult last = mistake.Moves.Last();

        string firstMove = $"{first.Replay.MoveNumber}{(first.Replay.Side == PlayerSide.White ? "." : "...")} {first.Replay.San}";
        if (mistake.Moves.Count == 1)
        {
            return firstMove;
        }

        string lastMove = $"{last.Replay.MoveNumber}{(last.Replay.Side == PlayerSide.White ? "." : "...")} {last.Replay.San}";
        return $"{firstMove} -> {lastMove}";
    }

    private static string FormatScore(int? centipawns, int? mateIn)
    {
        if (mateIn is int mate)
        {
            return $"mate {mate}";
        }

        return centipawns is int cp ? $"{cp} cp" : "n/a";
    }

    private static string FormatMoveFromFen(string fenBefore, string? uciMove)
    {
        if (string.IsNullOrWhiteSpace(uciMove))
        {
            return "(unknown)";
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _)
            || !game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _)
            || appliedMove is null)
        {
            return uciMove;
        }

        return FormatSanAndUci(appliedMove.San, appliedMove.Uci);
    }

    private static string FormatSanAndUci(string san, string uci)
    {
        return string.Equals(san, uci, StringComparison.OrdinalIgnoreCase)
            ? san
            : $"{san} ({uci})";
    }

    private void HandleSelectedSideChanged()
    {
        TryLoadCachedResultForSelectedSide();
    }

    private void RestoreWindowState()
    {
        if (!GameAnalysisCache.TryGetWindowState(importedGame, out AnalysisWindowState? state) || state is null)
        {
            return;
        }

        sideComboBox.SelectedIndex = state.SelectedSide == PlayerSide.Black ? 1 : 0;
        qualityFilterComboBox.SelectedIndex = Math.Clamp(state.QualityFilterIndex, 0, qualityFilterComboBox.Items.Count - 1);
        explanationLevelComboBox.SelectedIndex = Math.Clamp(state.ExplanationLevelIndex, 0, explanationLevelComboBox.Items.Count - 1);
    }

    private bool TryLoadCachedResultForSelectedSide()
    {
        mistakesListBox.Items.Clear();
        detailsTextBox.Clear();
        showOnBoardButton.Enabled = false;

        if (sideComboBox.SelectedItem is not SideOption selectedSide)
        {
            currentResult = null;
            currentResultIsCached = false;
            summaryLabel.Text = "Choose a side and run the analysis.";
            return false;
        }

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            return true;
        }

        currentResult = null;
        currentResultIsCached = false;
        summaryLabel.Text = $"No cached analysis for {selectedSide.Side}. Run analysis to generate it.";
        return false;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (sideComboBox.SelectedItem is SideOption selectedSide)
        {
            GameAnalysisCache.StoreWindowState(
                importedGame,
                new AnalysisWindowState(
                    selectedSide.Side,
                    qualityFilterComboBox.SelectedIndex,
                    explanationLevelComboBox.SelectedIndex));
        }

        base.OnFormClosed(e);
    }

    private sealed record SideOption(PlayerSide Side, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record FilterOption(string Label, MoveQualityBucket? QualityFilter)
    {
        public override string ToString() => Label;
    }

    private sealed record ExplanationLevelOption(ExplanationLevel Level, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SelectedMistakeViewItem(SelectedMistake Mistake, string Label)
    {
        public override string ToString() => Label;
    }

    private static string FormatEngineCandidates(string fenBefore, IReadOnlyList<EngineLine> lines)
    {
        StringBuilder builder = new();

        for (int i = 0; i < lines.Count; i++)
        {
            EngineLine line = lines[i];
            string moveLabel = FormatMoveFromFen(fenBefore, line.MoveUci);
            string pv = FormatPrincipalVariation(fenBefore, line.Pv, maxHalfMoves: 8);
            builder.AppendLine($"{i + 1}. {moveLabel} | {FormatEngineScore(line.Centipawns, line.MateIn)}");
            if (!string.IsNullOrWhiteSpace(pv))
            {
                builder.AppendLine($"   PV: {pv}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildSummaryText(GameAnalysisResult result, int visibleCount, FilterOption? filter, bool isCached)
    {
        int blunders = result.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Blunder);
        int mistakes = result.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = result.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Inaccuracy);
        string filterText = filter?.QualityFilter is null ? "all highlighted mistakes" : filter!.Label.ToLowerInvariant();
        string cacheSuffix = isCached ? " Loaded from cache." : string.Empty;

        return visibleCount == 0
            ? $"No items match the current filter ({filterText}) for {result.AnalyzedSide}.{cacheSuffix}"
            : $"Showing {visibleCount} items for {result.AnalyzedSide}. Highlights: {blunders} blunders, {mistakes} mistakes, {inaccuracies} inaccuracies.{cacheSuffix}";
    }

    private static string FormatEngineScore(int? centipawns, int? mateIn)
    {
        if (mateIn is int mate)
        {
            return $"mate {mate}";
        }

        if (centipawns is not int cp)
        {
            return "n/a";
        }

        double pawns = cp / 100.0;
        return pawns >= 0 ? $"+{pawns:0.0}" : $"{pawns:0.0}";
    }

    private static string FormatPrincipalVariation(string fenBefore, IReadOnlyList<string> pv, int maxHalfMoves)
    {
        ChessGame game = new();
        if (!game.TryLoadFen(fenBefore, out _))
        {
            return string.Join(' ', pv.Take(maxHalfMoves));
        }

        List<string> formattedMoves = new(Math.Min(pv.Count, maxHalfMoves));
        foreach (string uciMove in pv.Take(maxHalfMoves))
        {
            if (!game.TryApplyUci(uciMove, out AppliedMoveInfo? appliedMove, out _) || appliedMove is null)
            {
                formattedMoves.Add(uciMove);
                continue;
            }

            formattedMoves.Add($"{appliedMove.MoveNumber}{(appliedMove.WhiteMoved ? "." : "...")} {appliedMove.San}");
        }

        if (pv.Count > maxHalfMoves)
        {
            formattedMoves.Add("...");
        }

        return string.Join(" ", formattedMoves);
    }
}
