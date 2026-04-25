namespace MoveMentorChessServices;

public sealed class AdviceRuntimeInvocationException : Exception
{
    public AdviceRuntimeInvocationException(
        string message,
        AdviceRuntimeInvocationLog log,
        string diagnosticPath,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
        DiagnosticPath = diagnosticPath ?? throw new ArgumentNullException(nameof(diagnosticPath));
    }

    public AdviceRuntimeInvocationLog Log { get; }

    public string DiagnosticPath { get; }
}
