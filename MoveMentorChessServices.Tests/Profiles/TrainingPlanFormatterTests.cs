using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class TrainingPlanFormatterTests
{
    [Fact]
    public void PromptFormatter_BuildsStructuredInputAndRequiredOutputKeys()
    {
        TrainingPlanReport report = CreateReport();

        string prompt = TrainingPlanLlmPromptFormatter.BuildPrompt(
            report,
            PlayerProfileAudienceLevel.Beginner,
            AdviceNarrationStyle.BotezLive);

        Assert.Contains("\"player\": \"Alpha\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"audience_level\": \"Beginner\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"trainer_style\": \"BotezLive\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"time_budget_description\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"priority_topics\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"weekly_schedule\"", prompt, StringComparison.Ordinal);
        Assert.Contains("short_weekly_plan", prompt, StringComparison.Ordinal);
        Assert.Contains("detailed_weekly_plan", prompt, StringComparison.Ordinal);
        Assert.Contains("priority_rationale", prompt, StringComparison.Ordinal);
        Assert.Contains("tone_adapted_version", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFormatter_UsesModelOutputWhenItPassesFactGuard()
    {
        FakeTrainingPlanModel model = new(
            """
            {
              "short_weekly_plan": "Short weekly plan: Day 1 starts Protect Loose Pieces for 15 min.",
              "detailed_weekly_plan": "Detailed weekly plan: Day 1 is Protect Loose Pieces for 15 min, then repeat the same safety habit across the week.",
              "priority_rationale": "Priority rationale: Protect Loose Pieces comes first because it is the clearest current training topic.",
              "tone_adapted_version": "Tone adapted version: keep the 105 min week focused and concrete."
            }
            """);
        LocalModelTrainingPlanFormatter formatter = new(model);

        TrainingPlanFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Advanced,
            AdviceNarrationStyle.HikaruNakamura);

        Assert.False(formatter.UsedFallback);
        Assert.Equal("Short weekly plan: Day 1 starts Protect Loose Pieces for 15 min.", output.ShortWeeklyPlan);
        Assert.NotNull(model.LastRequest);
        Assert.Equal(TrainingPlanLlmPromptFormatter.OutputKeys, model.LastRequest!.JsonOutputKeys);
        Assert.Equal(ExplanationLevel.Advanced, model.LastRequest.ExplanationLevel);
        Assert.Equal(AdviceNarrationStyle.HikaruNakamura, model.LastRequest.NarrationStyle);
        Assert.Contains("weekly_schedule", model.LastRequest.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFormatter_FallsBackWhenModelAddsFactsOutsidePlan()
    {
        FakeTrainingPlanModel model = new(
            """
            {
              "short_weekly_plan": "Short weekly plan: train C99 for 12 days.",
              "detailed_weekly_plan": "Detailed weekly plan: add a 99 min opening block in C99.",
              "priority_rationale": "Priority rationale: C99 is urgent.",
              "tone_adapted_version": "Tone adapted version: fix C99."
            }
            """);
        LocalModelTrainingPlanFormatter formatter = new(model);

        TrainingPlanFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Beginner,
            AdviceNarrationStyle.RegularTrainer);

        Assert.True(formatter.UsedFallback);
        Assert.Contains("Protect Loose Pieces", output.ShortWeeklyPlan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("training plan data contract", formatter.FallbackReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeuristicFormatter_ProducesCompleteFallbackWithoutModel()
    {
        HeuristicTrainingPlanFormatter formatter = new();

        TrainingPlanFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Intermediate,
            AdviceNarrationStyle.WittyAlien);

        Assert.False(string.IsNullOrWhiteSpace(output.ShortWeeklyPlan));
        Assert.False(string.IsNullOrWhiteSpace(output.DetailedWeeklyPlan));
        Assert.False(string.IsNullOrWhiteSpace(output.PriorityRationale));
        Assert.False(string.IsNullOrWhiteSpace(output.ToneAdaptedVersion));
        Assert.Contains("Alien", output.ShortWeeklyPlan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cosmic", output.PriorityRationale, StringComparison.OrdinalIgnoreCase);
    }

    private static TrainingPlanReport CreateReport()
    {
        TrainingBlock repair = new(
            TrainingBlockPurpose.Repair,
            TrainingBlockKind.Tactics,
            "Loose piece scan",
            "Check attacked pieces before moving.",
            15,
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"]);
        TrainingBlock maintain = new(
            TrainingBlockPurpose.Maintain,
            TrainingBlockKind.GameReview,
            "Loose piece review",
            "Review two loose-piece examples.",
            20,
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"]);
        TrainingBlock checklist = new(
            TrainingBlockPurpose.Checklist,
            TrainingBlockKind.SlowPlayFocus,
            "Loose piece checklist",
            "Ask what became loose before every move.",
            15,
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"]);
        TrainingPlanTopic topic = new(
            1,
            TrainingPlanTopicCategory.CoreWeakness,
            "hanging_piece",
            "Board safety",
            "Protect Loose Pieces",
            "Loose pieces are the main recurring pattern.",
            "This topic appears often and costs material.",
            "The pattern is frequent and costly.",
            ProfileProgressDirection.Stable,
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"],
            ["Check whether any piece is undefended"],
            ["Review two examples"],
            [repair, maintain, checklist],
            [],
            new TrainingPlanPriorityBreakdown(100, 120, 60, 40, 320));
        WeeklyTrainingPlan weeklyPlan = new(
            "Alpha Weekly Training Plan",
            "Protect Loose Pieces is the first target.",
            new WeeklyTrainingBudget(105, 45, 30, 30, 0, "About 105 minutes for the week."),
            [
                new WeeklyTrainingDay(1, "Protect Loose Pieces", "Repair tactics", "Spot loose pieces", 15, TrainingPlanTopicCategory.CoreWeakness),
                new WeeklyTrainingDay(2, "Protect Loose Pieces", "Maintain game review", "Review two loose-piece examples", 20, TrainingPlanTopicCategory.CoreWeakness),
                new WeeklyTrainingDay(3, "Protect Loose Pieces", "Checklist slow play focus", "Ask what became loose", 15, TrainingPlanTopicCategory.CoreWeakness)
            ]);
        TrainingRecommendation recommendation = new(
            1,
            "Board safety",
            "Protect Loose Pieces",
            "Train loose piece awareness.",
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"],
            ["Check whether any piece is undefended"],
            ["Review two examples"],
            Blocks: [repair, maintain, checklist]);

        return new TrainingPlanReport(
            "alpha",
            "Alpha",
            ProfileProgressDirection.Stable,
            "Loose pieces are the first training priority.",
            [topic],
            [recommendation],
            weeklyPlan);
    }

    private sealed class FakeTrainingPlanModel : ILocalAdviceModel
    {
        private readonly string response;

        public FakeTrainingPlanModel(string response)
        {
            this.response = response;
        }

        public string Name => "fake-training-plan-model";

        public bool IsAvailable => true;

        public LocalModelAdviceRequest? LastRequest { get; private set; }

        public string? Generate(LocalModelAdviceRequest request)
        {
            LastRequest = request;
            return response;
        }
    }
}
