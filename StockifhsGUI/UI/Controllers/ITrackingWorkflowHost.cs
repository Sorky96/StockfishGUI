namespace StockifhsGUI;

internal interface ITrackingWorkflowHost
{
    string GetCurrentFen();

    string GetTesseractDataPath();

    bool TryLoadTrackedSnapshot(TrackedPositionSnapshot snapshot, out string? error);

    void SetTrackingStatus(string message);

    void SetTrackingControlsRunning(bool isRunning);
}
