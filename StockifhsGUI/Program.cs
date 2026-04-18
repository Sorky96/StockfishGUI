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
            // Advice quality evaluation mode (console).
            if (args.Length > 0 && args[0] == "--eval-advice")
            {
                AdviceQualityEvaluator.RunEvaluation();
                LlamaCppServerManager.Instance.Shutdown();
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
