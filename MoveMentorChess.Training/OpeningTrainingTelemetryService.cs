namespace MoveMentorChess.Training;

public sealed class OpeningTrainingTelemetryService
{
    private readonly List<OpeningTrainingTelemetryEvent> events = [];
    private readonly IOpeningTrainingTelemetryStore? telemetryStore;
    private readonly IClock clock;

    public OpeningTrainingTelemetryService(IOpeningTrainingTelemetryStore? telemetryStore = null)
        : this(telemetryStore, SystemClock.Instance)
    {
    }

    public OpeningTrainingTelemetryService(IOpeningTrainingTelemetryStore? telemetryStore, IClock clock)
    {
        this.telemetryStore = telemetryStore;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void Track(OpeningTrainingTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        telemetryStore?.SaveOpeningTrainingTelemetryEvent(telemetryEvent);
        lock (events)
        {
            events.Add(telemetryEvent);
        }
    }

    public void Track(
        string eventName,
        string? playerKey = null,
        OpeningLineCatalogItem? opening = null,
        OpeningTrainingSession? session = null,
        string? recommendationId = null,
        SpecialTrainingModeKind? specialMode = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Track(new OpeningTrainingTelemetryEvent(
            eventName,
            clock.UtcNow,
            NormalizePlayerKey(playerKey),
            opening?.LineKey,
            opening?.OpeningKey,
            session?.SessionId,
            recommendationId,
            specialMode,
            properties));
    }

    public IReadOnlyList<OpeningTrainingTelemetryEvent> Snapshot()
    {
        lock (events)
        {
            return events.ToList();
        }
    }

    private static string? NormalizePlayerKey(string? playerKey)
    {
        return string.IsNullOrWhiteSpace(playerKey)
            ? null
            : playerKey.Trim().ToLowerInvariant();
    }
}
