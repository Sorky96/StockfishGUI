using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class SavedAnalysesForm : Form
{
    private readonly IAnalysisStore analysisStore;
    private readonly bool canOpenAnalysis;
    private readonly TextBox filterTextBox;
    private readonly ListBox analysesListBox;
    private readonly TextBox detailsTextBox;
    private readonly Button openAnalysisButton;
    private readonly Button loadGameButton;

    public SavedAnalysesForm(IAnalysisStore analysisStore, bool canOpenAnalysis)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));
        this.canOpenAnalysis = canOpenAnalysis;
        UiTheme.ApplyFormChrome(this);

        Text = "Saved Analyses";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 680);
        MinimumSize = new Size(900, 580);

        TableLayoutPanel rootLayout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 2
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(rootLayout);

        TableLayoutPanel topBar = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 6,
            AutoSize = true,
            Margin = Padding.Empty
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(topBar, 0, 0);

        Label filterLabel = new()
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
            Text = "Filter by player, opening, result or labels:"
        };
        UiTheme.StyleSectionLabel(filterLabel);
        topBar.Controls.Add(filterLabel, 0, 0);

        filterTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 0)
        };
        UiTheme.StyleTextBox(filterTextBox);
        filterTextBox.TextChanged += (_, _) => RefreshList();
        topBar.Controls.Add(filterTextBox, 1, 0);

        loadGameButton = new Button
        {
            Text = "Load Game",
            Size = new Size(120, 32),
            Enabled = false,
            Anchor = AnchorStyles.Right
        };
        UiTheme.StyleSecondaryButton(loadGameButton);
        loadGameButton.Click += (_, _) => ConfirmSelection(SavedAnalysisAction.LoadGame);
        topBar.Controls.Add(loadGameButton, 3, 0);

        openAnalysisButton = new Button
        {
            Text = "Open Analysis",
            Size = new Size(120, 32),
            Enabled = false,
            Anchor = AnchorStyles.Right
        };
        UiTheme.StylePrimaryButton(openAnalysisButton);
        openAnalysisButton.Click += (_, _) => ConfirmSelection(SavedAnalysisAction.OpenAnalysis);
        topBar.Controls.Add(openAnalysisButton, 4, 0);

        Button closeButton = new()
        {
            Text = "Close",
            Size = new Size(120, 32),
            Anchor = AnchorStyles.Right
        };
        UiTheme.StyleSecondaryButton(closeButton);
        closeButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        topBar.Controls.Add(closeButton, 5, 0);

        SplitContainer splitContainer = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            FixedPanel = FixedPanel.None,
            SplitterDistance = 460
        };
        splitContainer.BackColor = UiTheme.BorderColor;
        rootLayout.Controls.Add(splitContainer, 0, 1);

        analysesListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        UiTheme.StyleListBox(analysesListBox);
        analysesListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        analysesListBox.DoubleClick += (_, _) => ConfirmSelection(canOpenAnalysis ? SavedAnalysisAction.OpenAnalysis : SavedAnalysisAction.LoadGame);
        splitContainer.Panel1.BackColor = UiTheme.CardBackground;
        splitContainer.Panel1.Controls.Add(analysesListBox);

        detailsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10)
        };
        UiTheme.StyleTextBox(detailsTextBox);
        splitContainer.Panel2.BackColor = UiTheme.CardBackground;
        splitContainer.Panel2.Controls.Add(detailsTextBox);

        RefreshList();
    }

    public GameAnalysisResult? SelectedResult { get; private set; }
    public SavedAnalysisAction RequestedAction { get; private set; }

    private void RefreshList()
    {
        analysesListBox.Items.Clear();
        detailsTextBox.Clear();
        loadGameButton.Enabled = false;
        openAnalysisButton.Enabled = false;
        SelectedResult = null;
        RequestedAction = SavedAnalysisAction.None;

        IReadOnlyList<GameAnalysisResult> items = analysisStore.ListResults(filterTextBox.Text, limit: 1000);
        string normalizedFilter = filterTextBox.Text.Trim();
        IEnumerable<GameAnalysisResult> filtered = string.IsNullOrWhiteSpace(normalizedFilter)
            ? items
            : items.Where(result => MatchesExtendedFilter(result, normalizedFilter));

        foreach (GameAnalysisResult result in filtered)
        {
            analysesListBox.Items.Add(new SavedAnalysisListItem(result, BuildListLabel(result)));
        }

        if (analysesListBox.Items.Count > 0)
        {
            analysesListBox.SelectedIndex = 0;
        }
        else
        {
            detailsTextBox.Text = "No saved analyses match the current filter.";
        }
    }

    private void UpdateDetails()
    {
        if (analysesListBox.SelectedItem is not SavedAnalysisListItem item)
        {
            detailsTextBox.Clear();
            loadGameButton.Enabled = false;
            openAnalysisButton.Enabled = false;
            return;
        }

        loadGameButton.Enabled = true;
        openAnalysisButton.Enabled = canOpenAnalysis;

        GameAnalysisResult result = item.Result;
        int blunders = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Blunder);
        int mistakes = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Mistake);
        int inaccuracies = result.HighlightedMistakes.Count(mistake => mistake.Quality == MoveQualityBucket.Inaccuracy);
        IReadOnlyList<string> topLabels = result.HighlightedMistakes
            .Select(mistake => mistake.Tag?.Label ?? "unclassified")
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => $"{group.First()} ({group.Count()})")
            .ToList();

        StringBuilder builder = new();
        builder.AppendLine($"Players: {result.Game.WhitePlayer ?? "White"} vs {result.Game.BlackPlayer ?? "Black"}");
        builder.AppendLine($"Analyzed side: {result.AnalyzedSide}");
        builder.AppendLine($"Date: {result.Game.DateText ?? "(unknown)"}");
        builder.AppendLine($"Result: {result.Game.Result ?? "(unknown)"}");
        builder.AppendLine($"Opening: {OpeningCatalog.Describe(result.Game.Eco)}");
        builder.AppendLine($"Site: {result.Game.Site ?? "(unknown)"}");
        builder.AppendLine($"Replay plies: {result.Replay.Count}");
        builder.AppendLine($"Analyzed moves: {result.MoveAnalyses.Count}");
        builder.AppendLine($"Highlights: {result.HighlightedMistakes.Count}");
        builder.AppendLine($"Blunders / mistakes / inaccuracies: {blunders} / {mistakes} / {inaccuracies}");
        builder.AppendLine();
        builder.AppendLine("Top labels:");
        if (topLabels.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (string label in topLabels)
            {
                builder.AppendLine($"- {label}");
            }
        }

        if (result.HighlightedMistakes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Top highlighted mistakes:");
            foreach (SelectedMistake mistake in result.HighlightedMistakes.Take(5))
            {
                MoveAnalysisResult? lead = mistake.Moves
                    .OrderByDescending(move => move.Quality)
                    .ThenByDescending(move => move.CentipawnLoss ?? 0)
                    .FirstOrDefault();

                if (lead is null)
                {
                    continue;
                }

                string moveLabel = $"{lead.Replay.MoveNumber}{(lead.Replay.Side == PlayerSide.White ? "." : "...")} {lead.Replay.San}";
                builder.AppendLine($"- {moveLabel} | {mistake.Quality} | {mistake.Tag?.Label ?? "unclassified"} | CPL {lead.CentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(canOpenAnalysis
            ? "Use 'Open Analysis' to reopen the full analyzer on cached data, or 'Load Game' to bring the PGN back to the main board."
            : "Use 'Load Game' to bring the PGN back to the main board. Open Analysis is unavailable because Stockfish is not loaded.");

        detailsTextBox.Text = builder.ToString().TrimEnd();
        detailsTextBox.SelectionStart = 0;
        detailsTextBox.SelectionLength = 0;
    }

    private void ConfirmSelection(SavedAnalysisAction action)
    {
        if (analysesListBox.SelectedItem is not SavedAnalysisListItem item)
        {
            return;
        }

        if (action == SavedAnalysisAction.OpenAnalysis && !canOpenAnalysis)
        {
            return;
        }

        SelectedResult = item.Result;
        RequestedAction = action;
        DialogResult = DialogResult.OK;
    }

    private static bool MatchesExtendedFilter(GameAnalysisResult result, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        string normalized = filterText.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return true;
        }

        bool metadataMatch =
            (result.Game.WhitePlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.BlackPlayer?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.DateText?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.Result?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (result.Game.Eco?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || OpeningCatalog.Describe(result.Game.Eco).Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || (result.Game.Site?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || result.AnalyzedSide.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase);

        if (metadataMatch)
        {
            return true;
        }

        return result.HighlightedMistakes.Any(mistake =>
            (mistake.Tag?.Label?.Contains(filterText, StringComparison.OrdinalIgnoreCase) ?? false)
            || mistake.Quality.ToString().Contains(filterText, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildListLabel(GameAnalysisResult result)
    {
        string players = $"{result.Game.WhitePlayer ?? "White"} vs {result.Game.BlackPlayer ?? "Black"}";
        string date = string.IsNullOrWhiteSpace(result.Game.DateText) ? "date ?" : result.Game.DateText!;
        string opening = OpeningCatalog.GetName(result.Game.Eco);
        string side = result.AnalyzedSide == PlayerSide.White ? "White" : "Black";
        return $"{players,-28} | {side,-5} | {date,-10} | {opening,-22} | {result.HighlightedMistakes.Count,2} highlights";
    }

    private sealed record SavedAnalysisListItem(GameAnalysisResult Result, string Label)
    {
        public override string ToString() => Label;
    }
}

public enum SavedAnalysisAction
{
    None,
    LoadGame,
    OpenAnalysis
}
