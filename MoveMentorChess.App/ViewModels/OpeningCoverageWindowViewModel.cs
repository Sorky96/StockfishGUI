using System.Collections.ObjectModel;
using MoveMentorChess.Persistence;

namespace MoveMentorChess.App.ViewModels;

public sealed class OpeningCoverageWindowViewModel : ViewModelBase
{
    private readonly OpeningTrainerWorkspaceService workspaceService;
    private string filterText = string.Empty;
    private string playerKey = "opening-coach:both";
    private RepertoireSide selectedSide = RepertoireSide.Both;
    private OpeningCoverageLineItemViewModel? selectedLine;
    private string statusText = "Loading opening coverage...";
    private int totalLines;
    private int dueLines;
    private int weakBranches;
    private int missingEcoLines;
    private double averageCoveragePercent;

    public OpeningCoverageWindowViewModel(IAnalysisStore analysisStore)
        : this(new OpeningTrainerWorkspaceService(analysisStore))
    {
    }

    public OpeningCoverageWindowViewModel(OpeningTrainerWorkspaceService workspaceService)
    {
        this.workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        RefreshCommand = new RelayCommand(Refresh);
        AvailableSides = Enum.GetValues<RepertoireSide>();
        Refresh();
    }

    public ObservableCollection<OpeningCoverageLineItemViewModel> Lines { get; } = [];

    public IReadOnlyList<RepertoireSide> AvailableSides { get; }

    public RelayCommand RefreshCommand { get; }

    public string FilterText
    {
        get => filterText;
        set
        {
            if (SetProperty(ref filterText, value))
            {
                Refresh();
            }
        }
    }

    public string PlayerKey
    {
        get => playerKey;
        set
        {
            if (SetProperty(ref playerKey, string.IsNullOrWhiteSpace(value) ? "opening-coach:both" : value.Trim()))
            {
                Refresh();
            }
        }
    }

    public RepertoireSide SelectedSide
    {
        get => selectedSide;
        set
        {
            if (SetProperty(ref selectedSide, value))
            {
                Refresh();
            }
        }
    }

    public OpeningCoverageLineItemViewModel? SelectedLine
    {
        get => selectedLine;
        set => SetProperty(ref selectedLine, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string TotalLinesText => totalLines.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string DueReviewText => dueLines.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string WeakBranchesText => weakBranches.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string MissingEcoText => missingEcoLines.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string AverageCoverageText => totalLines == 0
        ? "n/a"
        : $"{averageCoveragePercent:0.0}%";

    private void Refresh()
    {
        IReadOnlyList<OpeningLineCatalogItem> lines = workspaceService.ListOpeningLines(FilterText, SelectedSide, 160);
        PlayerOpeningPlan plan = workspaceService.GetPlayerOpeningPlan(PlayerKey, SelectedSide, 160);
        HashSet<string> dueEco = plan.Today
            .Where(item => !string.IsNullOrWhiteSpace(item.Eco))
            .Select(item => item.Eco!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<OpeningCoverageLineItemViewModel> items = [];
        foreach (OpeningLineCatalogItem line in lines)
        {
            OpeningTrainerOverview? overview = null;
            workspaceService.TryGetOverview(line, PlayerKey, out overview);
            items.Add(OpeningCoverageLineItemViewModel.Create(line, overview, dueEco.Contains(line.Eco)));
        }

        List<OpeningCoverageLineItemViewModel> ordered = items
            .OrderByDescending(item => item.IsDue)
            .ThenByDescending(item => item.WeakBranches)
            .ThenBy(item => item.CoveragePercent)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Lines.Clear();
        foreach (OpeningCoverageLineItemViewModel item in ordered)
        {
            Lines.Add(item);
        }

        SelectedLine = Lines.FirstOrDefault();
        totalLines = Lines.Count;
        dueLines = Lines.Count(item => item.IsDue);
        weakBranches = Lines.Sum(item => item.WeakBranches);
        missingEcoLines = Lines.Count(item => string.IsNullOrWhiteSpace(item.Eco));
        averageCoveragePercent = Lines.Count == 0 ? 0 : Lines.Average(item => item.CoveragePercent);
        StatusText = Lines.Count == 0
            ? "No repertoire lines match the current filters."
            : $"Showing {Lines.Count} repertoire line{PluralSuffix(Lines.Count)} for {FormatSide(SelectedSide)}.";
        RaiseSummaryChanged();
    }

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(TotalLinesText));
        OnPropertyChanged(nameof(DueReviewText));
        OnPropertyChanged(nameof(WeakBranchesText));
        OnPropertyChanged(nameof(MissingEcoText));
        OnPropertyChanged(nameof(AverageCoverageText));
    }

    private static string FormatSide(RepertoireSide side)
        => side switch
        {
            RepertoireSide.White => "White",
            RepertoireSide.Black => "Black",
            _ => "both sides"
        };

    private static string PluralSuffix(int count) => count == 1 ? string.Empty : "s";
}

public sealed class OpeningCoverageLineItemViewModel
{
    private OpeningCoverageLineItemViewModel(
        OpeningLineCatalogItem line,
        OpeningTrainerOverview? overview,
        bool isDue)
    {
        Line = line;
        IsDue = isDue;
        Eco = line.Eco;
        DisplayName = line.DisplayName;
        SideText = line.RepertoireSide.ToString();
        BookBranches = line.BookBranchCount;
        BookGames = line.BookGameCount;
        MainLineText = overview is null || overview.MainLine.Count == 0
            ? "No main line loaded"
            : string.Join(" ", overview.MainLine.Take(8).Select(move => move.San));
        CoveragePercent = overview?.Coverage.CoveragePercent ?? 0;
        CoveredBranches = overview?.Coverage.CoveredBranches ?? 0;
        TotalBranches = overview?.Coverage.TotalBookBranches ?? Math.Max(line.BookBranchCount, 0);
        WeakBranches = overview?.Coverage.WeakBranches ?? Math.Max(line.BookBranchCount, 0);
        UnseenCommonBranches = overview?.Coverage.UnseenCommonBranches ?? WeakBranches;
        KnowledgeBoundaryPly = overview?.Coverage.KnowledgeBoundaryPly ?? 0;
        PriorityText = BuildPriorityText(overview);
        StatusText = BuildStatusText();
        CoverageText = $"{CoveragePercent:0.0}% covered";
        BranchText = $"{CoveredBranches}/{Math.Max(TotalBranches, 1)} branches covered";
        WeakBranchText = WeakBranches == 0
            ? "No weak branches saved"
            : $"{WeakBranches} weak branch{PluralSuffix(WeakBranches)}";
        MissingEcoText = string.IsNullOrWhiteSpace(Eco) ? "Missing ECO" : Eco;
    }

    public OpeningLineCatalogItem Line { get; }

    public bool IsDue { get; }

    public string Eco { get; }

    public string DisplayName { get; }

    public string SideText { get; }

    public int BookBranches { get; }

    public int BookGames { get; }

    public string MainLineText { get; }

    public double CoveragePercent { get; }

    public int CoveredBranches { get; }

    public int TotalBranches { get; }

    public int WeakBranches { get; }

    public int UnseenCommonBranches { get; }

    public int KnowledgeBoundaryPly { get; }

    public string PriorityText { get; }

    public string StatusText { get; }

    public string CoverageText { get; }

    public string BranchText { get; }

    public string WeakBranchText { get; }

    public string MissingEcoText { get; }

    public static OpeningCoverageLineItemViewModel Create(
        OpeningLineCatalogItem line,
        OpeningTrainerOverview? overview,
        bool isDue)
        => new(line, overview, isDue);

    private string BuildStatusText()
    {
        if (IsDue)
        {
            return "Due review";
        }

        if (WeakBranches > 0)
        {
            return "Weak branches";
        }

        if (CoveragePercent <= 0.1)
        {
            return "Unstarted";
        }

        return "Stable";
    }

    private static string BuildPriorityText(OpeningTrainerOverview? overview)
    {
        TrainingPriorityItem? priority = overview?.Priorities.FirstOrDefault();
        if (priority is null)
        {
            return "No priority signal yet.";
        }

        return $"{priority.Title}: {priority.Summary}";
    }

    private static string PluralSuffix(int count) => count == 1 ? string.Empty : "es";
}
