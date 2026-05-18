namespace MoveMentorChess.App.ViewModels;

internal sealed class ProfileCoachSessionTracker
{
    private readonly OpeningTrainingTelemetryService telemetryService;
    private readonly IClock clock;
    private PlayerProfileReport? currentReport;
    private string? currentPlayerKey;
    private ProfileViewMode currentViewMode = ProfileViewMode.Coach;
    private bool actionClicked;
    private bool closedTracked;
    private DateTime? openedUtc;
    private double maxScrollDepth;

    public ProfileCoachSessionTracker(OpeningTrainingTelemetryService telemetryService)
        : this(telemetryService, SystemClock.Instance)
    {
    }

    public ProfileCoachSessionTracker(OpeningTrainingTelemetryService telemetryService, IClock clock)
    {
        this.telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void StartProfile(PlayerProfileReport report, string? playerKey, ProfileViewMode viewMode)
    {
        currentReport = report ?? throw new ArgumentNullException(nameof(report));
        currentPlayerKey = playerKey;
        currentViewMode = viewMode;
        actionClicked = false;
        closedTracked = false;
        openedUtc = clock.UtcNow;
        maxScrollDepth = 0;
    }

    public void SetViewMode(ProfileViewMode viewMode)
    {
        currentViewMode = viewMode;
    }

    public void RecordScrollDepth(double scrollDepth)
    {
        maxScrollDepth = Math.Max(maxScrollDepth, scrollDepth);
    }

    public void Track(string eventName, Dictionary<string, string>? properties = null)
    {
        Dictionary<string, string>? mergedProperties = currentReport is null
            ? properties
            : BuildProperties(currentReport, properties);
        telemetryService.Track(eventName, currentPlayerKey, properties: mergedProperties);
    }

    public void TrackOpened()
    {
        Track("profile_coach_opened");
    }

    public void TrackActionClicked(string action, Dictionary<string, string>? properties = null)
    {
        actionClicked = true;
        Dictionary<string, string> result = properties is null
            ? []
            : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);

        result["action"] = action;
        result["view_mode"] = currentViewMode.ToString();
        result["seconds_since_open"] = openedUtc is null
            ? "0"
            : ((int)Math.Max(0, (clock.UtcNow - openedUtc.Value).TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

        Track("profile_training_action_clicked", result);
    }

    public void TrackClosed(string reason)
    {
        if (closedTracked || openedUtc is null)
        {
            return;
        }

        closedTracked = true;
        int secondsOpen = (int)Math.Max(0, (clock.UtcNow - openedUtc.Value).TotalSeconds);
        Track("profile_coach_closed", new Dictionary<string, string>
        {
            ["reason"] = reason,
            ["action_clicked"] = actionClicked.ToString().ToLowerInvariant(),
            ["seconds_open"] = secondsOpen.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max_scroll_depth"] = maxScrollDepth.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private Dictionary<string, string> BuildProperties(
        PlayerProfileReport report,
        Dictionary<string, string>? properties = null)
    {
        Dictionary<string, string> result = properties is null
            ? []
            : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);

        result["view_mode"] = currentViewMode.ToString();
        result["games_analyzed"] = report.GamesAnalyzed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["moves_analyzed"] = report.TotalAnalyzedMoves.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["highlighted_mistakes"] = report.HighlightedMistakes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["main_issue"] = report.TopMistakeLabels.Count > 0 ? report.TopMistakeLabels[0].Label : "none";
        result["trend"] = report.ProgressSignal.Direction.ToString();
        return result;
    }
}

internal enum ProfileViewMode
{
    Coach,
    Evidence
}
