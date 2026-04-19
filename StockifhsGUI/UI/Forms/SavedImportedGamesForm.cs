using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public sealed class SavedImportedGamesForm : MaterialForm
{
    private readonly IAnalysisStore analysisStore;
    private readonly TextBox filterTextBox;
    private readonly ListBox gamesListBox;
    private readonly MaterialMultiLineTextBox2 detailsTextBox;
    private readonly MaterialButton loadButton;
    private readonly MaterialButton deleteButton;

    public SavedImportedGamesForm(IAnalysisStore analysisStore)
    {
        this.analysisStore = analysisStore ?? throw new ArgumentNullException(nameof(analysisStore));

        Text = "Saved Imported Games";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 700);
        MinimumSize = new Size(900, 600);

        TableLayoutPanel rootLayout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 70, 16, 16),
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

        MaterialLabel filterLabel = new()
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
            Text = "Filter games:"
        };
        topBar.Controls.Add(filterLabel, 0, 0);

        filterTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 0)
        };
        filterTextBox.TextChanged += (_, _) => RefreshList();
        topBar.Controls.Add(filterTextBox, 1, 0);

        loadButton = new MaterialButton
        {
            Text = "Load Game",
            AutoSize = false,
            Size = new Size(130, 36),
            Enabled = false,
            Anchor = AnchorStyles.Right
        };
        loadButton.Click += (_, _) => LoadSelectedGame();
        topBar.Controls.Add(loadButton, 3, 0);

        deleteButton = new MaterialButton
        {
            Text = "Delete",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(100, 36),
            Enabled = false,
            Anchor = AnchorStyles.Right
        };
        deleteButton.Click += (_, _) => DeleteSelectedGame();
        topBar.Controls.Add(deleteButton, 4, 0);

        MaterialButton cancelButton = new()
        {
            Text = "Cancel",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Text,
            Size = new Size(100, 36),
            Anchor = AnchorStyles.Right
        };
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        topBar.Controls.Add(cancelButton, 5, 0);

        SplitContainer splitContainer = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            FixedPanel = FixedPanel.None,
            SplitterDistance = 480
        };
        splitContainer.BackColor = System.Drawing.Color.Transparent;
        rootLayout.Controls.Add(splitContainer, 0, 1);

        gamesListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        gamesListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        gamesListBox.DoubleClick += (_, _) => LoadSelectedGame();
        splitContainer.Panel1.BackColor = System.Drawing.Color.Transparent;
        splitContainer.Panel1.Controls.Add(gamesListBox);

        detailsTextBox = new MaterialMultiLineTextBox2
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 10)
        };
        splitContainer.Panel2.BackColor = System.Drawing.Color.Transparent;
        splitContainer.Panel2.Controls.Add(detailsTextBox);

        RefreshList();
    }

    public ImportedGame? SelectedGame { get; private set; }

    private void RefreshList()
    {
        gamesListBox.Items.Clear();
        detailsTextBox.Clear();
        loadButton.Enabled = false;
        deleteButton.Enabled = false;
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
            deleteButton.Enabled = false;
            return;
        }

        loadButton.Enabled = true;
        deleteButton.Enabled = true;
        SavedImportedGameSummary summary = item.Summary;
        detailsTextBox.Text =
            $"Players: {summary.WhitePlayer ?? "White"} vs {summary.BlackPlayer ?? "Black"}{Environment.NewLine}" +
            $"Date: {summary.DateText ?? "(unknown)"}{Environment.NewLine}" +
            $"Result: {summary.Result ?? "(unknown)"}{Environment.NewLine}" +
            $"Opening: {OpeningCatalog.Describe(summary.Eco)}{Environment.NewLine}" +
            $"Site: {summary.Site ?? "(unknown)"}{Environment.NewLine}" +
            $"Saved: {(summary.UpdatedUtc == default ? "(unknown)" : summary.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}{Environment.NewLine}{Environment.NewLine}" +
            "Double click or use 'Load Game' to bring this PGN back into the main board." + Environment.NewLine +
            "Use 'Delete Game' to remove the PGN together with saved analysis data for this game.";
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

    private void DeleteSelectedGame()
    {
        if (gamesListBox.SelectedItem is not SavedGameListItem item)
        {
            return;
        }

        SavedImportedGameSummary summary = item.Summary;
        string label = summary.DisplayTitle;
        DialogResult confirmation = MessageBox.Show(
            $"Delete this saved game from local storage?{Environment.NewLine}{Environment.NewLine}{label}{Environment.NewLine}{Environment.NewLine}Saved analysis results and analyzer window state for this game will also be removed.",
            "Delete Saved Game",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        bool deleted;
        try
        {
            deleted = analysisStore.DeleteImportedGame(summary.GameFingerprint);
            GameAnalysisCache.RemoveGame(summary.GameFingerprint);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the selected game from local storage.{Environment.NewLine}{ex.Message}", "Delete Saved Game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!deleted)
        {
            MessageBox.Show("The selected game no longer exists in local storage.", "Delete Saved Game", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        RefreshList();
    }

    private sealed record SavedGameListItem(SavedImportedGameSummary Summary, string Label)
    {
        public override string ToString() => Label;
    }
}
