using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public sealed class PlayerProfilesForm : MaterialForm
{
    private readonly PlayerProfileService profileService;
    private readonly TextBox filterTextBox;
    private readonly ListBox profilesListBox;
    private readonly DoubleBufferedTableLayoutPanel detailsContainer;
    private readonly DoubleBufferedPanel detailsPanel;
    private readonly DoubleBufferedTableLayoutPanel examplesContainer;
    private readonly DoubleBufferedPanel examplesPanel;
    private readonly Action<ProfileMistakeExample>? navigateToExample;
    private readonly IReadOnlyDictionary<string, Image> pieceImages;
    private readonly Color surfaceColor;
    private int detailsRequestVersion;

    public PlayerProfilesForm(PlayerProfileService profileService, IReadOnlyDictionary<string, Image> pieceImages, Action<ProfileMistakeExample>? navigateToExample = null)
    {
        this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        this.pieceImages = pieceImages ?? throw new ArgumentNullException(nameof(pieceImages));
        this.navigateToExample = navigateToExample;
        DoubleBuffered = true;
        ResizeRedraw = true;

        MaterialSkinManager materialSkinManager = MaterialSkinManager.Instance;
        materialSkinManager.AddFormToManage(this);
        surfaceColor = BackColor;

        Text = "Player Profiles";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(920, 620);

        DoubleBufferedTableLayoutPanel rootLayout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = surfaceColor
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Controls.Add(rootLayout);

        DoubleBufferedTableLayoutPanel topBar = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            Margin = Padding.Empty,
            BackColor = surfaceColor
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(topBar, 0, 0);

        MaterialLabel filterLabel = new()
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

        MaterialButton closeButton = new()
        {
            Text = "Close",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36),
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
        splitContainer.BackColor = surfaceColor;
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
        splitContainer.Panel1.BackColor = surfaceColor;
        splitContainer.Panel1.Controls.Add(profilesListBox);

        detailsPanel = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, 16, 0),
            BackColor = surfaceColor
        };
        detailsContainer = new DoubleBufferedTableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 0,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            BackColor = surfaceColor
        };
        detailsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        detailsPanel.Controls.Add(detailsContainer);

        examplesPanel = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = surfaceColor
        };
        examplesContainer = new DoubleBufferedTableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 0,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            BackColor = surfaceColor
        };
        examplesContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        examplesPanel.Controls.Add(examplesContainer);

        SplitContainer innerSplitContainer = new()
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 650,
            Orientation = Orientation.Vertical,
            BackColor = surfaceColor
        };
        innerSplitContainer.Panel1.BackColor = surfaceColor;
        innerSplitContainer.Panel1.Controls.Add(detailsPanel);
        innerSplitContainer.Panel2.BackColor = surfaceColor;
        innerSplitContainer.Panel2.Controls.Add(examplesPanel);

        splitContainer.Panel2.Padding = new Padding(0);
        splitContainer.Panel2.Controls.Add(innerSplitContainer);
        splitContainer.Panel2.BackColor = surfaceColor;

        detailsPanel.Resize += (_, _) => UpdateScrollableContentWidths();
        examplesPanel.Resize += (_, _) => UpdateScrollableContentWidths();
        RefreshList();
    }

    private void RefreshList()
    {
        SuspendProfileLayout();
        profilesListBox.Items.Clear();
        ClearAndDisposeControls(detailsContainer);
        ClearAndDisposeControls(examplesContainer);

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
            detailsContainer.Controls.Add(new MaterialLabel { Text = "No analyzed players match the current filter.", AutoSize = true });
        }

        ResumeProfileLayout();
    }

    private void UpdateDetails()
    {
        _ = UpdateDetailsAsync();
    }

    private async Task UpdateDetailsAsync()
    {
        int requestVersion = ++detailsRequestVersion;
        SuspendProfileLayout();
        ClearAndDisposeControls(detailsContainer);
        ClearAndDisposeControls(examplesContainer);

        if (profilesListBox.SelectedItem is not ProfileListItem item)
        {
            ResumeProfileLayout();
            return;
        }

        MaterialLabel loadingLabel = new() { Text = "Loading profile data...", AutoSize = true, Margin = new Padding(16) };
        detailsContainer.Controls.Add(loadingLabel);

        string playerKey = item.Summary.PlayerKey;
        PlayerProfileReport? report = null;

        await Task.Run(() => {
            profileService.TryBuildProfile(playerKey, out report);
        });

        if (requestVersion != detailsRequestVersion || IsDisposed)
        {
            ResumeProfileLayout();
            return;
        }

        ClearAndDisposeControls(detailsContainer);
        if (report is null)
        {
            detailsContainer.Controls.Add(new MaterialLabel { Text = "Failed to load report.", AutoSize = true });
            ResumeProfileLayout();
            return;
        }

        AddCollapsibleSection("Performance Summary", BuildSummaryContent(report), expanded: true);
        AddCollapsibleSection("Priority Recommendations", BuildWhatToFixContent(report), expanded: true);
        AddCollapsibleSection("Opening & Phase Analysis", BuildDetailedStatsContent(report), expanded: false);
        AddCollapsibleSection("Weekly Training Routine", BuildTrainingPlanContent(report), expanded: false);

        PopulateExamples(report);
        ResumeProfileLayout();
    }

    private void AddCollapsibleSection(string title, Control content, bool expanded, bool nested = false)
    {
        MaterialButton header = new()
        {
            Text = (expanded ? "[-] " : "[+] ") + title.ToUpperInvariant(),
            Type = nested ? MaterialButton.MaterialButtonType.Text : MaterialButton.MaterialButtonType.Contained,
            UseAccentColor = true,
            AutoSize = false,
            Height = nested ? 36 : 45,
            Dock = DockStyle.Top,
            Margin = new Padding(nested ? 16 : 0, nested ? 2 : 8, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        content.Visible = expanded;
        content.Dock = DockStyle.Top;
        content.Margin = new Padding(nested ? 32 : 0, 0, 0, nested ? 8 : 16);

        header.Click += (s, e) =>
        {
            content.Visible = !content.Visible;
            header.Text = (content.Visible ? "[-] " : "[+] ") + title.ToUpperInvariant();
            UpdateScrollableContentWidths();
        };

        detailsContainer.RowCount++;
        detailsContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsContainer.Controls.Add(header, 0, detailsContainer.RowCount - 1);

        detailsContainer.RowCount++;
        detailsContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsContainer.Controls.Add(content, 0, detailsContainer.RowCount - 1);
    }

    private Control BuildSummaryContent(PlayerProfileReport report)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        void AddRow(Control c)
        {
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(c, 0, layout.RowCount - 1);
        }

        AddRow(new MaterialLabel { Text = $"Games analyzed: {report.GamesAnalyzed} | Total moves: {report.TotalAnalyzedMoves}", AutoSize = true });
        AddRow(new MaterialLabel { Text = $"Average Centipawn Loss: {report.AverageCentipawnLoss?.ToString() ?? "n/a"}", AutoSize = true });

        if (report.MistakesByPhase.Count > 0)
        {
            var best = report.MistakesByPhase.OrderBy(p => p.Count).First();
            var worst = report.MistakesByPhase.OrderByDescending(p => p.Count).First();
            AddRow(new MaterialLabel { Text = $"Best phase: {best.Phase} | Needs work: {worst.Phase}", AutoSize = true });
        }

        if (report.TopMistakeLabels.Count > 0)
        {
            AddRow(new MaterialLabel { Text = "Main Struggles:", FontType = MaterialSkinManager.fontType.Subtitle1, AutoSize = true, Margin = new Padding(0, 12, 0, 4) });
            foreach (var label in report.TopMistakeLabels.Take(3))
            {
                AddRow(new MaterialLabel { Text = $"• {FormatLabel(label.Label)} ({label.Count} instances)", AutoSize = true });
            }
        }

        return layout;
    }

    private Control BuildWhatToFixContent(PlayerProfileReport report)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        void AddRow(Control c)
        {
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(c, 0, layout.RowCount - 1);
        }

        if (report.Recommendations.Count == 0)
        {
            AddRow(new MaterialLabel { Text = "Keep analyzing games to get personalized advice.", AutoSize = true });
        }
        else
        {
            foreach (var rec in report.Recommendations.Take(2))
            {
                AddRow(new MaterialLabel { Text = rec.Title, FontType = MaterialSkinManager.fontType.Subtitle1, AutoSize = true, HighEmphasis = true, Margin = new Padding(0, 8, 0, 0) });
                AddRow(new MaterialLabel { Text = rec.Description, FontType = MaterialSkinManager.fontType.Body2, AutoSize = true });
            }
        }

        return layout;
    }

    private Control BuildDetailedStatsContent(PlayerProfileReport report)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        void AddRow(Control c)
        {
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(c, 0, layout.RowCount - 1);
        }

        if (report.MistakesByPhase.Count > 0)
        {
            AddRow(new MaterialLabel { Text = "By Phase:", FontType = MaterialSkinManager.fontType.Subtitle2, AutoSize = true });
            foreach (var phase in report.MistakesByPhase)
            {
                AddRow(new MaterialLabel { Text = $"• {phase.Phase}: {phase.Count} mistakes", AutoSize = true });
            }
        }

        if (report.MistakesByOpening.Count > 0)
        {
            AddRow(new MaterialLabel { Text = "Problematic Openings:", FontType = MaterialSkinManager.fontType.Subtitle2, AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
            foreach (var opening in report.MistakesByOpening.Take(5))
            {
                AddRow(new MaterialLabel { Text = $"• {OpeningCatalog.Describe(opening.Eco)}: {opening.Count} mistakes", AutoSize = true });
            }
        }

        return layout;
    }

    private Control BuildTrainingPlanContent(PlayerProfileReport report)
    {
        DoubleBufferedTableLayoutPanel mainLayout = CreateContentLayout();
        mainLayout.Padding = new Padding(0); // Nested items will have their own padding

        void AddMainRow(Control c)
        {
            mainLayout.RowCount++;
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.Controls.Add(c, 0, mainLayout.RowCount - 1);
            c.Dock = DockStyle.Top;
        }

        AddMainRow(new MaterialLabel { Text = report.WeeklyPlan.Summary, FontType = MaterialSkinManager.fontType.Body1, AutoSize = true, Margin = new Padding(16, 0, 16, 12) });

        foreach (var day in report.WeeklyPlan.Days)
        {
            DoubleBufferedTableLayoutPanel dayContent = CreateContentLayout();
            dayContent.Padding = new Padding(24, 0, 16, 8);
            
            void AddDayRow(Control c)
            {
                dayContent.RowCount++;
                dayContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dayContent.Controls.Add(c, 0, dayContent.RowCount - 1);
                c.Dock = DockStyle.Top;
            }

            foreach (var activity in day.Activities)
            {
                AddDayRow(new MaterialLabel { Text = $"• {activity}", FontType = MaterialSkinManager.fontType.Body2, AutoSize = true });
            }
            AddDayRow(new MaterialLabel { Text = $"Success criteria: {day.SuccessCheck}", FontType = MaterialSkinManager.fontType.Caption, AutoSize = true, Margin = new Padding(0, 4, 0, 0) });

            // Using a simple header for the day
            MaterialButton dayHeader = new()
            {
                Text = $"[+] DAY {day.DayNumber}: {day.Theme} ({day.EstimatedMinutes} min)",
                Type = MaterialButton.MaterialButtonType.Text,
                AutoSize = false,
                Height = 36,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(16, 2, 16, 0)
            };

            dayContent.Visible = false;
            dayHeader.Click += (s, e) => {
                dayContent.Visible = !dayContent.Visible;
                dayHeader.Text = (dayContent.Visible ? "[-] " : "[+] ") + $"DAY {day.DayNumber}: {day.Theme} ({day.EstimatedMinutes} min)";
            };

            AddMainRow(dayHeader);
            AddMainRow(dayContent);
        }

        return mainLayout;
    }

    private DoubleBufferedTableLayoutPanel CreateContentLayout()
    {
        DoubleBufferedTableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = true,
            Padding = new Padding(16, 8, 16, 16),
            BackColor = surfaceColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return layout;
    }

    private static string FormatLabel(string label)
    {
        return label.Replace("_", " ").ToUpperInvariant();
    }

    private static string FormatFullPlan(WeeklyTrainingPlan plan)
    {
        StringBuilder sb = new();
        sb.AppendLine(plan.Title);
        sb.AppendLine(plan.Summary);
        sb.AppendLine();
        foreach (var day in plan.Days)
        {
            sb.AppendLine($"Day {day.DayNumber}: {day.Theme} ({day.EstimatedMinutes} min)");
            foreach (var act in day.Activities) sb.AppendLine($"- {act}");
            sb.AppendLine($"Success: {day.SuccessCheck}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void PopulateExamples(PlayerProfileReport report)
    {
        ClearAndDisposeControls(examplesContainer);
        if (report.Recommendations.Count == 0 && report.MistakeExamples.Count == 0)
        {
            examplesContainer.Controls.Add(new MaterialLabel { Text = "No examples available.", AutoSize = true, Padding = new Padding(16) });
            UpdateScrollableContentWidths();
            return;
        }

        void AddExampleRow(Control c) {
            examplesContainer.RowCount++;
            examplesContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            examplesContainer.Controls.Add(c, 0, examplesContainer.RowCount - 1);
            c.Dock = DockStyle.Top;
        }

        AddExampleRow(new MaterialLabel 
        { 
            Text = "Mistake Examples", 
            FontType = MaterialSkinManager.fontType.H6, 
            AutoSize = true, 
            Margin = new Padding(8, 8, 8, 16) 
        });

        HashSet<string> displayedFens = [];

        foreach (var rec in report.Recommendations)
        {
            if (rec?.Examples is null || rec.Examples.Count == 0) continue;

            AddExampleRow(new MaterialLabel 
            { 
                Text = $"Focus: {rec.Title}", 
                FontType = MaterialSkinManager.fontType.Subtitle1, 
                AutoSize = true, 
                Margin = new Padding(8, 0, 8, 8) 
            });

            foreach (var example in rec.Examples)
            {
                if (!displayedFens.Add(example.FenBefore)) continue;
                AddExampleRow(CreateExampleCard(example));
            }
        }

        var otherExamples = report.MistakeExamples.Where(e => !displayedFens.Contains(e.FenBefore)).ToList();
        if (otherExamples.Count > 0)
        {
            AddExampleRow(new MaterialLabel 
            { 
                Text = "Other Patterns", 
                FontType = MaterialSkinManager.fontType.Subtitle1, 
                AutoSize = true, 
                Margin = new Padding(8, 16, 8, 8) 
            });

            foreach (var example in otherExamples)
            {
                if (!displayedFens.Add(example.FenBefore)) continue;
                AddExampleRow(CreateExampleCard(example));
            }
        }

        UpdateScrollableContentWidths();
    }

    private Control CreateExampleCard(ProfileMistakeExample example)
    {
        MaterialCard card = new()
        {
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 12)
        };

        DoubleBufferedTableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8), BackColor = surfaceColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(layout);

        PictureBox thumbnail = new()
        {
            Size = new Size(112, 112),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = UI.Helpers.BoardThumbnailRenderer.Render(example.FenBefore, 112, pieceImages),
            Anchor = AnchorStyles.None
        };
        layout.Controls.Add(thumbnail, 0, 0);

        DoubleBufferedFlowLayoutPanel textLayout = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = card.BackColor
        };
        layout.Controls.Add(textLayout, 1, 0);

        textLayout.Controls.Add(new MaterialLabel
        {
            Text = $"{FormatLabel(example.Label)}",
            FontType = MaterialSkinManager.fontType.Subtitle2,
            AutoSize = true,
            HighEmphasis = true
        });

        textLayout.Controls.Add(new MaterialLabel
        {
            Text = $"{OpeningCatalog.Describe(example.Eco)}\nMove {example.MoveNumber}: {example.PlayedSan} (CPL {example.CentipawnLoss})",
            FontType = MaterialSkinManager.fontType.Body2,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 4)
        });

        MaterialButton btn = new()
        {
            Text = "Go to Analysis",
            Type = MaterialButton.MaterialButtonType.Contained,
            AutoSize = false,
            Size = new Size(140, 36)
        };
        btn.Click += (_, _) =>
        {
            navigateToExample?.Invoke(example);
            DialogResult = DialogResult.OK;
        };
        textLayout.Controls.Add(btn);

        return card;
    }

    private sealed record ProfileListItem(PlayerProfileSummary Summary, string Label)
    {
        public override string ToString() => Label;
    }

    private void SuspendProfileLayout()
    {
        SuspendLayout();
        detailsPanel.SuspendLayout();
        detailsContainer.SuspendLayout();
        examplesPanel.SuspendLayout();
        examplesContainer.SuspendLayout();
    }

    private void ResumeProfileLayout()
    {
        detailsContainer.ResumeLayout(true);
        detailsPanel.ResumeLayout(true);
        examplesContainer.ResumeLayout(true);
        examplesPanel.ResumeLayout(true);
        ResumeLayout(true);
        UpdateScrollableContentWidths();
    }

    private void UpdateScrollableContentWidths()
    {
        UpdateContainerWidth(detailsPanel, detailsContainer);
        UpdateContainerWidth(examplesPanel, examplesContainer);
    }

    private static void UpdateContainerWidth(ScrollableControl host, Control content)
    {
        int availableWidth = Math.Max(240, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        if (content.Width != availableWidth)
        {
            content.Width = availableWidth;
        }
    }

    private static void ClearAndDisposeControls(Control parent)
    {
        Control[] controls = parent.Controls.Cast<Control>().ToArray();
        parent.Controls.Clear();
        foreach (Control control in controls)
        {
            DisposeControlTree(control);
        }
    }

    private static void DisposeControlTree(Control control)
    {
        foreach (Control child in control.Controls.Cast<Control>().ToArray())
        {
            DisposeControlTree(child);
        }

        if (control is PictureBox pictureBox)
        {
            pictureBox.Image?.Dispose();
            pictureBox.Image = null;
        }

        control.Dispose();
    }
}
