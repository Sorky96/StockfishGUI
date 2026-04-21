using System.Threading.Tasks;
using System.Windows.Forms;

namespace StockifhsGUI;

public partial class MainForm
{
    private Task OpenImportedGameAnalysisAsync()
    {
        if (importedSession.Game is null || importedSession.Game.SanMoves.Count == 0)
        {
            MessageBox.Show("Import a PGN game before starting analysis.", "Analyze Imported Game", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        if (engine is null)
        {
            MessageBox.Show(MissingEngineMessage, "Analyze Imported Game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.CompletedTask;
        }

        GameAnalysisForm analysisForm = new(importedSession.Game, engine, NavigateToAnalysisMistake);
        analysisForm.Show(this);
        return Task.CompletedTask;
    }

    private void OpenSavedAnalyses()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            MessageBox.Show("Local analysis storage is unavailable on this machine.", "Saved Analyses", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SavedAnalysesForm dialog = new(store, canOpenAnalysis: engine is not null);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedResult is null)
        {
            return;
        }

        try
        {
            LoadImportedGame(dialog.SelectedResult.Game);

            if (dialog.RequestedAction == SavedAnalysisAction.OpenAnalysis)
            {
                if (engine is null)
                {
                    MessageBox.Show(MissingEngineMessage, "Saved Analyses", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                GameAnalysisForm analysisForm = new(dialog.SelectedResult.Game, engine, NavigateToAnalysisMistake, dialog.SelectedResult.AnalyzedSide);
                analysisForm.Show(this);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the selected saved analysis.{Environment.NewLine}{ex.Message}", "Saved Analyses", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenPlayerProfiles()
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null)
        {
            MessageBox.Show("Local analysis storage is unavailable on this machine.", "Player Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PlayerProfilesForm profilesForm = new(new PlayerProfileService(store), pieceImages, NavigateToProfileExample);
        profilesForm.Show(this);
    }

    private void NavigateToProfileExample(ProfileMistakeExample example)
    {
        IAnalysisStore? store = AnalysisStoreProvider.GetStore();
        if (store is null || !store.TryLoadImportedGame(example.GameFingerprint, out ImportedGame? game) || game is null)
        {
            MessageBox.Show("Could not find the game in the local store.", "Game Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (engine is null)
        {
            MessageBox.Show(MissingEngineMessage, "Analysis Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // We load the game into the main UI so they can see the PGN in the background
        LoadImportedGame(game);

        // Then we open the analysis form for that side
        GameAnalysisForm analysisForm = new(game, engine, NavigateToAnalysisMistake, example.Side);
        analysisForm.Show(this);
    }

    private void NavigateToAnalysisMistake(MoveAnalysisResult moveAnalysis)
    {
        analysisNavigation.NavigateToMistake(moveAnalysis);
    }

    ImportedGameSession IAnalysisNavigationHost.ImportedSession => importedSession;

    IList<BoardArrow> IAnalysisNavigationHost.AnalysisArrows => analysisArrows;

    Point? IAnalysisNavigationHost.AnalysisTargetSquare
    {
        get => analysisTargetSquare;
        set => analysisTargetSquare = value;
    }

    void IAnalysisNavigationHost.ReplayImportedMovesThrough(int targetIndex) => ReplayImportedMovesThrough(targetIndex);

    void IAnalysisNavigationHost.SetSuggestionText(string text) => suggestionLabel.Text = text;

    void IAnalysisNavigationHost.InvalidateBoardSurface() => InvalidateBoardSurface();
}
