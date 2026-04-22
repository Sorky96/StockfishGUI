namespace StockifhsGUI
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            LlamaCppProcessCleaner.CleanupOrphanedProcesses();

            // Advice quality evaluation mode (console).
            if (args.Length > 0 && args[0] == "--eval-advice")
            {
                AdviceQualityEvaluator.RunEvaluation();
                LlamaCppServerManager.Instance.Shutdown();
                return;
            }

            // Analysis quality report: classifier confidence + advice fallback summary.
            if (args.Length > 0 && args[0] == "--quality-report")
            {
                AnalysisQualityReporter.RunReport();
                return;
            }

            // Dataset export: dump StoredMoveAnalysis to JSONL + CSV for offline experiments.
            if (args.Length > 0 && args[0] == "--export-dataset")
            {
                IAnalysisStore store = SqliteAnalysisStore.CreateDefault();
                DatasetExporter.RunExport(store);
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.ApplicationExit += (_, _) => LlamaCppServerManager.Instance.Shutdown();
            Application.Run(new MainForm());
        }
    }
}
