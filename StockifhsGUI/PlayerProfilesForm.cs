using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StockifhsGUI;

public sealed class PlayerProfilesForm : Form
{
    private readonly PlayerProfileService profileService;
    private readonly TextBox filterTextBox;
    private readonly ListBox profilesListBox;
    private readonly TextBox detailsTextBox;

    public PlayerProfilesForm(PlayerProfileService profileService)
    {
        this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

        Text = "Player Profiles";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(980, 620);

        Label filterLabel = new()
        {
            AutoSize = true,
            Location = new Point(16, 18),
            Text = "Filter by analyzed player:"
        };
        Controls.Add(filterLabel);

        filterTextBox = new TextBox
        {
            Location = new Point(16, 42),
            Size = new Size(360, 28)
        };
        filterTextBox.TextChanged += (_, _) => RefreshList();
        Controls.Add(filterTextBox);

        Button closeButton = new()
        {
            Text = "Close",
            Location = new Point(392, 40),
            Size = new Size(120, 32)
        };
        closeButton.Click += (_, _) => DialogResult = DialogResult.OK;
        Controls.Add(closeButton);

        profilesListBox = new ListBox
        {
            Location = new Point(16, 88),
            Size = new Size(360, 512),
            Font = new Font("Consolas", 10)
        };
        profilesListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        Controls.Add(profilesListBox);

        detailsTextBox = new TextBox
        {
            Location = new Point(392, 88),
            Size = new Size(572, 512),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10)
        };
        Controls.Add(detailsTextBox);

        RefreshList();
    }

    private void RefreshList()
    {
        profilesListBox.Items.Clear();
        detailsTextBox.Clear();

        IReadOnlyList<PlayerProfileSummary> profiles = profileService.ListProfiles(filterTextBox.Text);
        foreach (PlayerProfileSummary profile in profiles)
        {
            string topLabels = profile.TopLabels.Count == 0
                ? "no tags"
                : string.Join(", ", profile.TopLabels);
            string averageCpl = profile.AverageCentipawnLoss?.ToString() ?? "n/a";
            string label = $"{profile.DisplayName,-18} games {profile.GamesAnalyzed,3} | CPL {averageCpl,4} | {topLabels}";
            profilesListBox.Items.Add(new ProfileListItem(profile, label));
        }

        if (profilesListBox.Items.Count > 0)
        {
            profilesListBox.SelectedIndex = 0;
        }
        else
        {
            detailsTextBox.Text = "No analyzed players match the current filter. Run and save a few game analyses first.";
        }
    }

    private void UpdateDetails()
    {
        if (profilesListBox.SelectedItem is not ProfileListItem item)
        {
            detailsTextBox.Clear();
            return;
        }

        if (!profileService.TryBuildProfile(item.Summary.PlayerKey, out PlayerProfileReport? report) || report is null)
        {
            detailsTextBox.Text = "Could not build the selected player profile from local analysis data.";
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine($"Player: {report.DisplayName}");
        builder.AppendLine($"Games analyzed: {report.GamesAnalyzed}");
        builder.AppendLine($"Analyzed moves: {report.TotalAnalyzedMoves}");
        builder.AppendLine($"Highlighted mistakes: {report.HighlightedMistakes}");
        builder.AppendLine($"Average CPL: {report.AverageCentipawnLoss?.ToString() ?? "n/a"}");

        builder.AppendLine();
        builder.AppendLine("Top mistake labels:");
        if (report.TopMistakeLabels.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (ProfileLabelStat itemStat in report.TopMistakeLabels)
            {
                builder.AppendLine($"- {itemStat.Label}: {itemStat.Count} | conf {itemStat.AverageConfidence:0.00}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Mistakes by phase:");
        if (report.MistakesByPhase.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (ProfilePhaseStat phaseStat in report.MistakesByPhase)
            {
                builder.AppendLine($"- {phaseStat.Phase}: {phaseStat.Count}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Mistakes by opening:");
        if (report.MistakesByOpening.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (ProfileOpeningStat openingStat in report.MistakesByOpening)
            {
                builder.AppendLine($"- {openingStat.Eco}: {openingStat.Count}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Games by side:");
        foreach (ProfileSideStat sideStat in report.GamesBySide)
        {
            builder.AppendLine($"- {sideStat.Side}: {sideStat.GamesAnalyzed} games | {sideStat.HighlightedMistakes} highlights");
        }

        builder.AppendLine();
        builder.AppendLine("Monthly trend:");
        if (report.MonthlyTrend.Count == 0)
        {
            builder.AppendLine("- unknown");
        }
        else
        {
            foreach (ProfileMonthlyTrend trend in report.MonthlyTrend)
            {
                builder.AppendLine($"- {trend.MonthKey}: {trend.GamesAnalyzed} games | highlights {trend.HighlightedMistakes} | avg CPL {trend.AverageCentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Training priorities:");
        if (report.Recommendations.Count == 0)
        {
            builder.AppendLine("- none yet");
        }
        else
        {
            foreach (TrainingRecommendation recommendation in report.Recommendations)
            {
                builder.AppendLine($"- {recommendation.Title}: {recommendation.Description}");
            }
        }

        detailsTextBox.Text = builder.ToString();
    }

    private sealed record ProfileListItem(PlayerProfileSummary Summary, string Label)
    {
        public override string ToString() => Label;
    }
}
