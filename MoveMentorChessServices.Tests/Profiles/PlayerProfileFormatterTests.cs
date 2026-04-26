using MoveMentorChessServices;
using Xunit;

namespace MoveMentorChessServices.Tests;

public sealed class PlayerProfileFormatterTests
{
    [Fact]
    public void PromptFormatter_BuildsStructuredInputAndRequiredOutputKeys()
    {
        PlayerProfileReport report = CreateReport();

        string prompt = PlayerProfileLlmPromptFormatter.BuildPrompt(report, PlayerProfileAudienceLevel.Beginner);

        Assert.Contains("\"player\": \"Alpha\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"audience_level\": \"Beginner\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"audience_description\": \"Beginner:", prompt, StringComparison.Ordinal);
        Assert.Contains("\"trainer_style\": \"RegularTrainer\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"trainer_description\": \"Regular trainer:", prompt, StringComparison.Ordinal);
        Assert.Contains("\"top_mistake_labels\"", prompt, StringComparison.Ordinal);
        Assert.Contains("profile_summary", prompt, StringComparison.Ordinal);
        Assert.Contains("strengths_and_weaknesses", prompt, StringComparison.Ordinal);
        Assert.Contains("what_to_focus_next", prompt, StringComparison.Ordinal);
        Assert.Contains("tone_adapted_version", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFormatter_UsesModelOutputWhenItPassesFactGuard()
    {
        FakeProfileModel model = new(
            """
            {
              "profile_summary": "Summary: Alpha should first reduce loose pieces.",
              "strengths_and_weaknesses": "Strengths and weaknesses: loose pieces are the clearest weakness.",
              "what_to_focus_next": "What to focus next: protect loose pieces before choosing active moves.",
              "tone_adapted_version": "Tone adapted version: keep the next session focused on loose pieces.",
              "deep_dive": "Deep dive: this is based on 4 games, 24 analyzed moves, and 6 highlighted mistakes."
            }
            """);
        LocalModelPlayerProfileFormatter formatter = new(model);

        PlayerProfileFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Advanced,
            AdviceNarrationStyle.HikaruNakamura);

        Assert.False(formatter.UsedFallback);
        Assert.Equal("Summary: Alpha should first reduce loose pieces.", output.ProfileSummary);
        Assert.NotNull(model.LastRequest);
        Assert.Equal(PlayerProfileLlmPromptFormatter.OutputKeys, model.LastRequest!.JsonOutputKeys);
        Assert.Equal(ExplanationLevel.Advanced, model.LastRequest.ExplanationLevel);
        Assert.Equal(AdviceNarrationStyle.HikaruNakamura, model.LastRequest.NarrationStyle);
        Assert.Contains("trainer_description", model.LastRequest.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFormatter_FallsBackWhenModelAddsFactsOutsideReport()
    {
        FakeProfileModel model = new(
            """
            {
              "profile_summary": "Summary: Alpha has trouble in C99.",
              "strengths_and_weaknesses": "Strengths and weaknesses: C99 is the issue.",
              "what_to_focus_next": "What to focus next: train C99 for 12 days.",
              "tone_adapted_version": "Tone adapted version: fix C99.",
              "deep_dive": ""
            }
            """);
        LocalModelPlayerProfileFormatter formatter = new(model);

        PlayerProfileFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Beginner,
            AdviceNarrationStyle.BotezLive);

        Assert.True(formatter.UsedFallback);
        Assert.Contains("loose pieces", output.ProfileSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile data contract", formatter.FallbackReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeuristicFormatter_ProducesCompleteFallbackWithoutModel()
    {
        HeuristicPlayerProfileFormatter formatter = new();

        PlayerProfileFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Advanced,
            AdviceNarrationStyle.HikaruNakamura);

        Assert.False(string.IsNullOrWhiteSpace(output.ProfileSummary));
        Assert.False(string.IsNullOrWhiteSpace(output.StrengthsAndWeaknesses));
        Assert.False(string.IsNullOrWhiteSpace(output.WhatToFocusNext));
        Assert.False(string.IsNullOrWhiteSpace(output.ToneAdaptedVersion));
        Assert.DoesNotContain("Frequency:", output.WhatToFocusNext, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("candidate", output.ToneAdaptedVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeuristicFormatter_AppliesWittyAlienStyleAcrossProfileFields()
    {
        HeuristicPlayerProfileFormatter formatter = new();

        PlayerProfileFormattedOutput output = formatter.Format(
            CreateReport(),
            PlayerProfileAudienceLevel.Intermediate,
            AdviceNarrationStyle.WittyAlien);

        Assert.Contains("Alien scan", output.ProfileSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cosmic", output.StrengthsAndWeaknesses, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spaceship", output.WhatToFocusNext, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alien-coach", output.ToneAdaptedVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("star-map", output.DeepDive, StringComparison.OrdinalIgnoreCase);
    }

    private static PlayerProfileReport CreateReport()
    {
        TrainingBlock block = new(
            TrainingBlockPurpose.Repair,
            TrainingBlockKind.Tactics,
            "Loose piece scan",
            "Check attacked pieces before moving.",
            15,
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"]);
        TrainingRecommendation recommendation = new(
            1,
            "Board safety",
            "Protect Loose Pieces",
            "Train loose piece awareness.",
            GamePhase.Middlegame,
            PlayerSide.White,
            ["C20"],
            ["Check whether any piece is undefended", "Compare forcing captures first"],
            ["Review two examples"],
            Blocks: [block]);
        WeeklyTrainingPlan weeklyPlan = new(
            "Alpha Weekly Training Plan",
            "Protect Loose Pieces is the first target.",
            new WeeklyTrainingBudget(105, 45, 30, 15, 15, "Balanced week."),
            [new WeeklyTrainingDay(1, "Protect Loose Pieces", "Repair tactics", "Spot loose pieces", 15, TrainingPlanTopicCategory.CoreWeakness)]);
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
            [block],
            [],
            new TrainingPlanPriorityBreakdown(100, 120, 60, 40, 320));
        TrainingPlanReport trainingPlan = new(
            "alpha",
            "Alpha",
            ProfileProgressDirection.Stable,
            "Loose pieces are the first training priority.",
            [topic],
            [recommendation],
            weeklyPlan);

        return new PlayerProfileReport(
            "alpha",
            "Alpha",
            4,
            24,
            6,
            140,
            [new ProfileLabelStat("hanging_piece", 3, 0.82), new ProfileLabelStat("missed_tactic", 2, 0.78)],
            [new ProfileCostlyLabelStat("hanging_piece", 3, 420, 140)],
            [new ProfilePhaseStat(GamePhase.Middlegame, 4)],
            [new ProfileOpeningStat("C20", 2)],
            [new ProfileSideStat(PlayerSide.White, 4, 6)],
            [],
            [],
            new ProfileProgressSignal(ProfileProgressDirection.Stable, "The recent trend is stable.", null, null),
            [],
            [recommendation],
            weeklyPlan,
            [],
            trainingPlan);
    }

    private sealed class FakeProfileModel : ILocalAdviceModel
    {
        private readonly string response;

        public FakeProfileModel(string response)
        {
            this.response = response;
        }

        public string Name => "fake-profile-model";

        public bool IsAvailable => true;

        public LocalModelAdviceRequest? LastRequest { get; private set; }

        public string? Generate(LocalModelAdviceRequest request)
        {
            LastRequest = request;
            return response;
        }
    }
}
