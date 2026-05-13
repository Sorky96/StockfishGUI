using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MoveMentorChess.Analysis;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;
using MoveMentorChess.Persistence;
using MoveMentorChess.Profiles;

namespace MoveMentorChess.App.Views;

public partial class ProfilesWindow : Window
{
    private const string TrainerPreparingSuggestionsText = "Your personal trainer is preparing suggestions...";

    private readonly PlayerProfileService profileService;
    private readonly IPlayerProfileFormatter profileFormatter;
    private readonly ITrainingPlanFormatter trainingPlanFormatter;
    private readonly Func<ProfileMistakeExample, Task>? navigateToProfileExampleAsync;
    private readonly Func<OpeningExampleGame, Task>? navigateToOpeningExampleAsync;
    private readonly Func<OpeningMoveRecommendation, Task>? navigateToOpeningPositionAsync;
    private List<PlayerProfileSummaryItemViewModel> items = [];
    private string? currentProfilePlayerKey;
    private int profileRenderVersion;

    public ProfilesWindow()
        : this(new PlayerProfileService(AnalysisStoreProvider.GetStore() ?? throw new InvalidOperationException("Local analysis store is unavailable.")))
    {
    }

    public ProfilesWindow(
        PlayerProfileService profileService,
        Func<ProfileMistakeExample, Task>? navigateToProfileExampleAsync = null,
        Func<OpeningExampleGame, Task>? navigateToOpeningExampleAsync = null,
        Func<OpeningMoveRecommendation, Task>? navigateToOpeningPositionAsync = null,
        IPlayerProfileFormatter? profileFormatter = null,
        ITrainingPlanFormatter? trainingPlanFormatter = null)
    {
        this.profileService = profileService;
        this.profileFormatter = profileFormatter ?? PlayerProfileFormatterFactory.CreateDefault();
        this.trainingPlanFormatter = trainingPlanFormatter ?? TrainingPlanFormatterFactory.CreateDefault();
        this.navigateToProfileExampleAsync = navigateToProfileExampleAsync;
        this.navigateToOpeningExampleAsync = navigateToOpeningExampleAsync;
        this.navigateToOpeningPositionAsync = navigateToOpeningPositionAsync;
        InitializeComponent();
        RefreshList();
    }

    private void FilterTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private async void ProfilesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is not PlayerProfileSummaryItemViewModel item)
        {
            ShowStatus("Select a player to inspect the profile.");
            return;
        }

        string requestedPlayerKey = item.Summary.PlayerKey;
        int renderVersion = ++profileRenderVersion;
        ShowProfileLoadingStatus(item.Summary.DisplayName);

        try
        {
            (PlayerProfileReport? Report, OpeningWeaknessReport? OpeningReport) result = await Task.Run<(PlayerProfileReport? Report, OpeningWeaknessReport? OpeningReport)>(() =>
            {
                if (!profileService.TryBuildProfile(requestedPlayerKey, out PlayerProfileReport? builtReport) || builtReport is null)
                {
                    return (null, null);
                }

                profileService.TryBuildOpeningWeaknessReport(requestedPlayerKey, out OpeningWeaknessReport? builtOpeningReport);
                return (builtReport, builtOpeningReport);
            });

            if (renderVersion != profileRenderVersion
                || IsClosed()
                || ProfilesListBox.SelectedItem is not PlayerProfileSummaryItemViewModel selectedItem
                || !string.Equals(selectedItem.Summary.PlayerKey, requestedPlayerKey, StringComparison.Ordinal))
            {
                return;
            }

            if (result.Report is null)
            {
                ShowStatus("Could not load the selected player profile.");
                return;
            }

            RenderProfile(result.Report, result.OpeningReport, renderVersion);
        }
        catch
        {
            ShowStatus("Could not load the selected player profile.");
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshList()
    {
        items = profileService.ListProfiles(FilterTextBox.Text)
            .Select(summary => new PlayerProfileSummaryItemViewModel(summary))
            .ToList();
        ProfilesListBox.ItemsSource = items;

        if (items.Count > 0)
        {
            ProfilesListBox.SelectedIndex = 0;
        }
        else
        {
            ShowStatus(BuildEmptyStateMessage());
        }
    }

    private string BuildEmptyStateMessage()
    {
        ProfileDataAvailability availability = profileService.GetDataAvailability(FilterTextBox.Text);
        ProfileDataAvailability totalAvailability = profileService.GetDataAvailability();
        if (availability.AnalyzedProfiles > 0)
        {
            return "No matching analyzed player. Try another name or clear the search.";
        }

        if (!string.IsNullOrWhiteSpace(FilterTextBox.Text) && totalAvailability.AnalyzedProfiles > 0)
        {
            return "No matching analyzed player. Try another name or clear the search.";
        }

        return "No player profile yet. Analyze at least one saved game, then come back here to see recurring mistakes, opening issues, and a weekly training plan.";
    }

    private void ShowStatus(string text)
    {
        DetailsPanel.Children.Clear();
        DetailsPanel.Children.Add(CreateSectionCard(
            "Player Coach",
            [
                CreateBodyText(text)
            ]));
    }

    private void RenderProfile(PlayerProfileReport report, OpeningWeaknessReport? openingReport)
        => RenderProfile(report, openingReport, ++profileRenderVersion);

    private void RenderProfile(PlayerProfileReport report, OpeningWeaknessReport? openingReport, int renderVersion)
    {
        DetailsPanel.Children.Clear();
        currentProfilePlayerKey = report.PlayerKey;
        StackPanel summaryPanel = BuildRowsPanel(BuildFormattedProfilePlaceholderRows());
        StackPanel weeklyPlanPanel = BuildRowsPanel(BuildWeeklyPlanRows(report, CreateTrainingPlanPlaceholder()));

        DetailsPanel.Children.Add(CreateHeroCard(report));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Coach summary", summaryPanel, isExpanded: true));
        DetailsPanel.Children.Add(CreateSnapshotCard(report));
        DetailsPanel.Children.Add(CreateMetricsCard(report));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Form and strength", BuildRatingAndFormRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Opening weaknesses", BuildOpeningWeaknessRows(openingReport), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Fix first", BuildFixFirstRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Training focus", BuildWorkOnRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Weekly plan", weeklyPlanPanel));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Recent form", BuildRecentTrendRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Why this matters", BuildDeepDiveRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Recurring mistakes", BuildTopLabelRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Costliest mistakes", BuildCostliestRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Example positions", BuildExampleRows(report)));

        _ = RenderFormattedProfileAsync(report, renderVersion, summaryPanel, weeklyPlanPanel);
    }

    private void ShowProfileLoadingStatus(string displayName)
    {
        DetailsPanel.Children.Clear();
        DetailsPanel.Children.Add(CreateSectionCard(
            "Player Coach",
            [
                CreateBodyText($"Loading {displayName}'s profile...", "#D7E2EA"),
                CreateBodyText(TrainerPreparingSuggestionsText, "#9EB5C5")
            ]));
    }

    private IEnumerable<Control> BuildFormattedProfileRows(PlayerProfileFormattedOutput output)
    {
        yield return CreateBodyText(output.ProfileSummary);
        yield return CreateBulletText(output.StrengthsAndWeaknesses);
        yield return CreateBulletText(output.WhatToFocusNext);
        yield return CreateBulletText(output.ToneAdaptedVersion);

        if (!string.IsNullOrWhiteSpace(output.DeepDive))
        {
            yield return CreateBodyText(output.DeepDive);
        }
    }

    private IEnumerable<Control> BuildFormattedProfilePlaceholderRows()
    {
        yield return CreateBodyText(TrainerPreparingSuggestionsText, "#D7E2EA");
    }

    private static TrainingPlanFormattedOutput CreateTrainingPlanPlaceholder()
    {
        return new TrainingPlanFormattedOutput(
            TrainerPreparingSuggestionsText,
            TrainerPreparingSuggestionsText,
            TrainerPreparingSuggestionsText,
            TrainerPreparingSuggestionsText);
    }

    private async Task RenderFormattedProfileAsync(
        PlayerProfileReport report,
        int renderVersion,
        StackPanel summaryPanel,
        StackPanel weeklyPlanPanel)
    {
        try
        {
            LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
            PlayerProfileAudienceLevel audienceLevel = ToProfileAudienceLevel(settings.DefaultExplanationLevel);
            AdviceNarrationStyle narrationStyle = settings.NarrationStyle;

            (PlayerProfileFormattedOutput Profile, TrainingPlanFormattedOutput TrainingPlan) formatted = await Task.Run(() =>
            {
                PlayerProfileFormattedOutput formattedProfile = profileFormatter.Format(report, audienceLevel, narrationStyle);
                TrainingPlanFormattedOutput formattedTrainingPlan = trainingPlanFormatter.Format(report.TrainingPlan, audienceLevel, narrationStyle);
                return (formattedProfile, formattedTrainingPlan);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (renderVersion != profileRenderVersion
                    || !string.Equals(currentProfilePlayerKey, report.PlayerKey, StringComparison.Ordinal)
                    || IsClosed())
                {
                    return;
                }

                ReplacePanelChildren(summaryPanel, BuildFormattedProfileRows(formatted.Profile));
                ReplacePanelChildren(weeklyPlanPanel, BuildWeeklyPlanRows(report, formatted.TrainingPlan));
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (renderVersion != profileRenderVersion || IsClosed())
                {
                    return;
                }

                PlayerProfileFormattedOutput fallbackProfile = new HeuristicPlayerProfileFormatter().Format(report);
                TrainingPlanFormattedOutput fallbackPlan = new HeuristicTrainingPlanFormatter().Format(report.TrainingPlan);
                ReplacePanelChildren(summaryPanel, BuildFormattedProfileRows(fallbackProfile));
                ReplacePanelChildren(weeklyPlanPanel, BuildWeeklyPlanRows(report, fallbackPlan));
            });
        }
    }

    private static PlayerProfileAudienceLevel ToProfileAudienceLevel(ExplanationLevel explanationLevel)
    {
        return explanationLevel switch
        {
            ExplanationLevel.Beginner => PlayerProfileAudienceLevel.Beginner,
            ExplanationLevel.Advanced => PlayerProfileAudienceLevel.Advanced,
            _ => PlayerProfileAudienceLevel.Intermediate
        };
    }

    private Control CreateHeroCard(PlayerProfileReport report)
    {
        Border card = CreateCardBorder();
        StackPanel panel = CreateCardPanel();

        panel.Children.Add(new TextBlock
        {
            Text = report.DisplayName,
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = $"Based on {report.GamesAnalyzed} games and {report.TotalAnalyzedMoves} analyzed moves.",
            FontSize = 15,
            Foreground = Brush.Parse("#D7E2EA"),
            TextWrapping = TextWrapping.Wrap
        });

        card.Child = panel;
        return card;
    }

    private Control CreateSnapshotCard(PlayerProfileReport report)
    {
        string mainIssue = report.TopMistakeLabels.Count > 0
            ? FormatMistakeLabel(report.TopMistakeLabels[0].Label)
            : "No dominant issue yet";
        string weakestPhase = report.MistakesByPhase.Count > 0
            ? FormatPhase(report.MistakesByPhase[0].Phase)
            : "mixed phases";
        string opening = report.MistakesByOpening.Count > 0
            ? FormatOpening(report.MistakesByOpening[0].Eco)
            : "mixed openings";

        return CreateInsightCard(
            "Profile snapshot",
            $"{FormatTrendHeadline(report.ProgressSignal.Direction)} • {mainIssue}",
            $"This player most often struggles in the {weakestPhase.ToLowerInvariant()} and the pattern clusters around {opening}. {BuildSnapshotSummary(report)}");
    }

    private Control CreateMetricsCard(PlayerProfileReport report)
    {
        Border card = CreateCardBorder();
        WrapPanel wrap = new()
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 220
        };

        wrap.Children.Add(CreateMetricTile("Games analyzed", report.GamesAnalyzed.ToString()));
        wrap.Children.Add(CreateMetricTile("Moves analyzed", report.TotalAnalyzedMoves.ToString()));
        wrap.Children.Add(CreateMetricTile("Highlighted mistakes", report.HighlightedMistakes.ToString()));
        wrap.Children.Add(CreateMetricTile("Average CPL", report.AverageCentipawnLoss?.ToString() ?? "n/a"));
        if (report.RatingTrend.CurrentStrength is not null)
        {
            MoveMentorStrengthPoint strength = report.RatingTrend.CurrentStrength;
            wrap.Children.Add(CreateMetricTile(
                "MoveMentor estimated strength",
                $"{strength.EstimatedStrength} ({strength.Low}-{strength.High})",
                300));
        }

        if (report.GamesBySide.Count > 0)
        {
            string sides = string.Join(" | ", report.GamesBySide.Select(side =>
                $"{(side.Side == PlayerSide.White ? "White" : "Black")}: {side.GamesAnalyzed} games, {side.HighlightedMistakes} mistakes"));
            wrap.Children.Add(CreateMetricTile("By side", sides, 440));
        }

        card.Child = wrap;
        return card;
    }

    private IEnumerable<Control> BuildRatingAndFormRows(PlayerProfileReport report)
    {
        yield return CreateBodyText(report.RatingTrend.Summary, "#D7E2EA");
        yield return CreateBodyText("Current estimate. It will get more reliable as more games are analyzed for this player.", "#9EB5C5");

        if (report.RatingTrend.RatingPoints.Count == 0 && report.RatingTrend.StrengthPoints.Count == 0)
        {
            yield return CreateBodyText("No rating or strength trend data yet.");
            yield break;
        }

        yield return CreateChartCard(
            "Rating trend",
            [
                new ProfileTrendChartSeries(
                    "Chess.com rating",
                    Brush.Parse("#7DD3FC"),
                    report.RatingTrend.RatingPoints.Select(point => new ProfileTrendChartPoint(FormatChartDate(point.GameDate), point.PlayerRating)).ToList()),
                new ProfileTrendChartSeries(
                    "MoveMentor estimated strength",
                    Brush.Parse("#FACC15"),
                    report.RatingTrend.StrengthPoints.Select(point => new ProfileTrendChartPoint(FormatChartDate(point.GameDate), point.EstimatedStrength)).ToList())
            ]);

        yield return CreateChartCard(
            "Average CPL",
            [
                new ProfileTrendChartSeries(
                    "Average CPL",
                    Brush.Parse("#FB7185"),
                    report.RatingTrend.AverageCentipawnLossTrend.Select(point => new ProfileTrendChartPoint(point.MonthKey, point.AverageCentipawnLoss)).ToList(),
                    ProfileTrendChartKind.Bars)
            ]);

        yield return CreateChartCard(
            "Move quality per game",
            [
                new ProfileTrendChartSeries(
                    "Blunders",
                    Brush.Parse("#F87171"),
                    report.RatingTrend.MoveQualityTrend.Select(point => new ProfileTrendChartPoint(point.PeriodKey, point.BlundersPerGame)).ToList(),
                    ProfileTrendChartKind.Bars),
                new ProfileTrendChartSeries(
                    "Mistakes",
                    Brush.Parse("#FDBA74"),
                    report.RatingTrend.MoveQualityTrend.Select(point => new ProfileTrendChartPoint(point.PeriodKey, point.MistakesPerGame)).ToList(),
                    ProfileTrendChartKind.Bars),
                new ProfileTrendChartSeries(
                    "Brilliant/great/best",
                    Brush.Parse("#86EFAC"),
                    report.RatingTrend.MoveQualityTrend.Select(point => new ProfileTrendChartPoint(point.PeriodKey, point.BrilliantGreatBestPerGame)).ToList(),
                    ProfileTrendChartKind.Bars)
            ]);

        if (report.RatingTrendsByTimeControl.Count > 0)
        {
            yield return CreateBodyText("By time control", "#9EB5C5");
            foreach (PlayerRatingTrendReport trend in report.RatingTrendsByTimeControl)
            {
                yield return CreateBulletText(trend.Summary);
            }
        }
    }

    private static Control CreateChartCard(string title, IReadOnlyList<ProfileTrendChartSeries> series)
    {
        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(CreateBodyText(title, "#FFFFFF"));
        panel.Children.Add(new ProfileTrendChartView
        {
            Height = 190,
            Series = series
        });
        card.Child = panel;
        return card;
    }

    private IEnumerable<Control> BuildTopLabelRows(PlayerProfileReport report)
    {
        if (report.TopMistakeLabels.Count == 0)
        {
            yield return CreateBodyText("No recurring labels yet.");
            yield break;
        }

        foreach (ProfileLabelStat item in report.TopMistakeLabels.Take(8))
        {
            yield return CreateBulletText($"{FormatMistakeLabel(item.Label)}: {FormatTimes(item.Count)}");
        }
    }

    private IEnumerable<Control> BuildFixFirstRows(PlayerProfileReport report)
    {
        IReadOnlyList<string> items = BuildFixFirstItems(report);
        foreach (string item in items)
        {
            yield return CreateBulletText(item);
        }
    }

    private IEnumerable<Control> BuildWorkOnRows(PlayerProfileReport report)
    {
        if (report.TrainingPlan.Topics.Count == 0)
        {
            yield return CreateBodyText("No focused work items yet.");
            yield break;
        }

        foreach (TrainingPlanTopic topic in report.TrainingPlan.Topics.Take(3))
        {
            Border innerCard = new()
            {
                Background = Brush.Parse("#182B37"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel panel = new() { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{BuildRoleLabel(topic.Category)}: {topic.Title}",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateBodyText(topic.Summary, "#D7E2EA"));
            panel.Children.Add(CreateBodyText(topic.WhyThisTopicNow, "#D7E2EA"));

            if (topic.Blocks.Count > 0)
            {
                string blocks = string.Join(", ", topic.Blocks.Select(block =>
                    $"{FormatTrainingBlockPurpose(block.Purpose).ToLowerInvariant()} {FormatTrainingBlockKind(block.Kind).ToLowerInvariant()}"));
                panel.Children.Add(CreateBodyText($"Training blocks: {blocks}.", "#9EB5C5"));
            }

            foreach (ProfileMistakeExample example in topic.Examples.Take(2))
            {
                panel.Children.Add(CreateExampleCard(example, compact: true));
            }

            innerCard.Child = panel;
            yield return innerCard;
        }
    }

    private IEnumerable<Control> BuildCostliestRows(PlayerProfileReport report)
    {
        if (report.CostliestMistakeLabels.Count == 0)
        {
            yield return CreateBodyText("No costly patterns yet.");
            yield break;
        }

        foreach (ProfileCostlyLabelStat item in report.CostliestMistakeLabels.Take(8))
        {
            yield return CreateBulletText($"{FormatMistakeLabel(item.Label)}: total CPL {item.TotalCentipawnLoss}, avg {item.AverageCentipawnLoss?.ToString() ?? "n/a"}");
        }
    }

    private IEnumerable<Control> BuildRecentTrendRows(PlayerProfileReport report)
    {
        yield return CreateBodyText(FormatTrendHeadline(report.ProgressSignal.Direction));
        yield return CreateBodyText(report.ProgressSignal.Summary, "#D7E2EA");

        if (report.ProgressSignal.Recent is not null)
        {
            yield return CreateBulletText($"Recent period: {FormatPeriod(report.ProgressSignal.Recent)}");
        }

        if (report.ProgressSignal.Previous is not null)
        {
            yield return CreateBulletText($"Earlier period: {FormatPeriod(report.ProgressSignal.Previous)}");
        }

        foreach (ProfileMonthlyTrend month in report.MonthlyTrend.Take(6))
        {
            yield return CreateBulletText($"{month.MonthKey}: {month.GamesAnalyzed} games, mistakes {month.HighlightedMistakes}, CPL {month.AverageCentipawnLoss?.ToString() ?? "n/a"}");
        }
    }

    private IEnumerable<Control> BuildDeepDiveRows(PlayerProfileReport report)
    {
        if (report.TopMistakeLabels.Count == 0
            && report.CostliestMistakeLabels.Count == 0
            && report.MistakesByPhase.Count == 0
            && report.MistakesByOpening.Count == 0
            && report.GamesBySide.Count == 0
            && report.LabelTrends.Count == 0
            && report.MonthlyTrend.Count == 0
            && report.QuarterlyTrend.Count == 0)
        {
            yield return CreateBodyText("More detail becomes available once recurring patterns accumulate.");
            yield break;
        }

        yield return CreateBodyText("Detailed diagnosis behind the training plan.", "#D7E2EA");
        yield return CreateBodyText("Recurring patterns", "#9EB5C5");
        foreach (ProfileLabelStat item in report.TopMistakeLabels.Take(5))
        {
            yield return CreateBulletText($"{FormatMistakeLabel(item.Label)}: {FormatTimes(item.Count)} in highlighted mistakes");
        }

        yield return CreateBodyText("Costliest mistakes", "#9EB5C5");
        foreach (ProfileCostlyLabelStat item in report.CostliestMistakeLabels.Take(5))
        {
            yield return CreateBulletText($"{FormatMistakeLabel(item.Label)}: total CPL {item.TotalCentipawnLoss}, avg {item.AverageCentipawnLoss?.ToString() ?? "n/a"}");
        }

        if (report.MistakesByPhase.Count > 0)
        {
            yield return CreateBodyText("By phase", "#9EB5C5");
            foreach (ProfilePhaseStat item in report.MistakesByPhase.Take(5))
            {
                yield return CreateBulletText($"{FormatPhase(item.Phase)}: {item.Count} highlighted mistakes");
            }
        }

        if (report.MistakesByOpening.Count > 0)
        {
            yield return CreateBodyText("By opening", "#9EB5C5");
            foreach (ProfileOpeningStat item in report.MistakesByOpening.Take(6))
            {
                yield return CreateBulletText($"{FormatOpening(item.Eco)}: {item.Count} highlighted mistakes");
            }
        }

        if (report.GamesBySide.Count > 0)
        {
            yield return CreateBodyText("By side", "#9EB5C5");
            foreach (ProfileSideStat item in report.GamesBySide)
            {
                string side = item.Side == PlayerSide.White ? "White" : "Black";
                yield return CreateBulletText($"{side}: {item.GamesAnalyzed} games, {item.HighlightedMistakes} highlighted mistakes");
            }
        }

        if (report.LabelTrends.Count > 0)
        {
            yield return CreateBodyText("Pattern trends", "#9EB5C5");
            foreach (ProfileLabelTrend trend in report.LabelTrends.Take(6))
            {
                yield return CreateBulletText(
                    $"{FormatMistakeLabel(trend.Label)}: {trend.Direction}, recent {trend.RecentCount}, previous {trend.PreviousCount}, recent CPL {trend.RecentAverageCentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        if (report.MonthlyTrend.Count > 0)
        {
            yield return CreateBodyText("Monthly trend", "#9EB5C5");
            foreach (ProfileMonthlyTrend item in report.MonthlyTrend.Take(6))
            {
                yield return CreateBulletText($"{item.MonthKey}: {item.GamesAnalyzed} games, mistakes {item.HighlightedMistakes}, CPL {item.AverageCentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        if (report.QuarterlyTrend.Count > 0)
        {
            yield return CreateBodyText("Quarterly trend", "#9EB5C5");
            foreach (ProfileQuarterlyTrend item in report.QuarterlyTrend.Take(4))
            {
                yield return CreateBulletText($"{item.QuarterKey}: {item.GamesAnalyzed} games, mistakes {item.HighlightedMistakes}, CPL {item.AverageCentipawnLoss?.ToString() ?? "n/a"}");
            }
        }

        IReadOnlyList<ProfileMistakeExample> rankedExamples = BuildDeepDiveExamples(report);
        if (rankedExamples.Count > 0)
        {
            yield return CreateBodyText($"Showing {rankedExamples.Count} ranked example positions from dominant motifs.", "#D7E2EA");

            foreach (IGrouping<string, ProfileMistakeExample> group in rankedExamples
                .GroupBy(example => example.Label, StringComparer.OrdinalIgnoreCase))
            {
                yield return CreateBodyText(FormatMistakeLabel(group.Key), "#9EB5C5");
                foreach (ProfileMistakeExample example in group)
                {
                    yield return CreateExampleCard(example);
                }
            }
        }
    }

    private IEnumerable<Control> BuildWeeklyPlanRows(PlayerProfileReport report, TrainingPlanFormattedOutput? formattedPlan = null)
    {
        if (formattedPlan is null)
        {
            LlamaGpuSettings settings = LlamaGpuSettingsStore.Load();
            formattedPlan = trainingPlanFormatter.Format(
                report.TrainingPlan,
                ToProfileAudienceLevel(settings.DefaultExplanationLevel),
                settings.NarrationStyle);
        }

        yield return CreateInsightCard("Short weekly plan", "Personalized plan", formattedPlan.ShortWeeklyPlan);
        yield return CreateInsightCard("Detailed weekly plan", "Expanded version", formattedPlan.DetailedWeeklyPlan);
        yield return CreateInsightCard("Why these priorities", "Priority rationale", formattedPlan.PriorityRationale);
        yield return CreateInsightCard("Tone adapted plan", "Training voice", formattedPlan.ToneAdaptedVersion);

        yield return CreateInsightCard("Diagnosis to plan", report.TrainingPlan.Topics.Count == 0
            ? "Training plan"
            : $"Training plan built from {string.Join(", ", report.TrainingPlan.Topics.Select(topic => topic.Title))}.", report.TrainingPlan.Summary);

        yield return CreateInsightCard("Weekly budget", "Time budget", report.WeeklyPlan.Budget.Summary);

        yield return CreateBodyText("Priority order", "#9EB5C5");
        foreach (TrainingPlanTopic topic in report.TrainingPlan.Topics.OrderBy(topic => topic.Priority))
        {
            foreach (TrainingBlock block in topic.Blocks
                .OrderBy(block => GetBlockPurposeOrder(block.Purpose))
                .ThenBy(block => block.EstimatedMinutes))
            {
                Border planCard = new()
                {
                    Background = Brush.Parse("#182B37"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                StackPanel planPanel = new() { Spacing = 6 };
                planPanel.Children.Add(new TextBlock
                {
                    Text = $"Priority {topic.Priority} • {topic.Title}",
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                });
                planPanel.Children.Add(CreateBodyText(block.Title, "#FFFFFF"));
                planPanel.Children.Add(CreateBodyText(
                    $"Block type: {FormatTrainingBlockKind(block.Kind)} | category: {FormatTrainingBlockPurpose(block.Purpose).ToLowerInvariant()} | estimated time: {block.EstimatedMinutes} min",
                    "#D7E2EA"));

                string topicContext = BuildTopicContext(topic);
                if (!string.IsNullOrWhiteSpace(topicContext))
                {
                    planPanel.Children.Add(CreateBodyText(topicContext, "#9EB5C5"));
                }

                planPanel.Children.Add(CreateBodyText("Why this topic now", "#9EB5C5"));
                planPanel.Children.Add(CreateBodyText(topic.WhyThisTopicNow, "#D7E2EA"));

                planCard.Child = planPanel;
                yield return planCard;
            }
        }

        yield return CreateBodyText("Topic breakdown", "#9EB5C5");
        foreach (TrainingPlanTopic topic in report.TrainingPlan.Topics.OrderBy(topic => topic.Priority))
        {
            Border topicCard = new()
            {
                Background = Brush.Parse("#182B37"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel panel = new() { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{BuildRoleLabel(topic.Category)}: {topic.Title}",
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateBodyText(topic.FocusArea, "#9EB5C5"));
            panel.Children.Add(CreateBodyText(topic.Summary, "#D7E2EA"));
            panel.Children.Add(CreateBodyText(topic.WhyThisTopicNow, "#D7E2EA"));

            if (!string.IsNullOrWhiteSpace(topic.Rationale))
            {
                panel.Children.Add(CreateBodyText(topic.Rationale, "#9EB5C5"));
            }

            string context = BuildTopicContext(topic);
            if (!string.IsNullOrWhiteSpace(context))
            {
                panel.Children.Add(CreateBodyText(context, "#9EB5C5"));
            }

            foreach (TrainingBlock block in topic.Blocks)
            {
                panel.Children.Add(CreateBulletText(
                    $"{FormatTrainingBlockKind(block.Kind)} | {FormatTrainingBlockPurpose(block.Purpose)} | {block.EstimatedMinutes} min | {block.Title}"));
            }

            topicCard.Child = panel;
            yield return topicCard;
        }

        yield return CreateBodyText("Weekly schedule", "#9EB5C5");
        foreach (WeeklyTrainingDay day in report.WeeklyPlan.Days)
        {
            Border dayCard = new()
            {
                Background = Brush.Parse("#182B37"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel dayPanel = new() { Spacing = 6 };
            dayPanel.Children.Add(new TextBlock
            {
                Text = $"Day {day.DayNumber}: {day.Topic}",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            dayPanel.Children.Add(CreateBodyText($"{day.WorkType} | {day.EstimatedMinutes} min", "#D7E2EA"));
            dayPanel.Children.Add(CreateBodyText(day.Goal, "#D7E2EA"));
            if (day.LaunchTrainingMode.HasValue && day.RelatedOpenings is { Count: > 0 })
            {
                dayPanel.Children.Add(CreateSectionButton(
                    "Practice this opening",
                    async () => await OpenOpeningTrainerAsync(day.RelatedOpenings[0])));
            }

            dayCard.Child = dayPanel;
            yield return dayCard;
        }
    }

    private IEnumerable<Control> BuildExampleRows(PlayerProfileReport report)
    {
        if (report.MistakeExamples.Count == 0)
        {
            yield return CreateBodyText("No example positions available yet.");
            yield break;
        }

        foreach (IGrouping<string, ProfileMistakeExample> group in report.MistakeExamples
            .Take(9)
            .GroupBy(example => example.Label, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return CreateBodyText(FormatMistakeLabel(group.Key), "#9EB5C5");
            foreach (ProfileMistakeExample example in group)
            {
                yield return CreateExampleCard(example);
            }
        }
    }

    private IEnumerable<Control> BuildOpeningWeaknessRows(OpeningWeaknessReport? report)
    {
        if (report is null || report.WeakOpenings.Count == 0)
        {
            yield return CreateBodyText("No recurring opening weaknesses available yet.");
            yield break;
        }

        yield return CreateInsightCard(
            "Opening signal",
            $"Across {report.OpeningGamesAnalyzed} opening samples, the sharpest problems cluster in {report.WeakOpenings.Count} recurring openings.",
            $"Average opening CPL: {report.AverageOpeningCentipawnLoss?.ToString() ?? "n/a"}. Use the example game and position shortcuts to jump straight into the board view.");

        foreach (OpeningWeaknessEntry opening in report.WeakOpenings.Take(5))
        {
            yield return CreateOpeningWeaknessCard(opening, report.OpeningGamesAnalyzed);
        }
    }

    private Control CreateSectionCard(string title, IEnumerable<Control> children)
    {
        Border card = CreateCardBorder();
        StackPanel panel = CreateCardPanel();

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }

        card.Child = panel;
        return card;
    }

    private Control CreateCollapsibleSection(string title, IEnumerable<Control> children, bool isExpanded = false)
    {
        return CreateCollapsibleSection(title, BuildRowsPanel(children), isExpanded);
    }

    private Control CreateCollapsibleSection(string title, StackPanel panel, bool isExpanded = false)
    {
        Expander expander = new()
        {
            IsExpanded = isExpanded,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush.Parse("#203542"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border
            {
                Padding = new Thickness(16, 4, 16, 16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = panel
            },
            Header = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 14, 16, 14),
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = 19,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        return expander;
    }

    private StackPanel BuildRowsPanel(IEnumerable<Control> children)
    {
        StackPanel panel = CreateCardPanel();
        ReplacePanelChildren(panel, children);
        return panel;
    }

    private static void ReplacePanelChildren(StackPanel panel, IEnumerable<Control> children)
    {
        panel.Children.Clear();
        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }
    }

    private static Border CreateCardBorder()
    {
        return new Border
        {
            Background = Brush.Parse("#203542"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static StackPanel CreateCardPanel()
    {
        return new StackPanel
        {
            Spacing = 6
        };
    }

    private static Button CreateSectionButton(string title, Action onClick)
    {
        Button button = new()
        {
            Content = title,
            Margin = new Thickness(0, 0, 8, 8),
            MinWidth = 200
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control CreateMetricTile(string label, string value, double? width = null)
    {
        Border tile = new()
        {
            Width = width ?? 220,
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8)
        };

        StackPanel panel = new() { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush.Parse("#9EB5C5"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        });

        tile.Child = panel;
        return tile;
    }

    private static TextBlock CreateBodyText(string text, string? color = null)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = color is null ? Brushes.White : Brush.Parse(color),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static TextBlock CreateBulletText(string text)
    {
        return new TextBlock
        {
            Text = $"• {text}",
            Foreground = Brushes.White,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static string FormatPeriod(ProfileProgressPeriod period)
    {
        return $"{period.GamesAnalyzed} games, CPL {period.AverageCentipawnLoss?.ToString() ?? "n/a"}, highlighted mistakes/game {period.HighlightedMistakesPerGame:F2}";
    }

    private Control CreateExampleCard(ProfileMistakeExample example, bool compact = false)
    {
        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(compact ? "120,*" : "160,*")
        };

        Border boardHost = new()
        {
            Width = compact ? 120 : 160,
            Height = compact ? 120 : 160,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };
        boardHost.Child = new ChessBoardView
        {
            Width = compact ? 120 : 160,
            Height = compact ? 120 : 160,
            Fen = example.FenBefore,
            IsHitTestVisible = false
        };
        grid.Children.Add(boardHost);

        StackPanel panel = new()
        {
            Margin = new Thickness(14, 0, 0, 0),
            Spacing = 6
        };
        Grid.SetColumn(panel, 1);

        panel.Children.Add(new TextBlock
        {
            Text = $"Move {example.MoveNumber}{(example.Side == PlayerSide.White ? "." : "...")} {example.PlayedSan}",
            FontSize = compact ? 16 : 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(FormatExampleRank(example.Rank), "#9EB5C5"));
        panel.Children.Add(CreateBodyText($"Better move: {example.BetterMove}", "#D7E2EA"));
        panel.Children.Add(CreateBodyText($"Label: {FormatMistakeLabel(example.Label)} | CPL: {example.CentipawnLoss?.ToString() ?? "n/a"} | Phase: {FormatPhase(example.Phase)}", "#D7E2EA"));
        panel.Children.Add(CreateBodyText($"Opening: {FormatOpening(example.Eco)}", "#D7E2EA"));

        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Content = "Go to Analysis",
            IsEnabled = navigateToProfileExampleAsync is not null
        };
        button.Click += async (_, _) =>
        {
            if (navigateToProfileExampleAsync is null)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await navigateToProfileExampleAsync(example);
                Close();
            }
            finally
            {
                if (!IsClosed())
                {
                    button.IsEnabled = true;
                }
            }
        };
        panel.Children.Add(button);

        grid.Children.Add(panel);
        card.Child = grid;
        return card;
    }

    private Control CreateOpeningWeaknessCard(OpeningWeaknessEntry opening, int openingGamesAnalyzed)
    {
        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = opening.OpeningDisplayName,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(
            $"{opening.Eco} | {FormatOpeningFrequency(opening.Count, openingGamesAnalyzed)} | Avg opening CPL {opening.AverageOpeningCentipawnLoss?.ToString() ?? "n/a"}",
            "#D7E2EA"));
        panel.Children.Add(CreateBodyText(
            $"{FormatOpeningWeaknessCategory(opening.Category)} | {FormatTrendHeadline(opening.TrendDirection)}",
            "#9EB5C5"));
        panel.Children.Add(CreateBodyText(opening.CategoryReason, "#9EB5C5"));
        panel.Children.Add(CreateBodyText(
            $"First recurring mistake: {FormatMistakeLabel(opening.FirstRecurringMistakeType ?? "unclassified")} ({opening.FirstRecurringMistakeCount} examples).",
            "#D7E2EA"));

        if (opening.RecurringMistakeSequences.Count > 0)
        {
            string sequenceSummary = string.Join(
                " | ",
                opening.RecurringMistakeSequences.Select(item =>
                    $"{string.Join(" -> ", item.Labels.Select(FormatMistakeLabel))} ({item.Count})"));
            panel.Children.Add(CreateBodyText($"Recurring sequence: {sequenceSummary}", "#9EB5C5"));
        }

        if (opening.ExampleGames.Count > 0)
        {
            panel.Children.Add(CreateBodyText("Example games", "#9EB5C5"));
            foreach (OpeningExampleGame example in opening.ExampleGames.Take(3))
            {
                panel.Children.Add(CreateOpeningExampleCard(example));
            }
        }

        if (opening.ExampleBetterMoves.Count > 0)
        {
            panel.Children.Add(CreateBodyText("Example positions", "#9EB5C5"));
            foreach (OpeningMoveRecommendation recommendation in opening.ExampleBetterMoves.Take(3))
            {
                panel.Children.Add(CreateOpeningPositionCard(recommendation));
            }
        }

        WrapPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Button trainingButton = new()
        {
            Content = "Practice this opening",
            IsEnabled = !string.IsNullOrWhiteSpace(currentProfilePlayerKey),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 220
        };
        trainingButton.Click += async (_, _) =>
        {
            trainingButton.IsEnabled = false;
            try
            {
                await OpenOpeningTrainerAsync(opening.Eco);
            }
            finally
            {
                if (!IsClosed())
                {
                    trainingButton.IsEnabled = !string.IsNullOrWhiteSpace(currentProfilePlayerKey);
                }
            }
        };
        actions.Children.Add(trainingButton);
        panel.Children.Add(actions);

        card.Child = panel;
        return card;
    }

    private async Task OpenOpeningTrainerAsync(string? openingFilter)
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            OpenSectionWindow(
                "Opening Trainer",
                [
                    CreateBodyText("Opening Trainer is unavailable because the local analysis store is not ready.", "#D7E2EA")
                ]);
            return;
        }

        OpeningTrainerWindowViewModel viewModel = new(store);
        if (!string.IsNullOrWhiteSpace(currentProfilePlayerKey))
        {
            viewModel.AdvancedPlayerKey = currentProfilePlayerKey;
        }

        if (!string.IsNullOrWhiteSpace(openingFilter))
        {
            viewModel.FilterText = openingFilter;
            viewModel.RefreshCommand.Execute(null);
        }

        OpeningTrainerWindow window = new(viewModel)
        {
            Title = "Opening Trainer"
        };
        await window.ShowDialog(this);
    }

    private Control CreateOpeningExampleCard(OpeningExampleGame example)
    {
        Border card = new()
        {
            Background = Brush.Parse("#203542"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        StackPanel panel = new() { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{example.OpponentName} | {example.DateText ?? "Unknown date"} | {example.Result ?? "?"}",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(
            $"First mistake: {FormatMistakeLabel(example.FirstMistakeType ?? "unclassified")} on {FormatPlyLabel(example.Side, example.FirstMistakePly, example.FirstMistakeSan)}",
            "#D7E2EA"));
        panel.Children.Add(CreateBodyText(
            $"Opening: {example.OpeningDisplayName} | CPL {example.FirstMistakeCentipawnLoss?.ToString() ?? "n/a"}",
            "#D7E2EA"));

        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "Open game",
            IsEnabled = navigateToOpeningExampleAsync is not null
        };
        button.Click += async (_, _) =>
        {
            if (navigateToOpeningExampleAsync is null)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await navigateToOpeningExampleAsync(example);
                Close();
            }
            finally
            {
                if (!IsClosed())
                {
                    button.IsEnabled = true;
                }
            }
        };
        panel.Children.Add(button);

        card.Child = panel;
        return card;
    }

    private Control CreateOpeningPositionCard(OpeningMoveRecommendation recommendation)
    {
        Border card = new()
        {
            Background = Brush.Parse("#203542"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("120,*")
        };

        IReadOnlyList<BoardArrowViewModel> arrows = BuildPreviewArrows(
            recommendation.FenBefore,
            (recommendation.PlayedSan, Color.Parse("#F6C453")),
            (recommendation.BetterMove, Color.Parse("#58D68D")));
        Control boardHost = CreateBoardPreview(
            recommendation.FenBefore,
            120,
            arrows,
            async () => await ShowBoardPreviewWindowAsync(
                title: $"Opening Position | {OpeningCatalog.Describe(recommendation.Eco)}",
                fen: recommendation.FenBefore,
                arrows: arrows,
                detailLines: BuildRecommendationPreviewDetailLines(recommendation)));
        grid.Children.Add(boardHost);

        StackPanel panel = new()
        {
            Margin = new Thickness(14, 0, 0, 0),
            Spacing = 6
        };
        Grid.SetColumn(panel, 1);

        panel.Children.Add(new TextBlock
        {
            Text = FormatPlyLabel(recommendation.Side, recommendation.Ply, recommendation.PlayedSan),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText($"Your move: {recommendation.PlayedSan}", "#9EB5C5"));
        panel.Children.Add(CreateBodyText($"Suggested move: {recommendation.BetterMove}", "#D7E2EA"));
        panel.Children.Add(CreateBodyText(
            $"Theme: {FormatMistakeLabel(recommendation.MistakeType ?? "unclassified")} | CPL {recommendation.CentipawnLoss?.ToString() ?? "n/a"}",
            "#D7E2EA"));

        Button button = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = "Open position",
            IsEnabled = navigateToOpeningPositionAsync is not null
        };
        button.Click += async (_, _) =>
        {
            if (navigateToOpeningPositionAsync is null)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await navigateToOpeningPositionAsync(recommendation);
                Close();
            }
            finally
            {
                if (!IsClosed())
                {
                    button.IsEnabled = true;
                }
            }
        };
        panel.Children.Add(button);

        grid.Children.Add(panel);
        card.Child = grid;
        return card;
    }

    private async void OpenSectionWindow(string title, IEnumerable<Control> content)
    {
        Window window = new()
        {
            Title = title,
            Width = 1320,
            Height = 900,
            MinWidth = 960,
            MinHeight = 700,
            Background = Brush.Parse("#23313B"),
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            CreateSectionCard(title, content)
                        }
                    }
                }
            }
        };

        await window.ShowDialog(this);
    }

    private static Control CreateBoardPreview(
        string fen,
        double size,
        IReadOnlyList<BoardArrowViewModel> arrows,
        Func<Task>? onClick = null)
    {
        Border boardHost = new()
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };

        boardHost.Child = new ChessBoardView
        {
            Width = size,
            Height = size,
            Fen = fen,
            Arrows = arrows,
            IsHitTestVisible = false
        };

        if (onClick is null)
        {
            return boardHost;
        }

        Button button = new()
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = boardHost
        };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private async Task ShowBoardPreviewWindowAsync(
        string title,
        string fen,
        IReadOnlyList<BoardArrowViewModel> arrows,
        IReadOnlyList<string> detailLines)
    {
        Window window = new()
        {
            Title = title,
            Width = 980,
            Height = 780,
            MinWidth = 760,
            MinHeight = 620,
            Background = Brush.Parse("#23313B")
        };

        StackPanel rightPanel = new()
        {
            Spacing = 8
        };

        foreach (string line in detailLines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            rightPanel.Children.Add(CreateBodyText(line, "#D7E2EA"));
        }

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("460,*")
        };

        Border boardCard = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Child = new ChessBoardView
            {
                Width = 420,
                Height = 420,
                Fen = fen,
                Arrows = arrows,
                IsHitTestVisible = false
            }
        };
        grid.Children.Add(boardCard);

        Border detailsCard = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(18, 0, 0, 0),
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = rightPanel
            }
        };
        Grid.SetColumn(detailsCard, 1);
        grid.Children.Add(detailsCard);

        window.Content = new Border
        {
            Padding = new Thickness(18),
            Child = grid
        };

        await window.ShowDialog(this);
    }

    private static IReadOnlyList<string> BuildRecommendationPreviewDetailLines(OpeningMoveRecommendation recommendation)
    {
        return
        [
            FormatPlyLabel(recommendation.Side, recommendation.Ply, recommendation.PlayedSan),
            $"Your move: {recommendation.PlayedSan}",
            $"Suggested move: {recommendation.BetterMove}",
            $"Theme: {FormatMistakeLabel(recommendation.MistakeType ?? "unclassified")} | CPL {recommendation.CentipawnLoss?.ToString() ?? "n/a"}"
        ];
    }

    private static IReadOnlyList<BoardArrowViewModel> BuildPreviewArrows(
        string fen,
        params (string? MoveText, Color Color)[] moveSpecs)
    {
        List<BoardArrowViewModel> arrows = [];
        foreach ((string? moveText, Color color) in moveSpecs)
        {
            if (TryBuildArrow(fen, moveText, color, out BoardArrowViewModel arrow))
            {
                arrows.Add(arrow);
            }
        }

        return arrows;
    }

    private static bool TryBuildArrow(string fen, string? moveText, Color color, out BoardArrowViewModel arrow)
    {
        arrow = default!;
        if (string.IsNullOrWhiteSpace(fen) || string.IsNullOrWhiteSpace(moveText))
        {
            return false;
        }

        ChessGame game = new();
        if (!game.TryLoadFen(fen, out _))
        {
            return false;
        }

        if (TryApplyPreviewMove(game, moveText, out AppliedMoveInfo? appliedMove) && appliedMove is not null)
        {
            arrow = new BoardArrowViewModel(appliedMove.FromSquare, appliedMove.ToSquare, color);
            return true;
        }

        return false;
    }

    private static bool TryApplyPreviewMove(ChessGame game, string moveText, out AppliedMoveInfo? appliedMove)
    {
        appliedMove = null;
        string trimmed = moveText.Trim();
        string? uci = TryExtractUci(trimmed);
        if (!string.IsNullOrWhiteSpace(uci) && game.TryApplyUci(uci, out appliedMove, out _))
        {
            return appliedMove is not null;
        }

        string san = TrimMoveDisplayText(trimmed);
        try
        {
            appliedMove = game.ApplySanWithResult(san);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryExtractUci(string moveText)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(moveText, "^[a-h][1-8][a-h][1-8][qrbn]?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return moveText;
        }

        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            moveText,
            "\\(([a-h][1-8][a-h][1-8][qrbn]?)\\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string TrimMoveDisplayText(string moveText)
    {
        int parenIndex = moveText.IndexOf(" (", StringComparison.Ordinal);
        return parenIndex > 0 ? moveText[..parenIndex].Trim() : moveText;
    }

    private static IReadOnlyList<string> BuildFixFirstItems(PlayerProfileReport report)
    {
        List<string> items = [];

        if (report.Recommendations.Count > 0)
        {
            TrainingRecommendation primary = report.Recommendations[0];
            TryAddFixFirst(items, primary.Checklist, 0);
            TryAddFixFirst(items, primary.Checklist, 1);
        }

        if (report.Recommendations.Count > 1)
        {
            TryAddFixFirst(items, report.Recommendations[1].Checklist, 0);
        }

        if (items.Count < 3 && report.MistakesByOpening.Count > 0)
        {
            string opening = FormatOpening(report.MistakesByOpening[0].Eco);
            items.Add($"Review two recent positions from {opening} where this pattern showed up.");
        }

        if (items.Count < 3 && report.MistakesByPhase.Count > 0)
        {
            string phase = FormatPhase(report.MistakesByPhase[0].Phase).ToLowerInvariant();
            items.Add($"Slow down in the {phase} and do a full safety check before moving.");
        }

        if (items.Count == 0)
        {
            items.Add("Pause at every big evaluation swing and ask what had to be checked first.");
            items.Add("Review two recent mistakes from your own games before the next training session.");
        }

        return items.Take(3).ToList();
    }

    private static void TryAddFixFirst(List<string> items, IReadOnlyList<string> checklist, int index)
    {
        if (checklist.Count <= index)
        {
            return;
        }

        string action = TrimSentence(checklist[index]);
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (items.Any(existing => string.Equals(existing, action, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        items.Add(action + ".");
    }

    private static string BuildRoleLabel(TrainingPlanTopicCategory category)
    {
        return category switch
        {
            TrainingPlanTopicCategory.CoreWeakness => "Core weakness",
            TrainingPlanTopicCategory.SecondaryWeakness => "Secondary weakness",
            TrainingPlanTopicCategory.MaintenanceTopic => "Maintenance topic",
            _ => "Training topic"
        };
    }

    private static Control CreateInsightCard(string label, string value, string? detail = null)
    {
        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        StackPanel panel = new() { Spacing = 4 };
        panel.Children.Add(CreateBodyText(label, "#9EB5C5"));
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(CreateBodyText(detail, "#D7E2EA"));
        }

        card.Child = panel;
        return card;
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
            _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((label ?? string.Empty).Replace('_', ' ').ToLowerInvariant())
        };
    }

    private static string FormatTrendHeadline(ProfileProgressDirection direction)
    {
        return direction switch
        {
            ProfileProgressDirection.Improving => "Improving lately",
            ProfileProgressDirection.Stable => "Mostly stable",
            ProfileProgressDirection.Regressing => "Results slipped recently",
            _ => "Need more games"
        };
    }

    private static string FormatTimes(int count) => count == 1 ? "1 time" : $"{count} times";

    private static string FormatChartDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
    }

    private static string FormatOpening(string eco)
    {
        string description = OpeningCatalog.Describe(eco);
        return string.IsNullOrWhiteSpace(description) ? "Mixed openings" : description;
    }

    private static string FormatPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Opening => "Opening",
            GamePhase.Middlegame => "Middlegame",
            GamePhase.Endgame => "Endgame",
            _ => phase.ToString()
        };
    }

    private static string FormatOpeningFrequency(int count, int total)
    {
        if (total <= 0)
        {
            return $"{count} games";
        }

        double percentage = (double)count / total * 100.0;
        return $"{count}/{total} games ({percentage:0.#}%)";
    }

    private static string FormatPlyLabel(PlayerSide side, int? ply, string? san)
    {
        if (!ply.HasValue)
        {
            return string.IsNullOrWhiteSpace(san) ? "Unknown move" : san!;
        }

        int moveNumber = (ply.Value + 1) / 2;
        string prefix = ply.Value % 2 == 1 ? $"{moveNumber}." : $"{moveNumber}...";
        return string.IsNullOrWhiteSpace(san) ? prefix : $"{prefix} {san}";
    }

    private static string TrimSentence(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Trim().TrimEnd('.', ';', ':', '!');
    }

    private static string FormatTrainingBlockKind(TrainingBlockKind kind)
    {
        return kind switch
        {
            TrainingBlockKind.Tactics => "Tactics",
            TrainingBlockKind.OpeningReview => "Opening review",
            TrainingBlockKind.EndgameDrill => "Endgame drill",
            TrainingBlockKind.GameReview => "Game review",
            TrainingBlockKind.SlowPlayFocus => "Slow play focus",
            _ => kind.ToString()
        };
    }

    private static string FormatTrainingBlockPurpose(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => "Repair",
            TrainingBlockPurpose.Maintain => "Maintain",
            TrainingBlockPurpose.Checklist => "Checklist",
            _ => purpose.ToString()
        };
    }

    private static string FormatOpeningWeaknessCategory(OpeningWeaknessCategory category)
    {
        return category switch
        {
            OpeningWeaknessCategory.FixNow => "Opening to fix now",
            OpeningWeaknessCategory.ReviewLater => "Opening to review later",
            OpeningWeaknessCategory.Stable => "Opening stable",
            _ => category.ToString()
        };
    }

    private static int GetBlockPurposeOrder(TrainingBlockPurpose purpose)
    {
        return purpose switch
        {
            TrainingBlockPurpose.Repair => 0,
            TrainingBlockPurpose.Maintain => 1,
            TrainingBlockPurpose.Checklist => 2,
            _ => 3
        };
    }

    private static string BuildTopicContext(TrainingPlanTopic topic)
    {
        List<string> parts = [];

        if (topic.EmphasisPhase.HasValue)
        {
            parts.Add(FormatPhase(topic.EmphasisPhase.Value));
        }

        if (topic.EmphasisSide.HasValue)
        {
            parts.Add(topic.EmphasisSide.Value == PlayerSide.White ? "Mostly as White" : "Mostly as Black");
        }

        if (topic.RelatedOpenings.Count > 0)
        {
            parts.Add(string.Join(" / ", topic.RelatedOpenings.Take(2).Select(FormatOpening)));
        }

        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    private static IReadOnlyList<ProfileMistakeExample> BuildDeepDiveExamples(PlayerProfileReport report)
    {
        if (report.MistakeExamples.Count == 0)
        {
            return [];
        }

        List<ProfileMistakeExample> selected = [];
        IEnumerable<string> dominantLabels = report.TopMistakeLabels
            .Select(item => item.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Take(3);

        foreach (string label in dominantLabels)
        {
            List<ProfileMistakeExample> examplesForLabel = report.MistakeExamples
                .Where(example => string.Equals(example.Label, label, StringComparison.OrdinalIgnoreCase))
                .OrderBy(example => GetExampleRankOrder(example.Rank))
                .ThenByDescending(example => example.CentipawnLoss ?? 0)
                .Take(3)
                .ToList();

            selected.AddRange(examplesForLabel);
        }

        if (selected.Count == 0)
        {
            selected.AddRange(report.MistakeExamples
                .OrderBy(example => GetExampleRankOrder(example.Rank))
                .ThenByDescending(example => example.CentipawnLoss ?? 0)
                .Take(6));
        }

        return selected
            .GroupBy(example => $"{example.GameFingerprint}|{example.Ply}|{example.Rank}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static int GetExampleRankOrder(ProfileMistakeExampleRank rank)
    {
        return rank switch
        {
            ProfileMistakeExampleRank.MostFrequent => 0,
            ProfileMistakeExampleRank.MostCostly => 1,
            ProfileMistakeExampleRank.MostRepresentative => 2,
            _ => 3
        };
    }

    private static string FormatExampleRank(ProfileMistakeExampleRank rank)
    {
        return rank switch
        {
            ProfileMistakeExampleRank.MostFrequent => "Most frequent",
            ProfileMistakeExampleRank.MostCostly => "Most costly",
            ProfileMistakeExampleRank.MostRepresentative => "Most representative",
            _ => rank.ToString()
        };
    }

    private static string BuildSnapshotSummary(PlayerProfileReport report)
    {
        string cpl = report.AverageCentipawnLoss?.ToString() ?? "n/a";
        return $"Across {report.GamesAnalyzed} games, the player averages CPL {cpl} with {report.HighlightedMistakes} highlighted mistakes.";
    }

    private bool IsClosed()
    {
        return VisualRoot is null || !IsVisible;
    }
}
