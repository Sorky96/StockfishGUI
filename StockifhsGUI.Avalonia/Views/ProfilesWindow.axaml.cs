using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using StockifhsGUI.Avalonia.Controls;
using StockifhsGUI.Avalonia.ViewModels;

namespace StockifhsGUI.Avalonia.Views;

public partial class ProfilesWindow : Window
{
    private readonly PlayerProfileService profileService;
    private readonly Func<ProfileMistakeExample, Task>? navigateToProfileExampleAsync;
    private readonly Func<OpeningExampleGame, Task>? navigateToOpeningExampleAsync;
    private readonly Func<OpeningMoveRecommendation, Task>? navigateToOpeningPositionAsync;
    private List<PlayerProfileSummaryItemViewModel> items = [];
    private OpeningTrainingSession? currentOpeningTrainingSession;
    private string? currentProfilePlayerKey;

    public ProfilesWindow()
        : this(new PlayerProfileService(AnalysisStoreProvider.GetStore() ?? throw new InvalidOperationException("Local analysis store is unavailable.")))
    {
    }

    public ProfilesWindow(
        PlayerProfileService profileService,
        Func<ProfileMistakeExample, Task>? navigateToProfileExampleAsync = null,
        Func<OpeningExampleGame, Task>? navigateToOpeningExampleAsync = null,
        Func<OpeningMoveRecommendation, Task>? navigateToOpeningPositionAsync = null)
    {
        this.profileService = profileService;
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

    private void ProfilesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfilesListBox.SelectedItem is not PlayerProfileSummaryItemViewModel item)
        {
            ShowStatus("Select a player to inspect the profile.");
            return;
        }

        if (!profileService.TryBuildProfile(item.Summary.PlayerKey, out PlayerProfileReport? report) || report is null)
        {
            ShowStatus("Could not load the selected player profile.");
            return;
        }

        profileService.TryBuildOpeningWeaknessReport(item.Summary.PlayerKey, out OpeningWeaknessReport? openingReport);
        profileService.TryBuildOpeningTrainingSession(item.Summary.PlayerKey, out currentOpeningTrainingSession, new OpeningTrainingSessionOptions(
            Modes: [OpeningTrainingMode.BranchAwareness],
            Sources: [OpeningTrainingSourceKind.OpeningWeakness],
            MaxPositions: 12,
            MaxPositionsPerSource: 12,
            MaxContinuationMoves: 4));
        RenderProfile(report, openingReport);
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
            ShowStatus("No analyzed players match the current filter.");
        }
    }

    private void ShowStatus(string text)
    {
        DetailsPanel.Children.Clear();
        DetailsPanel.Children.Add(CreateSectionCard(
            "Profile Details",
            [
                CreateBodyText(text)
            ]));
    }

    private void RenderProfile(PlayerProfileReport report, OpeningWeaknessReport? openingReport)
    {
        DetailsPanel.Children.Clear();
        currentProfilePlayerKey = report.PlayerKey;

        DetailsPanel.Children.Add(CreateHeroCard(report));
        DetailsPanel.Children.Add(CreateSnapshotCard(report));
        DetailsPanel.Children.Add(CreateMetricsCard(report));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Profile summary", BuildProfileSummaryRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateDetailedWindowsSection(report, openingReport));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Opening weaknesses", BuildOpeningWeaknessRows(openingReport), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("What to fix first", BuildFixFirstRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("What to work on", BuildWorkOnRows(report), isExpanded: true));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Recent trend", BuildRecentTrendRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Deep dive", BuildDeepDiveRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Top mistake labels", BuildTopLabelRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Costliest patterns", BuildCostliestRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Trend", BuildTrendRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Recommendations", BuildRecommendationRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Weekly plan", BuildWeeklyPlanRows(report)));
        DetailsPanel.Children.Add(CreateCollapsibleSection("Examples", BuildExampleRows(report)));
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

        if (report.GamesBySide.Count > 0)
        {
            string sides = string.Join(" | ", report.GamesBySide.Select(side =>
                $"{(side.Side == PlayerSide.White ? "White" : "Black")}: {side.GamesAnalyzed} games, {side.HighlightedMistakes} mistakes"));
            wrap.Children.Add(CreateMetricTile("By side", sides, 440));
        }

        card.Child = wrap;
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

    private IEnumerable<Control> BuildProfileSummaryRows(PlayerProfileReport report)
    {
        foreach (string line in BuildSummaryLines(report))
        {
            yield return CreateBulletText(line);
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
            yield return CreateBulletText($"Recent sample: {FormatPeriod(report.ProgressSignal.Recent)}");
        }

        if (report.ProgressSignal.Previous is not null)
        {
            yield return CreateBulletText($"Earlier sample: {FormatPeriod(report.ProgressSignal.Previous)}");
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
            yield return CreateBodyText("Deep dive becomes available once more analyzed patterns accumulate.");
            yield break;
        }

        yield return CreateBodyText("Detailed diagnosis behind the training plan.", "#D7E2EA");
        yield return CreateBodyText("Recurring patterns", "#9EB5C5");
        foreach (ProfileLabelStat item in report.TopMistakeLabels.Take(5))
        {
            yield return CreateBulletText($"{FormatMistakeLabel(item.Label)}: {FormatTimes(item.Count)} in highlighted mistakes");
        }

        yield return CreateBodyText("Costliest patterns", "#9EB5C5");
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

    private IEnumerable<Control> BuildTrendRows(PlayerProfileReport report)
    {
        yield return CreateBodyText($"{report.ProgressSignal.Direction}: {report.ProgressSignal.Summary}");

        if (report.ProgressSignal.Recent is not null)
        {
            yield return CreateBulletText($"Recent: {FormatPeriod(report.ProgressSignal.Recent)}");
        }

        if (report.ProgressSignal.Previous is not null)
        {
            yield return CreateBulletText($"Previous: {FormatPeriod(report.ProgressSignal.Previous)}");
        }

        foreach (ProfileLabelTrend trend in report.LabelTrends.Take(5))
        {
            yield return CreateBulletText($"{trend.Label}: {trend.Direction}, recent {trend.RecentCount}, previous {trend.PreviousCount}");
        }
    }

    private IEnumerable<Control> BuildRecommendationRows(PlayerProfileReport report)
    {
        if (report.Recommendations.Count == 0)
        {
            yield return CreateBodyText("No recommendations available.");
            yield break;
        }

        foreach (TrainingRecommendation recommendation in report.Recommendations.Take(4))
        {
            Border innerCard = new()
            {
                Background = Brush.Parse("#203542"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            StackPanel panel = new() { Spacing = 6 };
            panel.Children.Add(new TextBlock
            {
                Text = recommendation.Title,
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(CreateBodyText(recommendation.Description, "#D7E2EA"));

            foreach (string item in recommendation.Checklist.Take(3))
            {
                panel.Children.Add(CreateBulletText(item));
            }

            innerCard.Child = panel;
            yield return innerCard;
        }
    }

    private IEnumerable<Control> BuildWeeklyPlanRows(PlayerProfileReport report)
    {
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
        StackPanel panel = CreateCardPanel();
        foreach (Control child in children)
        {
            panel.Children.Add(child);
        }

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

    private Control CreateActionRow(PlayerProfileReport report, OpeningWeaknessReport? openingReport)
    {
        Border card = CreateCardBorder();
        WrapPanel panel = new()
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 220
        };

        if (openingReport is not null && openingReport.WeakOpenings.Count > 0)
        {
            panel.Children.Add(CreateSectionButton("Opening weaknesses", () => OpenSectionWindow("Opening weaknesses", BuildOpeningWeaknessRows(openingReport))));
        }

        panel.Children.Add(CreateSectionButton("What to work on", () => OpenSectionWindow("What to work on", BuildWorkOnRows(report))));
        panel.Children.Add(CreateSectionButton("Deep dive", () => OpenSectionWindow("Deep dive", BuildDeepDiveRows(report))));
        panel.Children.Add(CreateSectionButton("Training plan", () => OpenSectionWindow("Training plan", BuildWeeklyPlanRows(report))));

        card.Child = panel;
        return card;
    }

    private Control CreateDetailedWindowsSection(PlayerProfileReport report, OpeningWeaknessReport? openingReport)
    {
        return CreateCollapsibleSection(
            "Detailed windows",
            BuildDetailedWindowRows(report, openingReport),
            isExpanded: true);
    }

    private IEnumerable<Control> BuildDetailedWindowRows(PlayerProfileReport report, OpeningWeaknessReport? openingReport)
    {
        yield return CreateBodyText("Open heavier sections in separate windows to keep the main profile view compact.", "#D7E2EA");
        yield return CreateWindowDescriptionCard(
            "Training plan",
            "Full weekly plan with priorities, topic breakdown and daily sessions.",
            () => OpenSectionWindow("Training plan", BuildWeeklyPlanRows(report)));
        yield return CreateWindowDescriptionCard(
            "What to work on",
            "Recommendations with example positions for each work item.",
            () => OpenSectionWindow("What to work on", BuildWorkOnRows(report)));
        yield return CreateWindowDescriptionCard(
            "Deep dive",
            "Key mistakes, costly patterns and grouped example positions by motif.",
            () => OpenSectionWindow("Deep dive", BuildDeepDiveRows(report)));
        if (openingReport is not null)
        {
            yield return CreateWindowDescriptionCard(
                "Opening weaknesses",
                "Recurring opening mistakes, example games and better-move positions.",
                () => OpenSectionWindow("Opening weaknesses", BuildOpeningWeaknessRows(openingReport)));
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

    private static Control CreateWindowDescriptionCard(string title, string description, Action onClick)
    {
        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        StackPanel panel = new() { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(description, "#D7E2EA"));
        panel.Children.Add(CreateSectionButton($"Open {title}", onClick));

        card.Child = panel;
        return card;
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
        IReadOnlyList<OpeningTrainingPosition> openingBranches = currentOpeningTrainingSession?.Positions
            .Where(position => position.Mode == OpeningTrainingMode.BranchAwareness
                && string.Equals(position.Eco, opening.Eco, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(position => position.Priority)
            .ToList()
            ?? [];
        Button branchButton = new()
        {
            Content = openingBranches.Count == 0 ? "Branch awareness unavailable" : "Open branch awareness",
            IsEnabled = openingBranches.Count > 0,
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 220
        };
        branchButton.Click += async (_, _) =>
        {
            branchButton.IsEnabled = false;
            try
            {
                await ShowBranchAwarenessWindowAsync(opening, openingBranches);
            }
            finally
            {
                if (!IsClosed())
                {
                    branchButton.IsEnabled = openingBranches.Count > 0;
                }
            }
        };
        actions.Children.Add(branchButton);

        Button trainingButton = new()
        {
            Content = "Start opening training",
            IsEnabled = !string.IsNullOrWhiteSpace(currentProfilePlayerKey),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 220
        };
        trainingButton.Click += async (_, _) =>
        {
            trainingButton.IsEnabled = false;
            try
            {
                await ShowOpeningTrainingWindowAsync(opening);
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
        actions.Children.Add(CreateBodyText(
            openingBranches.Count == 0
                ? "No stable local branch sample yet for this opening."
                : "Shows the most common local opponent replies and one recommended local reaction.",
            "#9EB5C5"));
        panel.Children.Add(actions);

        card.Child = panel;
        return card;
    }

    private async Task ShowOpeningTrainingWindowAsync(OpeningWeaknessEntry opening)
    {
        if (string.IsNullOrWhiteSpace(currentProfilePlayerKey))
        {
            return;
        }

        OpeningTrainingSessionOptions options = new(
            TargetOpenings: [opening.Eco],
            MaxPositions: 12,
            MaxPositionsPerSource: 6,
            MaxContinuationMoves: 4);

        if (!profileService.TryBuildOpeningTrainingSession(currentProfilePlayerKey, out OpeningTrainingSession? session, options)
            || session is null
            || session.Positions.Count == 0)
        {
            OpenSectionWindow(
                $"Opening training - {opening.OpeningDisplayName}",
                [
                    CreateBodyText("No local training positions are available yet for this opening.", "#D7E2EA")
                ]);
            return;
        }

        Window window = new()
        {
            Title = $"Opening training - {opening.OpeningDisplayName}",
            Width = 1160,
            Height = 840,
            MinWidth = 900,
            MinHeight = 620,
            Background = Brush.Parse("#23313B")
        };

        StackPanel content = new()
        {
            Spacing = 12
        };

        content.Children.Add(CreateSectionCard(
            "Opening training",
            [
                CreateBodyText($"{opening.OpeningDisplayName} | {FormatOpeningWeaknessCategory(opening.Category)}", "#D7E2EA"),
                CreateBodyText("This session is filtered to the selected opening and built only from stored profile weaknesses and local analysis.", "#D7E2EA")
            ]));

        foreach (OpeningTrainingPosition position in session.Positions)
        {
            content.Children.Add(CreateOpeningTrainingPositionCard(position));
        }

        window.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                Content = content
            }
        };

        await window.ShowDialog(this);
    }

    private Control CreateOpeningTrainingPositionCard(OpeningTrainingPosition position)
    {
        if (position.Mode == OpeningTrainingMode.BranchAwareness)
        {
            return CreateBranchAwarenessCard(position);
        }

        Border card = new()
        {
            Background = Brush.Parse("#182B37"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("120,*")
        };

        Border boardHost = new()
        {
            Width = 120,
            Height = 120,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };
        boardHost.Child = new ChessBoardView
        {
            Width = 120,
            Height = 120,
            Fen = position.Fen,
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
            Text = $"{FormatOpeningTrainingMode(position.Mode)} | {position.Prompt}",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(position.Instruction, "#D7E2EA"));

        if (!string.IsNullOrWhiteSpace(position.PlayedMove))
        {
            panel.Children.Add(CreateBodyText($"Played in game: {position.PlayedMove}", "#9EB5C5"));
        }

        if (!string.IsNullOrWhiteSpace(position.BetterMove))
        {
            panel.Children.Add(CreateBodyText($"Target repair: {position.BetterMove}", "#D7E2EA"));
        }

        IReadOnlyList<string> candidateMoves = position.CandidateMoves
            .OrderByDescending(move => move.IsPreferred)
            .ThenBy(move => move.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(move => move.IsPreferred ? $"{move.DisplayText} (preferred)" : move.DisplayText)
            .ToList();
        if (candidateMoves.Count > 0)
        {
            panel.Children.Add(CreateBodyText($"Local references: {string.Join(", ", candidateMoves)}", "#9EB5C5"));
        }

        if (position.Continuation.Count > 0)
        {
            panel.Children.Add(CreateBodyText($"Continuation: {string.Join(" -> ", position.Continuation.Select(move => move.San))}", "#9EB5C5"));
        }

        grid.Children.Add(panel);
        card.Child = grid;
        return card;
    }

    private async Task ShowBranchAwarenessWindowAsync(OpeningWeaknessEntry opening, IReadOnlyList<OpeningTrainingPosition> positions)
    {
        Window window = new()
        {
            Title = $"Branch awareness - {opening.OpeningDisplayName}",
            Width = 1120,
            Height = 820,
            MinWidth = 900,
            MinHeight = 620,
            Background = Brush.Parse("#23313B")
        };

        StackPanel content = new()
        {
            Spacing = 12
        };

        content.Children.Add(CreateSectionCard(
            "Branch awareness",
            [
                CreateBodyText($"{opening.OpeningDisplayName} | {opening.Eco}", "#D7E2EA"),
                CreateBodyText("The branches below come only from local example games, recurring mistake patterns, and saved continuations.", "#D7E2EA")
            ]));

        foreach (OpeningTrainingPosition position in positions)
        {
            content.Children.Add(CreateBranchAwarenessCard(position));
        }

        window.Content = new Border
        {
            Padding = new Thickness(18),
            Child = new ScrollViewer
            {
                Content = content
            }
        };

        await window.ShowDialog(this);
    }

    private Control CreateBranchAwarenessCard(OpeningTrainingPosition position)
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
            ColumnDefinitions = new ColumnDefinitions("170,*")
        };

        Border boardHost = new()
        {
            Width = 160,
            Height = 160,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };
        boardHost.Child = new ChessBoardView
        {
            Width = 160,
            Height = 160,
            Fen = position.Fen,
            IsHitTestVisible = false
        };
        grid.Children.Add(boardHost);

        StackPanel panel = new()
        {
            Margin = new Thickness(16, 0, 0, 0),
            Spacing = 6
        };
        Grid.SetColumn(panel, 1);

        panel.Children.Add(new TextBlock
        {
            Text = $"{position.Prompt}",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText(position.Instruction, "#D7E2EA"));
        if (!string.IsNullOrWhiteSpace(position.BranchSelectionSummary))
        {
            panel.Children.Add(CreateBodyText(position.BranchSelectionSummary, "#9EB5C5"));
        }

        foreach (OpeningTrainingBranch branch in position.Branches ?? [])
        {
            Border branchCard = new()
            {
                Background = Brush.Parse("#203542"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 4)
            };

            StackPanel branchPanel = new() { Spacing = 5 };
            branchPanel.Children.Add(new TextBlock
            {
                Text = $"Opponent reply: {branch.OpponentMove} | seen {branch.Frequency} time(s)",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            branchPanel.Children.Add(CreateBodyText(branch.SourceSummary, "#D7E2EA"));
            branchPanel.Children.Add(CreateBodyText(
                branch.RecommendedResponse is null
                    ? "Recommended reaction: no stable local response saved yet."
                    : $"Recommended reaction: {branch.RecommendedResponse.DisplayText}",
                "#D7E2EA"));

            if (!string.IsNullOrWhiteSpace(branch.RecommendedResponse?.Note))
            {
                branchPanel.Children.Add(CreateBodyText(branch.RecommendedResponse.Note, "#9EB5C5"));
            }

            if (branch.Continuation.Count > 0)
            {
                string lineText = string.Join(" -> ", branch.Continuation.Select(move => move.San));
                branchPanel.Children.Add(CreateBodyText($"Sample continuation: {lineText}", "#9EB5C5"));
            }

            branchCard.Child = branchPanel;
            panel.Children.Add(branchCard);
        }

        grid.Children.Add(panel);
        card.Child = grid;
        return card;
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

        Border boardHost = new()
        {
            Width = 120,
            Height = 120,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true
        };
        boardHost.Child = new ChessBoardView
        {
            Width = 120,
            Height = 120,
            Fen = recommendation.FenBefore,
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
            Text = FormatPlyLabel(recommendation.Side, recommendation.Ply, recommendation.PlayedSan),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateBodyText($"Better move: {recommendation.BetterMove}", "#D7E2EA"));
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

    private static string FormatOpeningTrainingMode(OpeningTrainingMode mode)
    {
        return mode switch
        {
            OpeningTrainingMode.LineRecall => "Line recall",
            OpeningTrainingMode.MistakeRepair => "Mistake repair",
            OpeningTrainingMode.BranchAwareness => "Branch awareness",
            _ => mode.ToString()
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

    private static IReadOnlyList<string> BuildSummaryLines(PlayerProfileReport report)
    {
        List<string> lines = [];

        if (report.TopMistakeLabels.Count > 0)
        {
            lines.Add($"Biggest problem: {FormatMistakeLabel(report.TopMistakeLabels[0].Label)} ({FormatTimes(report.TopMistakeLabels[0].Count)}).");
        }

        if (report.TopMistakeLabels.Count > 1)
        {
            lines.Add($"Second problem: {FormatMistakeLabel(report.TopMistakeLabels[1].Label)} ({FormatTimes(report.TopMistakeLabels[1].Count)}).");
        }

        if (report.TrainingPlan.Topics.Count > 0)
        {
            TrainingPlanTopic topic = report.TrainingPlan.Topics
                .FirstOrDefault(item => item.Category == TrainingPlanTopicCategory.CoreWeakness)
                ?? report.TrainingPlan.Topics[0];
            lines.Add($"Training priority: {topic.Title} ({topic.FocusArea}).");
        }

        if (report.MistakesByPhase.Count > 0)
        {
            lines.Add($"Weakest phase: {FormatPhase(report.MistakesByPhase[0].Phase)} ({report.MistakesByPhase[0].Count} highlighted mistakes).");
        }

        if (report.MistakesByOpening.Count > 0)
        {
            lines.Add($"Most problematic opening: {FormatOpening(report.MistakesByOpening[0].Eco)} ({report.MistakesByOpening[0].Count} highlighted mistakes).");
        }

        lines.Add($"Recent trend: {FormatTrendHeadline(report.ProgressSignal.Direction)}.");
        return lines;
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
