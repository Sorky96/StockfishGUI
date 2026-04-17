using System.Drawing;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class SavedImportedGamesForm : Form
{
    private readonly IAnalysisStore analysisStore;
    private readonly TextBox filterTextBox;
    private readonly ListBox gamesListBox;
    private readonly TextBox detailsTextBox;
    private readonly Button loadButton;

    public SavedImportedGamesForm(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));

        Text = "Saved Imported Games";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        MinimumSize = new Size(900, 560);

        Label filterLabel = new()
        {
            AutoSize = true,
            Location = new Point(16, 18),
            Text = "Filter by player, date, ECO, result or site:"
        };
        Controls.Add(filterLabel);

        filterTextBox = new TextBox
        {
            Location = new Point(16, 42),
            Size = new Size(360, 28)
        };
        filterTextBox.TextChanged += (_, _) => RefreshList();
        Controls.Add(filterTextBox);

        loadButton = new Button
        {
            Text = "Load Game",
            Location = new Point(392, 40),
            Size = new Size(120, 32),
            Enabled = false
        };
        loadButton.Click += (_, _) => LoadSelectedGame();
        Controls.Add(loadButton);

        Button cancelButton = new()
        {
            Text = "Cancel",
            Location = new Point(528, 40),
            Size = new Size(120, 32)
        };
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        gamesListBox = new ListBox
        {
            Location = new Point(16, 88),
            Size = new Size(400, 448),
            Font = new Font("Consolas", 10)
        };
        gamesListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        gamesListBox.DoubleClick += (_, _) => LoadSelectedGame();
        Controls.Add(gamesListBox);

        detailsTextBox = new TextBox
        {
            Location = new Point(432, 88),
            Size = new Size(452, 448),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10)
        };
        Controls.Add(detailsTextBox);

        RefreshList();
    }

    public ImportedGame? SelectedGame { get; private set; }

    private void RefreshList()
    {
        gamesListBox.Items.Clear();
        detailsTextBox.Clear();
        loadButton.Enabled = false;
        SelectedGame = null;

        IReadOnlyList<SavedImportedGameSummary> items = analysisStore.ListImportedGames(filterTextBox.Text);
        foreach (SavedImportedGameSummary item in items)
        {
            gamesListBox.Items.Add(new SavedGameListItem(item, item.DisplayTitle));
        }

        if (gamesListBox.Items.Count > 0)
        {
            gamesListBox.SelectedIndex = 0;
        }
        else
        {
            detailsTextBox.Text = "No saved imported games match the current filter.";
        }
    }

    private void UpdateDetails()
    {
        if (gamesListBox.SelectedItem is not SavedGameListItem item)
        {
            detailsTextBox.Clear();
            loadButton.Enabled = false;
            return;
        }

        loadButton.Enabled = true;
        SavedImportedGameSummary summary = item.Summary;
        detailsTextBox.Text =
            $"Players: {summary.WhitePlayer ?? "White"} vs {summary.BlackPlayer ?? "Black"}{Environment.NewLine}" +
            $"Date: {summary.DateText ?? "(unknown)"}{Environment.NewLine}" +
            $"Result: {summary.Result ?? "(unknown)"}{Environment.NewLine}" +
            $"ECO: {summary.Eco ?? "(unknown)"}{Environment.NewLine}" +
            $"Site: {summary.Site ?? "(unknown)"}{Environment.NewLine}" +
            $"Saved: {(summary.UpdatedUtc == default ? "(unknown)" : summary.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}{Environment.NewLine}{Environment.NewLine}" +
            "Double click or use 'Load Game' to bring this PGN back into the main board.";
    }

    private void LoadSelectedGame()
    {
        if (gamesListBox.SelectedItem is not SavedGameListItem item)
        {
            return;
        }

        if (!analysisStore.TryLoadImportedGame(item.Summary.GameFingerprint, out ImportedGame? importedGame) || importedGame is null)
        {
            MessageBox.Show("Could not load the selected game from local storage.", "Saved Imported Games", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedGame = importedGame;
        DialogResult = DialogResult.OK;
    }

    private sealed record SavedGameListItem(SavedImportedGameSummary Summary, string Label)
    {
        public override string ToString() => Label;
    }
}
