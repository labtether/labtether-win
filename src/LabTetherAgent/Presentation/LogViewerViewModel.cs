using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;
using LabTetherAgent.State;

namespace LabTetherAgent.Presentation;

/// <summary>
/// ViewModel for the log viewer window.
/// Provides filtering, search, and export capabilities.
/// </summary>
public partial class LogViewerViewModel : ObservableObject, IDisposable
{
    private readonly AgentLogReader _logReader;
    private readonly SynchronizationContext? _uiContext;
    private readonly Action<LogLine> _logLineHandler;
    private List<LogLine> _allLines = [];
    private bool _disposed;

    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _selectedLevel = "All";
    [ObservableProperty] private List<LogLine> _filteredLines = [];
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private bool _autoScroll = true;

    public readonly string[] LevelOptions = ["All", "Info", "Warning", "Error", "Debug"];

    public event Action? OnNewLine; // signal UI to scroll

    public LogViewerViewModel(AgentLogReader logReader)
    {
        _logReader = logReader;
        _uiContext = SynchronizationContext.Current;
        _logLineHandler = line => RunOnUiThread(() => OnLogLineReceived(line));
        _logReader.OnLogLine += _logLineHandler;
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

    public string BuildExportContent(AgentSettings settings) =>
        string.Join(
            Environment.NewLine,
            FilteredLines.Select(line => DiagnosticsCollector.RedactLogLine(line.Raw, settings)));

    private void OnLogLineReceived(LogLine line)
    {
        if (_disposed)
            return;

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

    private void RunOnUiThread(Action update)
    {
        if (_disposed)
            return;
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            update();
            return;
        }

        _uiContext.Post(_ =>
        {
            if (!_disposed)
                update();
        }, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logReader.OnLogLine -= _logLineHandler;
    }
}
