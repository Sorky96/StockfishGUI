using System.Collections.Generic;

namespace MoveMentorChess.Domain;

public sealed record PgnGameMetadata(
    string? Round,
    string? CurrentPosition,
    string? Timezone,
    string? EcoUrl,
    string? UtcDate,
    string? UtcTime,
    string? TimeControl,
    string? Termination,
    string? StartTime,
    string? EndDate,
    string? EndTime,
    string? Link,
    GameTimeControlCategory TimeControlCategory);
