using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Process;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the log viewer window.
/// Provides filtering, search, and export capabilities.
/// </summary>
public partial class LogViewerViewModel : ObservableObject
{
    private readonly AgentLogReader _logReader;
    private List<LogLine> _allLines = [];

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedLevel = "All";
    [ObservableProperty] private List<LogLine> _filteredLines = [];
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _autoScroll = true;

    public static readonly string[] LevelOptions = ["All", "Info", "Warning", "Error", "Debug"];

    public event Action? OnNewLine; // signal UI to scroll

    public LogViewerViewModel(AgentLogReader logReader)
    {
        _logReader = logReader;
        _logReader.OnLogLine += OnLogLineReceived;
        Refresh();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    /// <summary>
    /// Reload all lines from the buffer and apply filter.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        _allLines = _logReader.GetSnapshot();
        TotalCount = _allLines.Count;
        ApplyFilter();
    }

    [RelayCommand]
    private void Clear()
    {
        _logReader.Clear();
        _allLines.Clear();
        TotalCount = 0;
        ApplyFilter();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        // Export filtered lines to a text file.
        // The actual file picker is platform-specific (WinUI FileSavePicker),
        // so this method prepares the content and the View code-behind handles the picker.
        var content = string.Join(Environment.NewLine, FilteredLines.Select(l => l.Raw));
        var path = Path.Combine(Path.GetTempPath(), $"labtether-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(path, content);
        // View code-behind can move this to user-selected path
    }

    private void OnLogLineReceived(LogLine line)
    {
        _allLines.Add(line);
        TotalCount = _allLines.Count;

        if (MatchesFilter(line))
        {
            var updated = new List<LogLine>(FilteredLines) { line };
            FilteredLines = updated;
            FilteredCount = updated.Count;
            OnNewLine?.Invoke();
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allLines.Where(MatchesFilter).ToList();
        FilteredLines = filtered;
        FilteredCount = filtered.Count;
    }

    private bool MatchesFilter(LogLine line)
    {
        // Level filter
        if (SelectedLevel != "All" &&
            !string.Equals(line.Level, SelectedLevel, StringComparison.OrdinalIgnoreCase))
            return false;

        // Text filter
        if (!string.IsNullOrEmpty(FilterText) &&
            !line.Raw.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
