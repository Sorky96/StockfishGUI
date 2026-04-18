using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class GameAnalysisForm : Form
{
    private static readonly EngineAnalysisOptions DefaultAnalysisOptions = new();

    private readonly ImportedGame importedGame;
    private readonly IEngineAnalyzer engineAnalyzer;
    private readonly Action<MoveAnalysisResult>? navigateToMove;
    private readonly PlayerSide? preferredSide;
    private readonly ComboBox sideComboBox;
    private readonly ComboBox qualityFilterComboBox;
    private readonly ComboBox explanationLevelComboBox;
    private readonly Button analyzeButton;
    private readonly Button testAdviceButton;
    private readonly Button showOnBoardButton;
    private readonly Label adviceStatusLabel;
    private readonly Label summaryLabel;
    private readonly ListBox mistakesListBox;
    private readonly TextBox detailsTextBox;
    private readonly Dictionary<string, MoveExplanation> explanationCache = new();

    private GameAnalysisService analysisService;
    private IAdviceGenerator adviceGenerator;
    private GameAnalysisResult? currentResult;
    private bool currentResultIsCached;
    private int explanationRequestId;

    public GameAnalysisForm(
        ImportedGame importedGame,
        IEngineAnalyzer engineAnalyzer,
        Action<MoveAnalysisResult>? navigateToMove = null,
        PlayerSide? preferredSide = null)
    {
        this.importedGame = importedGame ?? throw new ArgumentNullException(nameof(importedGame));
        this.navigateToMove = navigateToMove;
        this.preferredSide = preferredSide;
        this.engineAnalyzer = engineAnalyzer ?? throw new ArgumentNullException(nameof(engineAnalyzer));
        adviceGenerator = AdviceGeneratorFactory.CreateDefault();
        analysisService = new GameAnalysisService(this.engineAnalyzer, adviceGenerator: adviceGenerator);
        UiTheme.ApplyFormChrome(this);

        Text = "Imported Game Analysis";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 620);
        MinimumSize = new Size(920, 620);

        Label headerLabel = new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(16, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Text = $"{importedGame.WhitePlayer ?? "White"} vs {importedGame.BlackPlayer ?? "Black"}"
        };
        Controls.Add(headerLabel);

        Label sideLabel = new()
        {
            AutoSize = true,
            Location = new Point(16, 34),
            Text = "Side"
        };
        UiTheme.StyleSectionLabel(sideLabel);
        Controls.Add(sideLabel);

        sideComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(16, 52),
            Size = new Size(220, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        UiTheme.StyleComboBox(sideComboBox);
        sideComboBox.Items.Add(new SideOption(PlayerSide.White, $"Analyze White ({importedGame.WhitePlayer ?? "White"})"));
        sideComboBox.Items.Add(new SideOption(PlayerSide.Black, $"Analyze Black ({importedGame.BlackPlayer ?? "Black"})"));
        sideComboBox.SelectedIndex = 0;
        sideComboBox.SelectedIndexChanged += (_, _) => HandleSelectedSideChanged();
        Controls.Add(sideComboBox);

        Label qualityLabel = new()
        {
            AutoSize = true,
            Location = new Point(392, 34),
            Text = "Highlight filter"
        };
        UiTheme.StyleSectionLabel(qualityLabel);
        Controls.Add(qualityLabel);

        qualityFilterComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(392, 52),
            Size = new Size(180, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        UiTheme.StyleComboBox(qualityFilterComboBox);
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

        Label explanationLabel = new()
        {
            AutoSize = true,
            Location = new Point(744, 34),
            Text = "Explanation level"
        };
        UiTheme.StyleSectionLabel(explanationLabel);
        Controls.Add(explanationLabel);

        explanationLevelComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(744, 52),
            Size = new Size(152, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        UiTheme.StyleComboBox(explanationLevelComboBox);
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
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Text = "Run Analysis"
        };
        UiTheme.StylePrimaryButton(analyzeButton);
        analyzeButton.Click += async (_, _) => await RunAnalysisAsync();
        Controls.Add(analyzeButton);

        showOnBoardButton = new Button
        {
            Location = new Point(588, 50),
            Size = new Size(140, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Text = "Show On Board",
            Enabled = false
        };
        UiTheme.StyleSecondaryButton(showOnBoardButton);
        showOnBoardButton.Click += (_, _) => ShowSelectedMistakeOnBoard();
        Controls.Add(showOnBoardButton);

        adviceStatusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 92),
            Size = new Size(712, 40),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true
        };
        UiTheme.StyleInfoLabel(adviceStatusLabel);
        Controls.Add(adviceStatusLabel);

        testAdviceButton = new Button
        {
            Location = new Point(744, 96),
            Size = new Size(152, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = "Test Advice Model"
        };
        UiTheme.StyleSecondaryButton(testAdviceButton);
        testAdviceButton.Click += async (_, _) => await TestAdviceRuntimeAsync();
        Controls.Add(testAdviceButton);

        summaryLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 140),
            Size = new Size(880, 44),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Choose a side and run the analysis."
        };
        UiTheme.StyleInfoLabel(summaryLabel);
        Controls.Add(summaryLabel);

        Label mistakesHeader = new()
        {
            AutoSize = true,
            Location = new Point(16, 184),
            Text = "Highlighted moments"
        };
        UiTheme.StyleSectionLabel(mistakesHeader);
        Controls.Add(mistakesHeader);

        Label detailsHeader = new()
        {
            AutoSize = true,
            Location = new Point(392, 184),
            Text = "Details and guidance"
        };
        UiTheme.StyleSectionLabel(detailsHeader);
        Controls.Add(detailsHeader);

        mistakesListBox = new ListBox
        {
            Location = new Point(16, 204),
            Size = new Size(360, 396),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Consolas", 10)
        };
        UiTheme.StyleListBox(mistakesListBox);
        mistakesListBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateDetails();
            ShowSelectedMistakeOnBoard();
        };
        mistakesListBox.DoubleClick += (_, _) => ShowSelectedMistakeOnBoard();
        Controls.Add(mistakesListBox);

        detailsTextBox = new TextBox
        {
            Location = new Point(392, 204),
            Size = new Size(504, 396),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10)
        };
        UiTheme.StyleTextBox(detailsTextBox);
        Controls.Add(detailsTextBox);

        RestoreWindowState();
        if (this.preferredSide.HasValue)
        {
            sideComboBox.SelectedIndex = this.preferredSide.Value == PlayerSide.Black ? 1 : 0;
        }
        RefreshAdviceRuntimeState();
        TryLoadCachedResultForSelectedSide();
    }

    private async Task RunAnalysisAsync()
    {
        RefreshAdviceRuntimeState();

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

    private async Task TestAdviceRuntimeAsync()
    {
        RefreshAdviceRuntimeState();
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();

        if (!status.IsReady)
        {
            MessageBox.Show(
                this,
                status.InstallHint is null ? status.StatusText : $"{status.StatusText}{Environment.NewLine}{Environment.NewLine}{status.InstallHint}",
                "Advice Model Not Ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        testAdviceButton.Enabled = false;
        analyzeButton.Enabled = false;
        UseWaitCursor = true;
        adviceStatusLabel.Text = $"Advice model: testing {status.RuntimeName ?? "local runtime"}...";

        try
        {
            AdviceRuntimeSmokeTestResult result = await Task.Run(AdviceRuntimeSmokeTester.Run);
            RefreshAdviceRuntimeState();
            MessageBox.Show(
                this,
                result.Message,
                result.Success ? "Advice Model Ready" : "Advice Model Test Failed",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            analyzeButton.Enabled = true;
            testAdviceButton.Enabled = true;
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
            explanationRequestId++;
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
        string cacheKey = BuildExplanationCacheKey(lead, explanationLevel);
        MoveExplanation explanation = lead.Explanation
            ?? new MoveExplanation("Explanation is loading...", "Training hint is loading...");

        bool isCached = explanationCache.TryGetValue(cacheKey, out MoveExplanation? cachedExplanation);
        if (isCached && cachedExplanation is not null)
        {
            explanation = cachedExplanation;
        }

        detailsTextBox.Text = BuildDetailsText(mistake, lead, explanationLevel, explanation, !isCached);

        if (!isCached)
        {
            int requestId = ++explanationRequestId;
            _ = LoadExplanationAsync(item, lead, explanationLevel, cacheKey, requestId);
        }
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

    private void RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        adviceGenerator = AdviceGeneratorFactory.CreateInteractiveGenerator();
        analysisService = new GameAnalysisService(
            engineAnalyzer,
            adviceGenerator: AdviceGeneratorFactory.CreateBulkAnalysisGenerator());
        adviceStatusLabel.Text = status.StatusText;
    }

    private async Task LoadExplanationAsync(
        SelectedMistakeViewItem item,
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        string cacheKey,
        int requestId)
    {
        MoveExplanation explanation;

        try
        {
            explanation = await Task.Run(() => adviceGenerator.Generate(
                lead.Replay,
                lead.Quality,
                lead.MistakeTag,
                lead.BeforeAnalysis.BestMoveUci,
                lead.CentipawnLoss,
                explanationLevel,
                new AdviceGenerationContext(
                    "game-analysis-form",
                    GameFingerprint.Compute(importedGame.PgnText),
                    currentResult?.AnalyzedSide)));
        }
        catch (Exception ex)
        {
            explanation = new MoveExplanation(
                "Local advice generation failed.",
                "Use the heuristic explanation for now.",
                ex.Message);
        }

        if (IsDisposed || requestId != explanationRequestId)
        {
            return;
        }

        explanationCache[cacheKey] = explanation;

        if (mistakesListBox.SelectedItem is not SelectedMistakeViewItem currentItem
            || !ReferenceEquals(currentItem, item))
        {
            return;
        }

        detailsTextBox.Text = BuildDetailsText(item.Mistake, lead, explanationLevel, explanation, false);
    }

    private static string BuildExplanationCacheKey(MoveAnalysisResult lead, ExplanationLevel explanationLevel)
        => $"{lead.Replay.Ply}:{lead.Replay.Uci}:{explanationLevel}";

    private static string BuildDetailsText(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        MoveExplanation explanation,
        bool isLoading)
    {
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

        if (isLoading)
        {
            builder.AppendLine();
            builder.AppendLine("Local advice model is generating a richer explanation in the background...");
        }

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
        return builder.ToString();
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
