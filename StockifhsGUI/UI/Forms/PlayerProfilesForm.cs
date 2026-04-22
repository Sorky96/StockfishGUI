using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace StockifhsGUI;

public sealed class PlayerProfilesForm : MaterialForm
{
    private sealed record CachedProfileView(PlayerProfileReport Report, PlayerProfilePresentationViewModel ViewModel);
    private sealed class WrappedLabel : Label
    {
        public int LastAppliedWidth { get; set; } = -1;

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncBackColor();
        }

        protected override void OnParentBackColorChanged(EventArgs e)
        {
            base.OnParentBackColorChanged(e);
            SyncBackColor();
        }

        private void SyncBackColor()
        {
            if (Parent is not null)
            {
                BackColor = Parent.BackColor;
            }
        }
    }

    private static class NativeMethods
    {
        public const int WmSetRedraw = 0x000B;
        public const int WmEnterSizeMove = 0x0231;
        public const int WmExitSizeMove = 0x0232;

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    private sealed class ProfileSectionWindow : MaterialForm
    {
        private readonly DoubleBufferedPanel scrollPanel;
        private readonly DoubleBufferedTableLayoutPanel contentHost;
        private int lastMeasuredWidth = -1;

        public ProfileSectionWindow(string title, Control content, Color surfaceColor, Size initialSize)
        {
            MaterialSkinManager.Instance.AddFormToManage(this);
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = initialSize;
            MinimumSize = new Size(720, 520);

            scrollPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(0, 0, 16, 0),
                BackColor = surfaceColor
            };

            contentHost = new DoubleBufferedTableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 0,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8),
                BackColor = surfaceColor
            };
            contentHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            content.Dock = DockStyle.Top;
            content.Margin = Padding.Empty;
            contentHost.Controls.Add(content);
            scrollPanel.Controls.Add(contentHost);
            Controls.Add(scrollPanel);

            scrollPanel.Resize += (_, _) => UpdateContentWidths(forceRefresh: false);
            Shown += (_, _) => UpdateContentWidths(forceRefresh: true);
        }

        private void UpdateContentWidths(bool forceRefresh)
        {
            int availableWidth = UpdateContainerWidth(scrollPanel, contentHost);
            if (!forceRefresh && availableWidth == lastMeasuredWidth)
            {
                return;
            }

            lastMeasuredWidth = availableWidth;
            UpdateWrappedLabelWidths(contentHost, availableWidth);
            contentHost.PerformLayout();
            scrollPanel.PerformLayout();
        }
    }

    private sealed class SectionLoadingWindow : MaterialForm
    {
        public SectionLoadingWindow(string title, Color surfaceColor)
        {
            MaterialSkinManager.Instance.AddFormToManage(this);
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 170);
            MinimumSize = new Size(420, 170);
            MaximumSize = new Size(420, 170);
            ControlBox = false;
            Sizable = false;

            DoubleBufferedTableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 0,
                Padding = new Padding(24),
                BackColor = surfaceColor
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            ProgressBar progressBar = new()
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28,
                Width = 320,
                Height = 20,
                Margin = new Padding(0, 16, 0, 14),
                Anchor = AnchorStyles.Left
            };
            AddContentRow(layout, progressBar);

            Label label = new()
            {
                AutoSize = true,
                Text = "Preparing section view...",
                ForeColor = Color.FromArgb(245, 245, 245),
                Font = SystemFonts.MessageBoxFont,
                Margin = new Padding(0),
                BackColor = surfaceColor
            };
            AddContentRow(layout, label);

            Controls.Add(layout);
        }
    }

    private readonly PlayerProfileService profileService;
    private readonly TextBox filterTextBox;
    private readonly ListBox profilesListBox;
    private readonly DoubleBufferedTableLayoutPanel detailsContainer;
    private readonly DoubleBufferedPanel detailsPanel;
    private readonly Action<ProfileMistakeExample>? navigateToExample;
    private readonly IReadOnlyDictionary<string, Image> pieceImages;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Bitmap> thumbnailCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<Bitmap>> thumbnailLoadTasks = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedProfileView> profileCache = new();
    private readonly Color surfaceColor;
    private readonly Color cardColor;
    private readonly Color highEmphasisTextColor;
    private readonly Color mediumEmphasisTextColor;
    private readonly Font primaryLabelFont;
    private readonly Font secondaryLabelFont;
    private readonly Font bodyLabelFont;
    private readonly System.Windows.Forms.Timer filterRefreshTimer;
    private int detailsRequestVersion;
    private int listRequestVersion;
    private int lastMeasuredDetailsWidth = -1;
    private bool isRefreshingList;
    private bool initialSelectionScheduled;
    private bool hasLoadedInitialList;
    private bool isInteractiveResize;
    private bool isHeavyRedrawSuspended;
    private string? currentPlayerKey;

    public PlayerProfilesForm(PlayerProfileService profileService, IReadOnlyDictionary<string, Image> pieceImages, Action<ProfileMistakeExample>? navigateToExample = null)
    {
        this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        this.pieceImages = pieceImages ?? throw new ArgumentNullException(nameof(pieceImages));
        this.navigateToExample = navigateToExample;
        DoubleBuffered = true;

        MaterialSkinManager materialSkinManager = MaterialSkinManager.Instance;
        materialSkinManager.AddFormToManage(this);
        surfaceColor = BackColor;
        cardColor = Color.FromArgb(78, 78, 78);
        highEmphasisTextColor = Color.FromArgb(245, 245, 245);
        mediumEmphasisTextColor = Color.FromArgb(220, 220, 220);
        primaryLabelFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        secondaryLabelFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        bodyLabelFont = new Font(Font.FontFamily, 10f, FontStyle.Regular);

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
        filterTextBox.TextChanged += (_, _) => QueueListRefresh();
        topBar.Controls.Add(filterTextBox, 1, 0);

        MaterialButton closeButton = new()
        {
            Text = "Close",
            AutoSize = false,
            Type = MaterialButton.MaterialButtonType.Outlined,
            Size = new Size(120, 36),
            Anchor = AnchorStyles.Left
        };
        closeButton.Click += (_, _) => Close();
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

        splitContainer.Panel2.Padding = new Padding(0);
        splitContainer.Panel2.Controls.Add(detailsPanel);
        splitContainer.Panel2.BackColor = surfaceColor;

        filterRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 300
        };
        filterRefreshTimer.Tick += (_, _) =>
        {
            filterRefreshTimer.Stop();
            _ = RefreshListAsync();
        };

        ResizeBegin += (_, _) => isInteractiveResize = true;
        ResizeEnd += (_, _) =>
        {
            isInteractiveResize = false;
            UpdateScrollableContentWidths(forceRefresh: true, rootOverride: null);
        };
        detailsPanel.Resize += (_, _) =>
        {
            if (isInteractiveResize)
            {
                UpdateContainerWidth(detailsPanel, detailsContainer);
                return;
            }

            UpdateScrollableContentWidths();
        };
        Shown += (_, _) =>
        {
            if (hasLoadedInitialList)
            {
                return;
            }

            hasLoadedInitialList = true;
            ShowListLoadingState("Loading analyzed players...");
            ShowDetailsLoadingState("Loading player profile...");
            _ = RefreshListAsync();
        };
    }

    private void QueueListRefresh()
    {
        filterRefreshTimer.Stop();
        filterRefreshTimer.Start();
    }

    private async Task RefreshListAsync()
    {
        int requestVersion = ++listRequestVersion;
        string filterText = filterTextBox.Text;
        ShowListLoadingState("Loading analyzed players...");
        ShowDetailsLoadingState("Loading player profile...");

        IReadOnlyList<PlayerProfileSummary> profiles = await Task.Run(() => profileService.ListProfiles(filterText));
        if (requestVersion != listRequestVersion || IsDisposed)
        {
            return;
        }

        isRefreshingList = true;
        SuspendProfileLayout();
        profilesListBox.BeginUpdate();
        try
        {
            string? previousPlayerKey = profilesListBox.SelectedItem is ProfileListItem previousItem
                ? previousItem.Summary.PlayerKey
                : currentPlayerKey;

            profilesListBox.Enabled = true;
            profilesListBox.Items.Clear();

            int selectedIndex = -1;
            for (int i = 0; i < profiles.Count; i++)
            {
                PlayerProfileSummary profile = profiles[i];
                string topLabels = profile.TopLabels.Count == 0
                    ? "no tags"
                    : string.Join(", ", profile.TopLabels);
                string averageCpl = profile.AverageCentipawnLoss?.ToString() ?? "n/a";
                string label = $"{profile.DisplayName,-18} games {profile.GamesAnalyzed,3} | CPL {averageCpl,4} | {topLabels}";
                profilesListBox.Items.Add(new ProfileListItem(profile, label));

                if (selectedIndex < 0 && string.Equals(profile.PlayerKey, previousPlayerKey, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                }
            }

            if (profilesListBox.Items.Count > 0)
            {
                profilesListBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                string selectedPlayerKey = ((ProfileListItem)profilesListBox.SelectedItem!).Summary.PlayerKey;
                if (!string.Equals(selectedPlayerKey, currentPlayerKey, StringComparison.Ordinal))
                {
                    initialSelectionScheduled = false;
                }
            }
            else
            {
                currentPlayerKey = null;
                ClearAndDisposeControls(detailsContainer);
                detailsContainer.Controls.Add(CreateStatusContent("No analyzed players match the current filter.", showSpinner: false));
            }
        }
        finally
        {
            profilesListBox.EndUpdate();
            ResumeProfileLayout();
            isRefreshingList = false;
        }

        EnsureInitialSelectionLoaded();
    }

    private void UpdateDetails()
    {
        if (isRefreshingList)
        {
            return;
        }

        _ = UpdateDetailsAsync();
    }

    private async Task UpdateDetailsAsync()
    {
        int requestVersion = ++detailsRequestVersion;
        if (profilesListBox.SelectedItem is not ProfileListItem item)
        {
            return;
        }

        string playerKey = item.Summary.PlayerKey;
        if (string.Equals(currentPlayerKey, playerKey, StringComparison.Ordinal) && detailsContainer.Controls.Count > 0)
        {
            return;
        }

        if (profileCache.TryGetValue(playerKey, out CachedProfileView? cachedProfile))
        {
            SuspendProfileLayout();
            ClearAndDisposeControls(detailsContainer);
            RenderProfile(cachedProfile);
            ResumeProfileLayout();
            return;
        }

        SuspendProfileLayout();
        ClearAndDisposeControls(detailsContainer);
        detailsContainer.Controls.Add(CreateStatusContent("Loading profile data...", showSpinner: true));
        ResumeProfileLayout();

        cachedProfile = await LoadProfileViewAsync(playerKey);

        if (requestVersion != detailsRequestVersion || IsDisposed)
        {
            return;
        }

        SuspendProfileLayout();
        ClearAndDisposeControls(detailsContainer);
        if (cachedProfile is null)
        {
            detailsContainer.Controls.Add(CreateStatusContent("Failed to load report.", showSpinner: false));
            ResumeProfileLayout();
            return;
        }

        RenderProfile(cachedProfile);
        ResumeProfileLayout();
    }

    private void RenderProfile(CachedProfileView cachedProfile)
    {
        PlayerProfileReport report = cachedProfile.Report;
        PlayerProfilePresentationViewModel viewModel = cachedProfile.ViewModel;
        currentPlayerKey = report.PlayerKey;

        AddCollapsibleSection("Summary", () => BuildSummaryContent(viewModel), expanded: true);
        AddCollapsibleSection("What to fix first", () => BuildWhatToFixContent(viewModel), expanded: true);
        AddCollapsibleSection("Detailed windows", () => BuildDetachedSectionsContent(report, viewModel), expanded: true);
    }

    private async Task<CachedProfileView?> LoadProfileViewAsync(string playerKey)
    {
        if (profileCache.TryGetValue(playerKey, out CachedProfileView? cachedProfile))
        {
            return cachedProfile;
        }

        PlayerProfileReport? report = null;
        await Task.Run(() => profileService.TryBuildProfile(playerKey, out report));
        if (report is null)
        {
            return null;
        }

        CachedProfileView created = new(report, PlayerProfilePresentationBuilder.Build(report));
        return profileCache.GetOrAdd(playerKey, created);
    }

    private void EnsureInitialSelectionLoaded()
    {
        if (initialSelectionScheduled || profilesListBox.Items.Count == 0 || profilesListBox.SelectedItem is not ProfileListItem)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            return;
        }

        initialSelectionScheduled = true;
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            UpdateDetails();
        }));
    }

    private void AddCollapsibleSection(string title, Func<Control> contentFactory, bool expanded, bool nested = false)
    {
        MaterialButton header = new()
        {
            Text = BuildSectionHeader(title, expanded),
            Type = nested ? MaterialButton.MaterialButtonType.Text : MaterialButton.MaterialButtonType.Outlined,
            UseAccentColor = true,
            AutoSize = false,
            Height = nested ? 36 : 42,
            Dock = DockStyle.Top,
            Margin = new Padding(nested ? 16 : 0, nested ? 2 : 8, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        Control? content = null;

        int headerRow = detailsContainer.RowCount++;
        detailsContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsContainer.Controls.Add(header, 0, headerRow);

        int contentRow = detailsContainer.RowCount++;
        detailsContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (expanded)
        {
            content = contentFactory();
            content.Visible = true;
            content.Dock = DockStyle.Top;
            content.Margin = new Padding(nested ? 32 : 0, 0, 0, nested ? 8 : 16);
            ResumeLayoutTree(content);
            detailsContainer.Controls.Add(content, 0, contentRow);
        }
        header.Click += (s, e) =>
        {
            RunWithDetailsRedrawSuspended(() =>
            {
                detailsContainer.SuspendLayout();
                try
                {
                    if (content == null)
                    {
                        content = contentFactory();
                        content.Visible = false;
                        content.Dock = DockStyle.Top;
                        content.Margin = new Padding(nested ? 32 : 0, 0, 0, nested ? 8 : 16);
                        ResumeLayoutTree(content);
                        detailsContainer.Controls.Add(content, 0, contentRow);
                    }

                    bool isExpanded = !content.Visible;
                    content.Visible = isExpanded;
                    header.Text = BuildSectionHeader(title, isExpanded);
                }
                finally
                {
                    detailsContainer.ResumeLayout(false);
                }

                if (content is not null && content.Visible)
                {
                    UpdateScrollableContentWidths(forceRefresh: true, rootOverride: content);
                }
                else
                {
                    UpdateScrollableContentWidths();
                }
            });
        };
    }

    private static string BuildSectionHeader(string title, bool expanded)
    {
        return (expanded ? "[-] " : "[+] ") + title;
    }

    private void ShowListLoadingState(string message)
    {
        profilesListBox.BeginUpdate();
        try
        {
            profilesListBox.Enabled = false;
            profilesListBox.Items.Clear();
            profilesListBox.Items.Add(message);
        }
        finally
        {
            profilesListBox.EndUpdate();
        }
    }

    private void ShowDetailsLoadingState(string message)
    {
        SuspendProfileLayout();
        try
        {
            ClearAndDisposeControls(detailsContainer);
            Control content = CreateStatusContent(message, showSpinner: true);
            ResumeLayoutTree(content);
            detailsContainer.Controls.Add(content);
        }
        finally
        {
            ResumeProfileLayout();
        }
    }

    private Control CreateStatusContent(string message, bool showSpinner)
    {
        DoubleBufferedTableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(24, 32, 24, 24),
            Margin = Padding.Empty,
            BackColor = surfaceColor
        };
        layout.SuspendLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        if (showSpinner)
        {
            ProgressBar progressBar = new()
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 28,
                Width = 220,
                Height = 20,
                Margin = new Padding(0, 0, 0, 12),
                Anchor = AnchorStyles.Left
            };
            AddContentRow(layout, progressBar);
        }

        AddContentRow(layout, CreatePrimaryLabel(message, 0, 0));
        return layout;
    }

    private Control BuildSummaryContent(PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        AddContentRow(layout, CreateSecondaryLabel(viewModel.SnapshotCaption, topMargin: 0, bottomMargin: 10));

        foreach (PlayerProfileSummaryItem item in viewModel.SummaryItems)
        {
            AddContentRow(layout, CreateInsightCard(item.Label, item.Value));
        }

        return layout;
    }

    private Control BuildWhatToFixContent(PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();
        DoubleBufferedPanel card = CreateBaseCard();
        DoubleBufferedTableLayoutPanel cardLayout = CreateCardLayout(card.BackColor);
        card.Controls.Add(cardLayout);

        AddCardRow(cardLayout, new MaterialLabel
        {
            Visible = false
        });

        for (int i = 0; i < viewModel.FixFirstItems.Count; i++)
        {
            AddCardRow(cardLayout, CreatePrimaryLabel($"{i + 1}. {viewModel.FixFirstItems[i]}", 0, i == 0 ? 0 : 4));
        }

        AddContentRow(layout, card);
        return layout;
    }

    private Control BuildKeyMistakesContent(PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        foreach (PlayerProfileStatItem item in viewModel.KeyMistakes)
        {
            AddContentRow(layout, CreateInsightCard(item.Title, item.Detail));
        }

        return layout;
    }

    private Control BuildCostliestMistakesContent(PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        foreach (PlayerProfileStatItem item in viewModel.CostliestMistakes)
        {
            AddContentRow(layout, CreateInsightCard(item.Title, item.Detail));
        }

        return layout;
    }

    private Control BuildWhatToWorkOnContent(PlayerProfileReport report, PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        for (int i = 0; i < viewModel.WorkOnItems.Count; i++)
        {
            PlayerProfileWorkItem item = viewModel.WorkOnItems[i];
            DoubleBufferedPanel card = CreateWorkItemCard();
            DoubleBufferedTableLayoutPanel cardLayout = CreateCardLayout(card.BackColor);
            card.Controls.Add(cardLayout);

            AddCardRow(cardLayout, CreatePrimaryLabel(item.Title, 0, 0));

            if (!string.IsNullOrWhiteSpace(item.Context))
            {
                AddCardRow(cardLayout, CreateSecondaryLabel(item.Context, 4, 8));
            }

            AddCardRow(cardLayout, CreateBodyLabel(item.Description));

            if (i < report.Recommendations.Count)
            {
                IReadOnlyList<ProfileMistakeExample> examples = report.Recommendations[i].Examples ?? [];
                if (examples.Count > 0)
                {
                    AddCardRow(cardLayout, CreateSecondaryLabel("Example positions", 12, 6));
                    foreach (ProfileMistakeExample example in examples)
                    {
                        AddCardRow(cardLayout, CreateRecommendationExampleSummary(example));
                    }
                }
            }

            AddContentRow(layout, card);
        }

        return layout;
    }

    private Control BuildRecentTrendContent(PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();
        AddContentRow(layout, CreateInsightCard(viewModel.RecentTrend.Headline, viewModel.RecentTrend.Summary, viewModel.RecentTrend.Comparison));
        return layout;
    }

    private Control BuildDetachedSectionsContent(PlayerProfileReport report, PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();
        AddContentRow(layout, CreateSecondaryLabel("Open heavier sections in separate windows to keep the main profile view responsive.", 0, 10));
        AddContentRow(layout, CreateDetachedSectionCard(
            "Training plan",
            "Full weekly plan with priorities, topic breakdown and daily sessions.",
            new Size(980, 760),
            () => BuildWeeklyTrainingPlanContent(viewModel.TrainingPlan)));
        AddContentRow(layout, CreateDetachedSectionCard(
            "What to work on",
            "Recommendations with example positions for each work item.",
            new Size(980, 760),
            () => BuildWhatToWorkOnContent(report, viewModel)));
        AddContentRow(layout, CreateDetachedSectionCard(
            "Deep dive",
            "Key mistakes, costliest mistakes and grouped example positions.",
            new Size(1100, 820),
            () => BuildDeepDiveContent(report, viewModel)));
        AddContentRow(layout, CreateDetachedSectionCard(
            "Recent trend",
            "Headline trend summary and comparison against earlier games.",
            new Size(760, 560),
            () => BuildRecentTrendContent(viewModel)));
        return layout;
    }

    private Control BuildWeeklyTrainingPlanContent(PlayerProfileTrainingPlanViewModel plan)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        AddContentRow(layout, CreateInsightCard("Diagnosis to plan", plan.Headline, plan.Summary));
        AddContentRow(layout, CreateInsightCard("Weekly budget", "Time budget", plan.BudgetSummary));
        AddContentRow(layout, CreateSecondaryLabel("Priority order", 8, 8));

        foreach (PlayerProfileTrainingPlanItemViewModel item in plan.Items)
        {
            DoubleBufferedPanel card = CreateBaseCard();
            DoubleBufferedTableLayoutPanel cardLayout = CreateCardLayout(card.BackColor);
            card.Controls.Add(cardLayout);

            AddCardRow(cardLayout, CreateSecondaryLabel($"{item.TopicPriorityLabel} • {item.Topic}", 0, 4));
            AddCardRow(cardLayout, CreatePrimaryLabel(item.ShortGoal, 0, 4));
            AddCardRow(cardLayout, CreateSecondaryLabel(
                $"Block type: {item.BlockType} | category: {item.Category.ToLowerInvariant()} | estimated time: {item.EstimatedMinutes} min",
                0,
                8));

            if (!string.IsNullOrWhiteSpace(item.Context))
            {
                AddCardRow(cardLayout, CreateSecondaryLabel(item.Context!, 0, 8));
            }

            AddCardRow(cardLayout, CreateSecondaryLabel("Why this topic now", 8, 6));
            AddCardRow(cardLayout, CreateBodyLabel(item.WhyThisTopicNow));

            AddContentRow(layout, card);
        }

        AddContentRow(layout, CreateSecondaryLabel("Topic breakdown", 8, 8));

        foreach (PlayerProfileTrainingTopicViewModel topic in plan.Topics)
        {
            DoubleBufferedPanel card = CreateBaseCard();
            DoubleBufferedTableLayoutPanel cardLayout = CreateCardLayout(card.BackColor);
            card.Controls.Add(cardLayout);

            AddCardRow(cardLayout, CreateSecondaryLabel(topic.RoleLabel, 0, 4));
            AddCardRow(cardLayout, CreatePrimaryLabel(topic.Title, 0, 4));
            AddCardRow(cardLayout, CreateSecondaryLabel(topic.FocusArea, 0, 8));
            AddCardRow(cardLayout, CreateBodyLabel(topic.Summary));

            if (!string.IsNullOrWhiteSpace(topic.Context))
            {
                AddCardRow(cardLayout, CreateSecondaryLabel(topic.Context!, 10, 6));
            }

            AddCardRow(cardLayout, CreateSecondaryLabel("Why this topic now", 10, 6));
            AddCardRow(cardLayout, CreateBodyLabel(topic.WhyThisTopicNow));
            AddCardRow(cardLayout, CreateSecondaryLabel(topic.Rationale, 0, 0));

            if (topic.Blocks.Count > 0)
            {
                AddCardRow(cardLayout, CreateSecondaryLabel("Training blocks", 10, 6));

                foreach (PlayerProfileTrainingBlockViewModel block in topic.Blocks)
                {
                    AddCardRow(cardLayout, CreateSecondaryLabel($"{block.PurposeLabel} • {block.KindLabel} • {block.EstimatedMinutes} min", 4, 4));
                    AddCardRow(cardLayout, CreateBodyLabel($"{block.Title}: {block.Description}"));
                }
            }

            AddContentRow(layout, card);
        }

        AddContentRow(layout, CreateSecondaryLabel("Weekly sessions", 8, 8));

        foreach (PlayerProfileTrainingDayViewModel day in plan.Days)
        {
            DoubleBufferedPanel card = CreateBaseCard();
            DoubleBufferedTableLayoutPanel cardLayout = CreateCardLayout(card.BackColor);
            card.Controls.Add(cardLayout);

            AddCardRow(cardLayout, CreatePrimaryLabel($"Day {day.DayNumber}. {day.Topic}", 0, 4));
            AddCardRow(cardLayout, CreateSecondaryLabel($"{day.RoleLabel} • {day.WorkType} • {day.EstimatedMinutes} min", 0, 8));
            AddCardRow(cardLayout, CreateBodyLabel(day.Goal));

            AddContentRow(layout, card);
        }

        return layout;
    }

    private Control BuildDeepDiveContent(PlayerProfileReport report, PlayerProfilePresentationViewModel viewModel)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();

        AddContentRow(layout, CreateSecondaryLabel("Detailed diagnosis behind the training plan.", 0, 10));
        AddContentRow(layout, BuildKeyMistakesContent(viewModel));
        AddContentRow(layout, BuildCostliestMistakesContent(viewModel));
        AddContentRow(layout, BuildExamplesContent(report));

        return layout;
    }

    private Control BuildExamplesContent(PlayerProfileReport report)
    {
        DoubleBufferedTableLayoutPanel layout = CreateContentLayout();
        IReadOnlyList<ProfileMistakeExample> examples = BuildDisplayedExamples(report);

        if (examples.Count == 0)
        {
            AddContentRow(layout, CreateSecondaryLabel("No example positions are available for this profile yet.", 0, 0));
            return layout;
        }

        AddContentRow(layout, CreateSecondaryLabel($"Showing {examples.Count} ranked example position{(examples.Count == 1 ? string.Empty : "s")} from dominant motifs.", 0, 10));

        foreach (IGrouping<string, ProfileMistakeExample> group in examples
            .GroupBy(example => example.Label, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            string header = PlayerProfileTextFormatter.FormatMistakeLabel(group.Key);
            AddContentRow(layout, CreateSecondaryLabel(header, 4, 8));

            foreach (ProfileMistakeExample example in group)
            {
                AddContentRow(layout, CreateExampleCard(example));
            }
        }

        return layout;
    }

    private DoubleBufferedTableLayoutPanel CreateContentLayout()
    {
        DoubleBufferedTableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = true,
            Padding = new Padding(16, 8, 16, 16),
            Margin = Padding.Empty,
            BackColor = surfaceColor
        };
        layout.SuspendLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return layout;
    }

    private static void AddContentRow(TableLayoutPanel layout, Control control)
    {
        control.Dock = DockStyle.Top;
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowCount - 1);
    }

    private static void AddCardRow(TableLayoutPanel layout, Control control)
    {
        control.Dock = DockStyle.Top;
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowCount - 1);
    }

    private DoubleBufferedPanel CreateBaseCard()
    {
        return new DoubleBufferedPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = cardColor,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private DoubleBufferedPanel CreateWorkItemCard()
    {
        return new DoubleBufferedPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(2),
            BackColor = Color.FromArgb(96, 96, 96),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private DoubleBufferedTableLayoutPanel CreateCardLayout(Color backColor)
    {
        DoubleBufferedTableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 12, 16, 12),
            Margin = Padding.Empty,
            BackColor = backColor
        };
        layout.SuspendLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return layout;
    }

    private DoubleBufferedPanel CreateDetachedSectionCard(string title, string description, Size initialWindowSize, Func<Control> contentFactory)
    {
        DoubleBufferedPanel card = CreateBaseCard();
        card.SuspendLayout();
        DoubleBufferedTableLayoutPanel layout = CreateCardLayout(card.BackColor);
        card.Controls.Add(layout);

        AddCardRow(layout, CreatePrimaryLabel(title, 0, 4));
        AddCardRow(layout, CreateBodyLabel(description));

        MaterialButton openButton = new()
        {
            Text = "Open window",
            Type = MaterialButton.MaterialButtonType.Contained,
            AutoSize = false,
            Size = new Size(150, 36),
            Margin = new Padding(0, 10, 0, 0)
        };
        openButton.Click += (_, _) => OpenDetachedSectionWindow(title, initialWindowSize, contentFactory);
        AddCardRow(layout, openButton);

        ResumeLayoutTree(card);
        return card;
    }

    private DoubleBufferedPanel CreateInsightCard(string label, string value, string? detail = null)
    {
        DoubleBufferedPanel card = CreateBaseCard();
        card.SuspendLayout();
        DoubleBufferedTableLayoutPanel layout = CreateCardLayout(card.BackColor);
        card.Controls.Add(layout);

        AddCardRow(layout, CreateSecondaryLabel(label));
        AddCardRow(layout, CreatePrimaryLabel(value, 2, string.IsNullOrWhiteSpace(detail) ? 0 : 8));

        if (!string.IsNullOrWhiteSpace(detail))
        {
            AddCardRow(layout, CreateBodyLabel(detail));
        }

        ResumeLayoutTree(card);
        return card;
    }

    private static string FormatFullPlan(WeeklyTrainingPlan plan)
    {
        StringBuilder sb = new();
        sb.AppendLine(plan.Title);
        sb.AppendLine(plan.Summary);
        sb.AppendLine(plan.Budget.Summary);
        sb.AppendLine();
        foreach (var day in plan.Days)
        {
            sb.AppendLine($"Day {day.DayNumber}: {day.Topic} ({day.EstimatedMinutes} min)");
            sb.AppendLine($"Work: {day.WorkType}");
            sb.AppendLine($"Goal: {day.Goal}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private IReadOnlyList<ProfileMistakeExample> BuildDisplayedExamples(PlayerProfileReport report)
    {
        if (report.MistakeExamples.Count == 0)
        {
            return [];
        }

        return report.MistakeExamples
            .GroupBy(example => $"{example.GameFingerprint}|{example.Ply}|{example.Rank}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(9)
            .ToList();
    }

    private Control CreateExampleCard(ProfileMistakeExample example)
    {
        DoubleBufferedPanel card = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = cardColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        card.SuspendLayout();

        DoubleBufferedTableLayoutPanel layout = new() 
        { 
            Dock = DockStyle.Top, 
            AutoSize = true, 
            ColumnCount = 2, 
            RowCount = 1, 
            Padding = new Padding(8), 
            BackColor = surfaceColor 
        };
        layout.SuspendLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(layout);

        PictureBox thumbnail = new()
        {
            Size = new Size(112, 112),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(40, 0, 0, 0)
        };
        layout.Controls.Add(thumbnail, 0, 0);

        BindExampleThumbnail(thumbnail, example.FenBefore);

        DoubleBufferedFlowLayoutPanel textLayout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = card.BackColor
        };
        textLayout.SuspendLayout();
        layout.Controls.Add(textLayout, 1, 0);

        textLayout.Controls.Add(CreateSecondaryLabel(PlayerProfileTextFormatter.FormatExampleRank(example.Rank), 0, 2));
        textLayout.Controls.Add(CreatePrimaryLabel(PlayerProfileTextFormatter.FormatMistakeLabel(example.Label), 0, 4));
        textLayout.Controls.Add(CreateBodyLabel(BuildExampleSummary(example)));

        MaterialButton btn = new()
        {
            Text = "Go to Analysis",
            Type = MaterialButton.MaterialButtonType.Contained,
            AutoSize = false,
            Size = new Size(140, 36),
            Margin = new Padding(0, 8, 0, 0)
        };
        btn.Click += (_, _) =>
        {
            navigateToExample?.Invoke(example);
        };
        textLayout.Controls.Add(btn);

        ResumeLayoutTree(card);
        return card;
    }

    private Control CreateRecommendationExampleSummary(ProfileMistakeExample example)
    {
        DoubleBufferedPanel card = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = cardColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        card.SuspendLayout();

        DoubleBufferedTableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
            BackColor = card.BackColor
        };
        layout.SuspendLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(layout);

        PictureBox thumbnail = new()
        {
            Size = new Size(112, 112),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(40, 0, 0, 0)
        };
        layout.Controls.Add(thumbnail, 0, 0);
        BindExampleThumbnail(thumbnail, example.FenBefore);

        DoubleBufferedFlowLayoutPanel textLayout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = card.BackColor
        };
        textLayout.SuspendLayout();
        layout.Controls.Add(textLayout, 1, 0);

        textLayout.Controls.Add(CreateSecondaryLabel(PlayerProfileTextFormatter.FormatExampleRank(example.Rank), 0, 4));
        textLayout.Controls.Add(CreatePrimaryLabel($"Move {example.MoveNumber}: {example.PlayedSan}", 0, 4));
        textLayout.Controls.Add(CreateBodyLabel(BuildExampleSummary(example)));

        MaterialButton btn = new()
        {
            Text = "Go to Analysis",
            Type = MaterialButton.MaterialButtonType.Contained,
            AutoSize = false,
            Size = new Size(140, 36),
            Margin = new Padding(0, 8, 0, 0)
        };
        btn.Click += (_, _) =>
        {
            navigateToExample?.Invoke(example);
        };
        textLayout.Controls.Add(btn);

        ResumeLayoutTree(card);
        return card;
    }

    private void BindExampleThumbnail(PictureBox thumbnail, string fen)
    {
        if (thumbnailCache.TryGetValue(fen, out Bitmap? cached))
        {
            thumbnail.Image = cached;
            return;
        }

        Task<Bitmap> renderTask = thumbnailLoadTasks.GetOrAdd(fen, key => Task.Run(() =>
        {
            Bitmap bitmap = UI.Helpers.BoardThumbnailRenderer.Render(key, 112, pieceImages);
            return thumbnailCache.GetOrAdd(key, bitmap);
        }));

        _ = ApplyThumbnailAsync(thumbnail, fen, renderTask);
    }

    private async Task ApplyThumbnailAsync(PictureBox thumbnail, string fen, Task<Bitmap> renderTask)
    {
        try
        {
            Bitmap bitmap = await renderTask;
            if (IsDisposed || thumbnail.IsDisposed)
            {
                return;
            }

            if (thumbnailCache.TryGetValue(fen, out Bitmap? cached))
            {
                bitmap = cached;
            }

            BeginInvoke(() =>
            {
                if (!IsDisposed && !thumbnail.IsDisposed)
                {
                    thumbnail.Image = bitmap;
                }
            });
        }
        catch
        {
            // Ignore render errors in background.
        }
        finally
        {
            if (renderTask.IsCompleted)
            {
                thumbnailLoadTasks.TryRemove(fen, out _);
            }
        }
    }

    private static string BuildExampleSummary(ProfileMistakeExample example)
    {
        return $"Label: {PlayerProfileTextFormatter.FormatMistakeLabel(example.Label)}{Environment.NewLine}"
            + $"CPL: {example.CentipawnLoss?.ToString() ?? "n/a"}{Environment.NewLine}"
            + $"Phase: {PlayerProfileTextFormatter.FormatPhase(example.Phase)}{Environment.NewLine}"
            + $"Opening: {PlayerProfileTextFormatter.FormatOpening(example.Eco)}{Environment.NewLine}"
            + $"Better move: {example.BetterMove}";
    }

    private Label CreatePrimaryLabel(string text, int topMargin = 0, int bottomMargin = 0)
    {
        return new WrappedLabel
        {
            AutoSize = true,
            Text = text,
            ForeColor = highEmphasisTextColor,
            Font = primaryLabelFont,
            Margin = new Padding(0, topMargin, 0, bottomMargin),
            BackColor = surfaceColor
        };
    }

    private Label CreateSecondaryLabel(string text, int topMargin = 0, int bottomMargin = 4)
    {
        return new WrappedLabel
        {
            AutoSize = true,
            Text = text,
            ForeColor = mediumEmphasisTextColor,
            Font = secondaryLabelFont,
            Margin = new Padding(0, topMargin, 0, bottomMargin),
            BackColor = surfaceColor
        };
    }

    private Label CreateBodyLabel(string text)
    {
        return new WrappedLabel
        {
            AutoSize = true,
            Text = text,
            ForeColor = highEmphasisTextColor,
            Font = bodyLabelFont,
            Margin = new Padding(0),
            BackColor = surfaceColor
        };
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
    }

    private void ResumeProfileLayout()
    {
        detailsContainer.ResumeLayout(false);
        detailsPanel.ResumeLayout(false);
        ResumeLayout(false);
        UpdateScrollableContentWidths();
    }

    private void UpdateScrollableContentWidths()
    {
        UpdateScrollableContentWidths(forceRefresh: false, rootOverride: null);
    }

    private void UpdateScrollableContentWidths(bool forceRefresh, Control? rootOverride = null)
    {
        int availableWidth = UpdateContainerWidth(detailsPanel, detailsContainer);
        if (!forceRefresh && rootOverride is null && availableWidth == lastMeasuredDetailsWidth)
        {
            return;
        }

        lastMeasuredDetailsWidth = availableWidth;
        Control targetRoot = rootOverride ?? detailsContainer;
        int targetWidth = targetRoot == detailsContainer
            ? availableWidth
            : CalculateAvailableWidth(targetRoot, availableWidth);
        UpdateWrappedLabelWidths(targetRoot, targetWidth);

        if (targetRoot == detailsContainer)
        {
            detailsContainer.PerformLayout();
            detailsPanel.PerformLayout();
        }
        else
        {
            targetRoot.PerformLayout();
        }
    }

    private static int UpdateContainerWidth(ScrollableControl host, Control content)
    {
        int availableWidth = Math.Max(240, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        if (content.Width != availableWidth)
        {
            content.Width = availableWidth;
        }

        return availableWidth;
    }

    private static void UpdateWrappedLabelWidths(Control root, int fallbackWidth)
    {
        foreach (Control child in root.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            int availableWidth = CalculateAvailableWidth(child, fallbackWidth);

            if (child is WrappedLabel label)
            {
                int labelWidth = Math.Max(160, availableWidth);
                if (label.LastAppliedWidth == labelWidth)
                {
                    UpdateWrappedLabelWidths(child, availableWidth);
                    continue;
                }

                label.AutoSize = true;
                label.MaximumSize = new Size(labelWidth, 0);
                label.LastAppliedWidth = labelWidth;
            }

            UpdateWrappedLabelWidths(child, availableWidth);
        }
    }

    private static int CalculateAvailableWidth(Control control, int fallbackWidth)
    {
        Control? parent = control.Parent;
        if (parent is null)
        {
            return fallbackWidth;
        }

        int width = parent.ClientSize.Width;
        if (width <= 0)
        {
            width = fallbackWidth;
        }

        width -= parent.Padding.Horizontal;
        width -= control.Margin.Horizontal;

        if (parent is TableLayoutPanel tableLayout && tableLayout.ColumnCount > 1)
        {
            width = Math.Max(160, width / tableLayout.ColumnCount);
        }

        return Math.Max(160, width);
    }

    private void OpenDetachedSectionWindow(string title, Size initialWindowSize, Func<Control> contentFactory)
    {
        using SectionLoadingWindow loadingWindow = new($"{title} - Loading", surfaceColor);
        loadingWindow.Show(this);
        loadingWindow.Update();
        Application.DoEvents();

        Control content = contentFactory();
        ResumeLayoutTree(content);

        loadingWindow.Close();

        ProfileSectionWindow window = new(title, content, surfaceColor, initialWindowSize);
        window.Show(this);
    }

    private static void ResumeLayoutTree(Control control)
    {
        foreach (Control child in control.Controls)
        {
            ResumeLayoutTree(child);
        }

        control.ResumeLayout(false);
    }

    private void RunWithDetailsRedrawSuspended(Action action)
    {
        if (isHeavyRedrawSuspended)
        {
            action();
            return;
        }

        ToggleControlRedraw(detailsContainer, false);
        ToggleControlRedraw(detailsPanel, false);
        try
        {
            action();
        }
        finally
        {
            ToggleControlRedraw(detailsContainer, true);
            ToggleControlRedraw(detailsPanel, true);
            detailsContainer.Refresh();
            detailsPanel.Refresh();
        }
    }

    private void SetHeavyRedrawSuspended(bool suspended)
    {
        if (isHeavyRedrawSuspended == suspended)
        {
            return;
        }

        isHeavyRedrawSuspended = suspended;
        ToggleControlRedraw(this, !suspended);
        ToggleControlRedraw(detailsPanel, !suspended);
        ToggleControlRedraw(detailsContainer, !suspended);
        ToggleControlRedraw(profilesListBox, !suspended);

        if (!suspended)
        {
            detailsPanel.Refresh();
            detailsContainer.Refresh();
            profilesListBox.Refresh();
            Refresh();
        }
    }

    private static void ToggleControlRedraw(Control control, bool enabled)
    {
        if (!control.IsHandleCreated)
        {
            return;
        }

        NativeMethods.SendMessage(control.Handle, NativeMethods.WmSetRedraw, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
        if (enabled)
        {
            control.Invalidate(true);
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
            pictureBox.Image = null;
        }

        if (control is ProgressBar progressBar)
        {
            progressBar.Style = ProgressBarStyle.Blocks;
        }

        control.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            filterRefreshTimer.Dispose();
            primaryLabelFont.Dispose();
            secondaryLabelFont.Dispose();
            bodyLabelFont.Dispose();

            foreach (Bitmap bitmap in thumbnailCache.Values)
            {
                bitmap.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WmEnterSizeMove:
                isInteractiveResize = true;
                SetHeavyRedrawSuspended(true);
                break;
            case NativeMethods.WmExitSizeMove:
                isInteractiveResize = false;
                SetHeavyRedrawSuspended(false);
                UpdateScrollableContentWidths(forceRefresh: true, rootOverride: null);
                break;
        }

        base.WndProc(ref m);
    }
}
