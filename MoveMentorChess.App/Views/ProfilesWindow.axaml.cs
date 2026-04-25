using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MoveMentorChess.App.Controls;
using MoveMentorChess.App.ViewModels;

namespace MoveMentorChess.App.Views;

public partial class ProfilesWindow : Window
{
    private readonly PlayerProfileService profileService;
    private readonly Func<ProfileMistakeExample, Task>? navigateToProfileExampleAsync;
    private readonly Func<OpeningExampleGame, Task>? navigateToOpeningExampleAsync;
    private readonly Func<OpeningMoveRecommendation, Task>? navigateToOpeningPositionAsync;
    private List<PlayerProfileSummaryItemViewModel> items = [];
    private string? currentProfilePlayerKey;
    private readonly Dictionary<string, IReadOnlyList<OpeningTrainingPosition>> branchAwarenessPositionsByEco = new(StringComparer.OrdinalIgnoreCase);

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
            ShowStatus(BuildEmptyStateMessage());
        }
    }

    private string BuildEmptyStateMessage()
    {
        ProfileDataAvailability availability = profileService.GetDataAvailability(FilterTextBox.Text);
        ProfileDataAvailability totalAvailability = profileService.GetDataAvailability();
        if (availability.AnalyzedProfiles > 0)
        {
            return "No analyzed players match the current filter.";
        }

        if (!string.IsNullOrWhiteSpace(FilterTextBox.Text) && totalAvailability.AnalyzedProfiles > 0)
        {
            return "No analyzed players match the current filter.";
        }

        if (totalAvailability.ImportedGames > 0 || totalAvailability.OpeningTreePositions > 0)
        {
            return "Imported opening data exists, but player profiles are built only from analyzed games. Load one of the saved games and run analysis first, then reopen Profiles.";
        }

        return "No analyzed players match the current filter.";
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
        branchAwarenessPositionsByEco.Clear();

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
        Button branchButton = new()
        {
            Content = "Open branch awareness",
            IsEnabled = !string.IsNullOrWhiteSpace(currentProfilePlayerKey),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 220
        };
        branchButton.Click += async (_, _) =>
        {
            branchButton.IsEnabled = false;
            try
            {
                IReadOnlyList<OpeningTrainingPosition> positions = GetBranchAwarenessPositionsForOpening(opening.Eco);
                if (positions.Count == 0)
                {
                    OpenSectionWindow(
                        "Branch awareness",
                        [
                            CreateBodyText($"{opening.OpeningDisplayName} | {opening.Eco}", "#D7E2EA"),
                            CreateBodyText("No imported-theory branch entries are available yet for this opening at the positions highlighted by the profile.", "#D7E2EA")
                        ]);
                    return;
                }

                await ShowBranchAwarenessWindowAsync(opening, positions);
            }
            finally
            {
                if (!IsClosed())
                {
                    branchButton.IsEnabled = !string.IsNullOrWhiteSpace(currentProfilePlayerKey);
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
            "Branch awareness checks imported opening theory for the highlighted positions in this opening.",
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
                    CreateBodyText("No imported-theory training positions are available yet for this opening.", "#D7E2EA")
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
                CreateBodyText("This session is filtered to the selected opening. Branches and book moves come from the imported opening database, while the profile weakness only decides which positions to review.", "#D7E2EA")
            ]));

        foreach (OpeningTrainingPosition position in session.Positions)
        {
            content.Children.Add(CreateOpeningTrainingPositionCard(position, window));
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

    private Control CreateOpeningTrainingPositionCard(OpeningTrainingPosition position, Window? ownerWindowToClose = null)
    {
        if (position.Mode == OpeningTrainingMode.BranchAwareness)
        {
            return CreateBranchAwarenessCard(position, ownerWindowToClose);
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

        IReadOnlyList<BoardArrowViewModel> arrows = BuildPreviewArrows(
            position.Fen,
            (position.PlayedMove, Color.Parse("#F6C453")),
            (GetPreferredTheoryMove(position), Color.Parse("#58D68D")));
        Control boardHost = CreateBoardPreview(
            position.Fen,
            120,
            arrows,
            async () => await ShowBoardPreviewWindowAsync(
                title: $"{FormatOpeningTrainingMode(position.Mode)} | {position.OpeningName}",
                fen: position.Fen,
                arrows: arrows,
                detailLines: BuildTrainingPreviewDetailLines(position)));
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
            panel.Children.Add(CreateBodyText($"Your move: {position.PlayedMove}", "#9EB5C5"));
        }

        string? preferredTheoryMove = GetPreferredTheoryMove(position);
        if (!string.IsNullOrWhiteSpace(preferredTheoryMove))
        {
            panel.Children.Add(CreateBodyText($"Best theory move: {preferredTheoryMove}", "#D7E2EA"));
        }

        IReadOnlyList<string> candidateMoves = GetImportedTheoryOptions(position)
            .OrderByDescending(move => move.IsPreferred)
            .ThenBy(move => move.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(move => move.IsPreferred ? $"{move.DisplayText} (preferred)" : move.DisplayText)
            .ToList();
        if (candidateMoves.Count > 0)
        {
            panel.Children.Add(CreateBodyText($"Imported theory options: {string.Join(", ", candidateMoves)}", "#9EB5C5"));
        }

        if (position.Continuation.Count > 0)
        {
            panel.Children.Add(CreateBodyText($"Continuation: {string.Join(" -> ", position.Continuation.Select(move => move.San))}", "#9EB5C5"));
        }

        panel.Children.Add(CreateTrainingPositionActionButton(position, ownerWindowToClose));

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
                CreateBodyText("The branches below come from the imported opening database for the highlighted profile positions in this opening.", "#D7E2EA")
            ]));

        foreach (OpeningTrainingPosition position in positions)
        {
            content.Children.Add(CreateBranchAwarenessCard(position, window));
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

    private Control CreateBranchAwarenessCard(OpeningTrainingPosition position, Window? ownerWindowToClose = null)
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

        Control boardHost = CreateBoardPreview(
            position.Fen,
            160,
            Array.Empty<BoardArrowViewModel>(),
            async () => await ShowBoardPreviewWindowAsync(
                title: $"Branch Awareness | {position.OpeningName}",
                fen: position.Fen,
                arrows: Array.Empty<BoardArrowViewModel>(),
                detailLines: BuildBranchPreviewDetailLines(position)));
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

        panel.Children.Add(CreateTrainingPositionActionButton(position, ownerWindowToClose));
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

    private async Task NavigateToTrainingPositionAsync(OpeningTrainingPosition position, Window? ownerWindowToClose = null)
    {
        if (navigateToOpeningPositionAsync is null)
        {
            return;
        }

        OpeningMoveRecommendation recommendation = new(
            position.Reference.GameFingerprint,
            position.Reference.Side,
            position.Eco,
            position.Ply,
            position.MoveNumber,
            position.PlayedMove ?? "?",
            GetPreferredTheoryMove(position) ?? position.BetterMove ?? "?",
            position.ThemeLabel,
            null,
            position.Fen);

        await navigateToOpeningPositionAsync(recommendation);
        ownerWindowToClose?.Close();
        Close();
    }

    private IReadOnlyList<OpeningTrainingPosition> GetBranchAwarenessPositionsForOpening(string eco)
    {
        if (string.IsNullOrWhiteSpace(currentProfilePlayerKey) || string.IsNullOrWhiteSpace(eco))
        {
            return [];
        }

        if (branchAwarenessPositionsByEco.TryGetValue(eco, out IReadOnlyList<OpeningTrainingPosition>? cached))
        {
            return cached;
        }

        OpeningTrainingSessionOptions options = new(
            Modes: [OpeningTrainingMode.BranchAwareness],
            Sources: [OpeningTrainingSourceKind.OpeningWeakness],
            MaxPositions: 24,
            MaxPositionsPerSource: 24,
            MaxContinuationMoves: 4,
            TargetOpenings: [eco]);

        if (!profileService.TryBuildOpeningTrainingSession(currentProfilePlayerKey, out OpeningTrainingSession? session, options)
            || session is null)
        {
            branchAwarenessPositionsByEco[eco] = [];
            return [];
        }

        IReadOnlyList<OpeningTrainingPosition> positions = session.Positions
            .Where(position => position.Mode == OpeningTrainingMode.BranchAwareness
                && string.Equals(position.Eco, eco, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(position => position.Priority)
            .ToList();
        branchAwarenessPositionsByEco[eco] = positions;
        return positions;
    }

    private static string? GetPreferredTheoryMove(OpeningTrainingPosition position)
    {
        return position.CandidateMoves
            .FirstOrDefault(move => move.IsPreferred && move.ReferenceKind != OpeningLineRecallReferenceKind.HistoricalGame)?.DisplayText
            ?? position.BetterMove;
    }

    private static IReadOnlyList<OpeningTrainingMoveOption> GetImportedTheoryOptions(OpeningTrainingPosition position)
    {
        return position.CandidateMoves
            .Where(move => move.ReferenceKind != OpeningLineRecallReferenceKind.HistoricalGame)
            .ToList();
    }

    private Button CreateTrainingPositionActionButton(OpeningTrainingPosition position, Window? ownerWindowToClose)
    {
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
                await NavigateToTrainingPositionAsync(position, ownerWindowToClose);
            }
            finally
            {
                if (!IsClosed())
                {
                    button.IsEnabled = true;
                }
            }
        };
        return button;
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

    private static IReadOnlyList<string> BuildTrainingPreviewDetailLines(OpeningTrainingPosition position)
    {
        List<string> lines = [];
        lines.Add(position.Prompt);
        lines.Add(position.Instruction);

        if (!string.IsNullOrWhiteSpace(position.PlayedMove))
        {
            lines.Add($"Your move: {position.PlayedMove}");
        }

        string? preferredTheoryMove = GetPreferredTheoryMove(position);
        if (!string.IsNullOrWhiteSpace(preferredTheoryMove))
        {
            lines.Add($"Best theory move: {preferredTheoryMove}");
        }

        if (position.Continuation.Count > 0)
        {
            lines.Add($"Line: {string.Join(" -> ", position.Continuation.Select(move => move.San))}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildBranchPreviewDetailLines(OpeningTrainingPosition position)
    {
        List<string> lines = [];
        lines.Add(position.Prompt);
        lines.Add(position.Instruction);

        if (!string.IsNullOrWhiteSpace(position.BranchSelectionSummary))
        {
            lines.Add(position.BranchSelectionSummary);
        }

        foreach (OpeningTrainingBranch branch in position.Branches ?? [])
        {
            lines.Add($"Opponent reply: {branch.OpponentMove} | seen {branch.Frequency} time(s)");
            if (!string.IsNullOrWhiteSpace(branch.RecommendedResponse?.DisplayText))
            {
                lines.Add($"Best theory response: {branch.RecommendedResponse.DisplayText}");
            }

            if (branch.Continuation.Count > 0)
            {
                lines.Add($"Sample line: {string.Join(" -> ", branch.Continuation.Select(move => move.San))}");
            }
        }

        return lines;
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
