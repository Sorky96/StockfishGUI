using System.Drawing;
using System.Linq;
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
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(920, 620);

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
            ColumnCount = 4,
            AutoSize = true,
            Margin = Padding.Empty
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(topBar, 0, 0);

        Label filterLabel = new()
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
            Text = "Filter by analyzed player:"
        };
        topBar.Controls.Add(filterLabel, 0, 0);

        filterTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 0)
        };
        filterTextBox.TextChanged += (_, _) => RefreshList();
        topBar.Controls.Add(filterTextBox, 1, 0);

        Button closeButton = new()
        {
            Text = "Close",
            Size = new Size(120, 32),
            Anchor = AnchorStyles.Left
        };
        closeButton.Click += (_, _) => DialogResult = DialogResult.OK;
        topBar.Controls.Add(closeButton, 3, 0);

        SplitContainer splitContainer = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 340
        };
        rootLayout.Controls.Add(splitContainer, 0, 1);

        profilesListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };
        profilesListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
        splitContainer.Panel1.Padding = new Padding(0);
        splitContainer.Panel1.Controls.Add(profilesListBox);

        detailsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10)
        };
        splitContainer.Panel2.Padding = new Padding(0);
        splitContainer.Panel2.Controls.Add(detailsTextBox);

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
                builder.AppendLine($"- {OpeningCatalog.Describe(openingStat.Eco)}: {openingStat.Count}");
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
                builder.AppendLine($"{recommendation.Priority}. {recommendation.Title} ({recommendation.FocusArea})");
                builder.AppendLine($"   {recommendation.Description}");
                builder.AppendLine($"   Emphasis: {FormatRecommendationContext(recommendation)}");
                builder.AppendLine("   Checklist:");
                foreach (string checklistItem in recommendation.Checklist)
                {
                    builder.AppendLine($"   - {checklistItem}");
                }

                builder.AppendLine("   Suggested drills:");
                foreach (string drill in recommendation.SuggestedDrills)
                {
                    builder.AppendLine($"   - {drill}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("Weekly training plan:");
        builder.AppendLine(report.WeeklyPlan.Title);
        builder.AppendLine(report.WeeklyPlan.Summary);
        builder.AppendLine();
        foreach (WeeklyTrainingDay day in report.WeeklyPlan.Days)
        {
            builder.AppendLine($"Day {day.DayNumber}: {day.Theme} | {day.PrimaryFocus} | {day.EstimatedMinutes} min");
            foreach (string activity in day.Activities)
            {
                builder.AppendLine($"- {activity}");
            }

            builder.AppendLine($"Success check: {day.SuccessCheck}");
            builder.AppendLine();
        }

        detailsTextBox.Text = builder.ToString().TrimEnd();
        detailsTextBox.SelectionStart = 0;
        detailsTextBox.SelectionLength = 0;
    }

    private sealed record ProfileListItem(PlayerProfileSummary Summary, string Label)
    {
        public override string ToString() => Label;
    }

    private static string FormatRecommendationContext(TrainingRecommendation recommendation)
    {
        List<string> parts = [];

        if (recommendation.EmphasisPhase.HasValue)
        {
            parts.Add(recommendation.EmphasisPhase.Value.ToString());
        }

        if (recommendation.EmphasisSide.HasValue)
        {
            parts.Add(recommendation.EmphasisSide.Value.ToString());
        }

        if (recommendation.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(", ", recommendation.RelatedOpenings.Select(OpeningCatalog.Describe)));
        }

        return parts.Count == 0
            ? "general"
            : string.Join(" | ", parts);
    }
}
