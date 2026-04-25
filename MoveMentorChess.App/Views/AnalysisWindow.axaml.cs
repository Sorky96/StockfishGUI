using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MoveMentorChess.App.Views;

public partial class AnalysisWindow : Window
{
    private static readonly EngineAnalysisOptions DefaultAnalysisOptions = new();

    private readonly ImportedGame? importedGame;
    private readonly IEngineAnalyzer? engineAnalyzer;
    private readonly Func<MoveAnalysisResult, Task>? navigateToMoveAsync;
    private readonly Action<GameAnalysisProgress>? analysisProgress;
    private readonly PlayerSide initialSide;
    private readonly Dictionary<string, MoveExplanation> explanationCache = [];
    private IAdviceGenerator adviceGenerator = CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
    private GameAnalysisResult? currentResult;
    private bool currentResultIsCached;
    private int explanationRequestId;

    public AnalysisWindow()
    {
        InitializeComponent();
    }

    public AnalysisWindow(
        ImportedGame importedGame,
        IEngineAnalyzer engineAnalyzer,
        Func<MoveAnalysisResult, Task> navigateToMoveAsync,
        Action<GameAnalysisProgress>? analysisProgress,
        PlayerSide initialSide)
    {
        this.importedGame = importedGame;
        this.engineAnalyzer = engineAnalyzer;
        this.navigateToMoveAsync = navigateToMoveAsync;
        this.analysisProgress = analysisProgress;
        this.initialSide = initialSide;

        InitializeComponent();
        SideComboBox.ItemsSource = new[]
        {
            new SideOption(PlayerSide.White, "Analyze White"),
            new SideOption(PlayerSide.Black, "Analyze Black")
        };
        QualityFilterComboBox.ItemsSource = new[]
        {
            new FilterOption("All highlights", null),
            new FilterOption("Blunders only", MoveQualityBucket.Blunder),
            new FilterOption("Mistakes only", MoveQualityBucket.Mistake),
            new FilterOption("Inaccuracies only", MoveQualityBucket.Inaccuracy)
        };
        SideComboBox.SelectedIndex = initialSide == PlayerSide.Black ? 1 : 0;
        QualityFilterComboBox.SelectedIndex = 0;
        SideComboBox.SelectionChanged += (_, _) => TryLoadCachedResultForSelectedSide();
        QualityFilterComboBox.SelectionChanged += (_, _) => ApplyFilter();
        ShowOnBoardButton.IsEnabled = false;
        DetailsTextBlock.Text = "Run analysis to inspect highlighted mistakes.";
        RefreshAdviceRuntimeState();

        if (GameAnalysisCache.TryGetWindowState(importedGame, out AnalysisWindowState? state) && state is not null)
        {
            SideComboBox.SelectedIndex = state.SelectedSide == PlayerSide.Black ? 1 : 0;
            QualityFilterComboBox.SelectedIndex = Math.Clamp(state.QualityFilterIndex, 0, QualityFilterComboBox.ItemCount - 1);
        }

        TryLoadCachedResultForSelectedSide();
    }

    private async void TestAdviceButton_Click(object? sender, RoutedEventArgs e)
    {
        RefreshAdviceRuntimeState();
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        if (!status.IsReady)
        {
            StatusTextBlock.Text = status.InstallHint is null
                ? status.StatusText
                : $"{status.StatusText} {status.InstallHint}";
            return;
        }

        TestAdviceButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing local advice runtime...";

        try
        {
            AdviceRuntimeSmokeTestResult result = await Task.Run(AdviceRuntimeSmokeTester.Run);
            StatusTextBlock.Text = result.Message;
        }
        finally
        {
            RefreshAdviceRuntimeState();
            TestAdviceButton.IsEnabled = true;
        }
    }

    private async void AnalyzeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SideComboBox.SelectedItem is not SideOption selectedSide)
        {
            return;
        }

        if (importedGame is null || engineAnalyzer is null)
        {
            StatusTextBlock.Text = "Analysis window is missing required game context.";
            return;
        }

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded cached analysis for {selectedSide.Side}.";
            return;
        }

        AnalyzeButton.IsEnabled = false;
        TestAdviceButton.IsEnabled = false;
        SideComboBox.IsEnabled = false;
        QualityFilterComboBox.IsEnabled = false;
        StatusTextBlock.Text = $"Analyzing imported game for {selectedSide.Side}...";
        SummaryTextBlock.Text = string.Empty;
        MistakesListBox.ItemsSource = null;
        DetailsTextBlock.Text = "The analysis engine is reviewing the imported game. This may take a moment.";
        ShowOnBoardButton.IsEnabled = false;

        try
        {
            GameAnalysisService analysisService = new(
                engineAnalyzer,
                adviceGenerator: CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateBulkAnalysisGenerator()));
            IProgress<GameAnalysisProgress>? progress = analysisProgress is null
                ? null
                : new Progress<GameAnalysisProgress>(analysisProgress);
            currentResult = await Task.Run(() => analysisService.AnalyzeGame(importedGame, selectedSide.Side, DefaultAnalysisOptions, progress));
            GameAnalysisCache.StoreResult(cacheKey, currentResult);
            currentResultIsCached = false;
            ApplyFilter();
            StatusTextBlock.Text = $"Analysis finished for {selectedSide.Side}.";
        }
        catch (Exception ex)
        {
            currentResult = null;
            currentResultIsCached = false;
            SummaryTextBlock.Text = string.Empty;
            DetailsTextBlock.Text = "Analysis failed.";
            StatusTextBlock.Text = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
            TestAdviceButton.IsEnabled = true;
            SideComboBox.IsEnabled = true;
            QualityFilterComboBox.IsEnabled = true;
            RefreshAdviceRuntimeState();
        }
    }

    private void MistakesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item)
        {
            DetailsTextBlock.Text = "Select a highlighted mistake to inspect details.";
            ShowOnBoardButton.IsEnabled = false;
            return;
        }

        LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
        ExplanationLevel level = settings.DefaultExplanationLevel;
        AdviceNarrationStyle narrationStyle = settings.NarrationStyle;
        string cacheKey = BuildExplanationCacheKey(item.LeadMove, level, narrationStyle);
        MoveExplanation explanation = item.LeadMove.Explanation
            ?? new MoveExplanation("Explanation is loading...", "Training hint is loading...");

        bool isCached = explanationCache.TryGetValue(cacheKey, out MoveExplanation? cachedExplanation);
        if (isCached && cachedExplanation is not null)
        {
            explanation = cachedExplanation;
        }

        DetailsTextBlock.Text = BuildDetailsText(item.Mistake, item.LeadMove, currentResult?.OpeningReview, explanation, !isCached);
        ShowOnBoardButton.IsEnabled = true;

        if (!isCached)
        {
            int requestId = ++explanationRequestId;
            _ = LoadExplanationAsync(item, item.LeadMove, level, cacheKey, requestId);
        }
    }

    private async void MistakesListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        await ShowSelectedMistakeAsync();
    }

    private async void ShowOnBoardButton_Click(object? sender, RoutedEventArgs e)
    {
        await ShowSelectedMistakeAsync();
    }

    private void ApplyFilter()
    {
        if (currentResult is null)
        {
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            MistakesListBox.ItemsSource = null;
            DetailsTextBlock.Text = "Run analysis to inspect highlighted mistakes.";
            ShowOnBoardButton.IsEnabled = false;
            return;
        }

        IEnumerable<SelectedMistake> visibleMistakes = currentResult.HighlightedMistakes;
        if (QualityFilterComboBox.SelectedItem is FilterOption filter && filter.QualityFilter is not null)
        {
            visibleMistakes = visibleMistakes.Where(mistake => mistake.Quality == filter.QualityFilter.Value);
        }

        List<SelectedMistakeViewItem> items = visibleMistakes
            .Select(mistake => new SelectedMistakeViewItem(mistake))
            .ToList();
        MistakesListBox.ItemsSource = items;

        int blunders = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Blunder);
        int mistakes = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = currentResult.HighlightedMistakes.Count(item => item.Quality == MoveQualityBucket.Inaccuracy);
        string cacheSuffix = currentResultIsCached ? " Loaded from cache." : string.Empty;
        SummaryTextBlock.Text = $"Showing {items.Count} items for {currentResult.AnalyzedSide}. Highlights: {blunders} blunders, {mistakes} mistakes, {inaccuracies} inaccuracies.{cacheSuffix}";

        if (items.Count > 0)
        {
            MistakesListBox.SelectedIndex = 0;
        }
        else
        {
            DetailsTextBlock.Text = "No items match the current filter.";
            ShowOnBoardButton.IsEnabled = false;
        }
    }

    private async Task ShowSelectedMistakeAsync()
    {
        if (MistakesListBox.SelectedItem is not SelectedMistakeViewItem item || navigateToMoveAsync is null)
        {
            return;
        }

        await navigateToMoveAsync(item.LeadMove);
        Close();
    }

    private async Task LoadExplanationAsync(
        SelectedMistakeViewItem item,
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        string cacheKey,
        int requestId)
    {
        if (importedGame is null)
        {
            return;
        }

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
                    "avalonia-analysis-window",
                    GameFingerprint.Compute(importedGame.PgnText),
                    currentResult?.AnalyzedSide,
                    NarrationStyle: LlamaGpuSettingsStore.Load().NarrationStyle)));
        }
        catch (Exception ex)
        {
            explanation = new MoveExplanation(
                "Local advice generation failed.",
                "Use the engine lines and training hint for now.",
                ex.Message);
        }

        if (requestId != explanationRequestId)
        {
            return;
        }

        explanationCache[cacheKey] = explanation;

        if (!ReferenceEquals(MistakesListBox.SelectedItem, item))
        {
            return;
        }

        DetailsTextBlock.Text = BuildDetailsText(item.Mistake, lead, currentResult?.OpeningReview, explanation, false);
    }

    private static string BuildDetailsText(
        SelectedMistake mistake,
        MoveAnalysisResult lead,
        OpeningPhaseReview? openingReview,
        MoveExplanation explanation,
        bool isLoading)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Moves: {BuildMoveRange(mistake)}");
        builder.AppendLine($"Quality: {mistake.Quality}");
        builder.AppendLine($"Label: {FormatMistakeLabel(mistake.Tag?.Label ?? "unclassified")}");
        builder.AppendLine($"Confidence: {(mistake.Tag?.Confidence ?? 0):0.00}");
        builder.AppendLine($"Phase: {lead.Replay.Phase}");
        builder.AppendLine($"Played move: {FormatSanAndUci(lead.Replay.San, lead.Replay.Uci)}");
        builder.AppendLine($"Best move: {FormatMoveFromFen(lead.Replay.FenBefore, lead.BeforeAnalysis.BestMoveUci)}");
        builder.AppendLine($"Eval before: {FormatScore(lead.EvalBeforeCp, lead.BestMateIn)}");
        builder.AppendLine($"Eval after: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
        builder.AppendLine($"Centipawn loss: {(lead.CentipawnLoss?.ToString() ?? "n/a")}");
        builder.AppendLine($"Material delta: {lead.MaterialDeltaCp}");

        if (openingReview is not null && lead.Replay.Phase == GamePhase.Opening)
        {
            builder.AppendLine();
            builder.AppendLine("Opening review:");
            builder.AppendLine($"Branch: {openingReview.Branch.BranchLabel}");

            if (openingReview.TheoryExit?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is marked as the theory exit: {openingReview.TheoryExit.Trigger}");
            }

            if (openingReview.FirstSignificantMistake?.Ply == lead.Replay.Ply)
            {
                builder.AppendLine($"This move is the first significant opening mistake: {openingReview.FirstSignificantMistake.Trigger}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Advice:");
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
            builder.AppendLine($"Score: {FormatScore(lead.EvalAfterCp, lead.PlayedMateIn)}");
            builder.AppendLine(FormatPrincipalVariation(lead.Replay.FenAfter, playedContinuation.Pv, maxHalfMoves: 8));
        }

        builder.AppendLine();
        builder.AppendLine("Board navigation:");
        builder.AppendLine("Use 'Show On Board' to jump to this position in the main window.");

        return builder.ToString().TrimEnd();
    }

    private void RefreshAdviceRuntimeState()
    {
        AdviceRuntimeStatus status = AdviceRuntimeCatalog.GetStatus();
        adviceGenerator = CreateSettingsBackedAdviceGenerator(AdviceGeneratorFactory.CreateInteractiveGenerator());
        AdviceStatusTextBlock.Text = status.StatusText;
    }

    private static IAdviceGenerator CreateSettingsBackedAdviceGenerator(IAdviceGenerator inner)
        => new SettingsBackedAdviceGenerator(inner);

    private bool TryLoadCachedResultForSelectedSide()
    {
        MistakesListBox.ItemsSource = null;
        DetailsTextBlock.Text = "Run analysis to inspect highlighted mistakes.";
        ShowOnBoardButton.IsEnabled = false;
        explanationRequestId++;

        if (importedGame is null || SideComboBox.SelectedItem is not SideOption selectedSide)
        {
            currentResult = null;
            currentResultIsCached = false;
            SummaryTextBlock.Text = "Choose a side and run the analysis.";
            return false;
        }

        GameAnalysisCacheKey cacheKey = GameAnalysisCache.CreateKey(importedGame, selectedSide.Side, DefaultAnalysisOptions);
        if (GameAnalysisCache.TryGetResult(cacheKey, out GameAnalysisResult? cachedResult) && cachedResult is not null)
        {
            currentResult = cachedResult;
            currentResultIsCached = true;
            ApplyFilter();
            StatusTextBlock.Text = $"Loaded cached analysis for {selectedSide.Side}.";
            return true;
        }

        currentResult = null;
        currentResultIsCached = false;
        SummaryTextBlock.Text = $"No cached analysis for {selectedSide.Side}. Run analysis to generate it.";
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (importedGame is not null && SideComboBox.SelectedItem is SideOption selectedSide)
        {
            GameAnalysisCache.StoreWindowState(
                importedGame,
                new AnalysisWindowState(
                    selectedSide.Side,
                    QualityFilterComboBox.SelectedIndex,
                    1));
        }

        base.OnClosed(e);
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
        => mateIn is int mate ? $"mate {mate}" : centipawns is int cp ? $"{cp} cp" : "n/a";

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
        => string.Equals(san, uci, StringComparison.OrdinalIgnoreCase) ? san : $"{san} ({uci})";

    private static string FormatEngineCandidates(string fenBefore, IReadOnlyList<EngineLine> lines)
    {
        StringBuilder builder = new();

        for (int i = 0; i < lines.Count; i++)
        {
            EngineLine line = lines[i];
            string moveLabel = FormatMoveFromFen(fenBefore, line.MoveUci);
            string pv = FormatPrincipalVariation(fenBefore, line.Pv, maxHalfMoves: 8);
            builder.AppendLine($"{i + 1}. {moveLabel} | {FormatScore(line.Centipawns, line.MateIn)}");
            if (!string.IsNullOrWhiteSpace(pv))
            {
                builder.AppendLine($"   PV: {pv}");
            }
        }

        return builder.ToString().TrimEnd();
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

    private static string FormatMistakeLabel(string label)
    {
        return label switch
        {
            "hanging_piece" => "Loose pieces",
            "missed_tactic" => "Missed tactics",
            "opening_principles" => "Opening discipline",
            "king_safety" => "King safety",
            "endgame_technique" => "Endgame technique",
            "material_loss" => "Material losses",
            "piece_activity" => "Passive pieces",
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.Replace('_', ' ').ToLowerInvariant())
        };
    }

    private sealed record SideOption(PlayerSide Side, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record FilterOption(string Label, MoveQualityBucket? QualityFilter)
    {
        public override string ToString() => Label;
    }

    private sealed class SelectedMistakeViewItem
    {
        public SelectedMistakeViewItem(SelectedMistake mistake)
        {
            Mistake = mistake;
            LeadMove = mistake.Moves
                .OrderByDescending(move => move.Quality)
                .ThenByDescending(move => move.CentipawnLoss ?? 0)
                .First();
        }

        public SelectedMistake Mistake { get; }

        public MoveAnalysisResult LeadMove { get; }

        public override string ToString()
        {
            string moveRange = BuildMoveRange(Mistake);
            string label = FormatMistakeLabel(Mistake.Tag?.Label ?? "unclassified");
            string cpl = LeadMove.CentipawnLoss?.ToString() ?? "n/a";
            return $"{moveRange} | {Mistake.Quality} | {label} | CPL {cpl}";
        }
    }

    private static string BuildExplanationCacheKey(
        MoveAnalysisResult lead,
        ExplanationLevel explanationLevel,
        AdviceNarrationStyle narrationStyle)
        => $"{lead.Replay.Ply}:{lead.Replay.Uci}:{explanationLevel}:{narrationStyle}";

    private sealed class SettingsBackedAdviceGenerator(IAdviceGenerator inner) : IAdviceGenerator
    {
        public MoveExplanation Generate(
            ReplayPly replay,
            MoveQualityBucket quality,
            MistakeTag? tag,
            string? bestMoveUci,
            int? centipawnLoss,
            ExplanationLevel level = ExplanationLevel.Intermediate,
            AdviceGenerationContext? context = null)
        {
            LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
            AdviceGenerationContext enrichedContext = context is null
                ? new AdviceGenerationContext("avalonia-analysis-window", null, NarrationStyle: settings.NarrationStyle)
                : context with { NarrationStyle = settings.NarrationStyle };

            MoveExplanation explanation = inner.Generate(
                replay,
                quality,
                tag,
                bestMoveUci,
                centipawnLoss,
                settings.DefaultExplanationLevel,
                enrichedContext);

            return ApplyNarrationStyle(explanation, settings.NarrationStyle);
        }

        private static MoveExplanation ApplyNarrationStyle(MoveExplanation explanation, AdviceNarrationStyle style)
        {
            return style switch
            {
                AdviceNarrationStyle.LevyRozman => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Here is the practical idea: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Coach recap: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Levy-style drill: ")
                },
                AdviceNarrationStyle.HikaruNakamura => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Candidate-move check: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Calculation note: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Speed drill: ")
                },
                AdviceNarrationStyle.BotezLive => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Okay, tiny chess crisis, very fixable: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Stream recap: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Next-game challenge: ")
                },
                AdviceNarrationStyle.WittyAlien => explanation with
                {
                    ShortText = AddPrefix(explanation.ShortText, "Alien coach says the pony wandered into danger: "),
                    DetailedText = AddPrefix(explanation.DetailedText, "Free-candy scanner report: "),
                    TrainingHint = AddPrefix(explanation.TrainingHint, "Do not grab free candy rule: ")
                },
                _ => explanation
            };
        }

        private static string AddPrefix(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(text)
                || text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            return $"{prefix}{text}";
        }
    }
}
