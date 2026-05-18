using System.Diagnostics;

namespace MoveMentorChess.Persistence;

public interface IPersistenceDiagnosticsLogger
{
    void WriteWarning(string source, string message, Exception exception);
}

public static class PersistenceDiagnostics
{
    private static readonly IPersistenceDiagnosticsLogger DefaultLogger = new TracePersistenceDiagnosticsLogger();

    private static IPersistenceDiagnosticsLogger logger = DefaultLogger;

    public static IPersistenceDiagnosticsLogger Logger
    {
        get => logger;
        set => logger = value ?? DefaultLogger;
    }

    public static void ResetLogger()
    {
        logger = DefaultLogger;
    }

    internal static void Warning(string source, string message, Exception exception)
    {
        logger.WriteWarning(source, message, exception);
    }
}

public sealed class TracePersistenceDiagnosticsLogger : IPersistenceDiagnosticsLogger
{
    public void WriteWarning(string source, string message, Exception exception)
    {
        Trace.TraceWarning(
            "{0}: {1} ({2}: {3})",
            source,
            message,
            exception.GetType().Name,
            exception.Message);
    }
}
