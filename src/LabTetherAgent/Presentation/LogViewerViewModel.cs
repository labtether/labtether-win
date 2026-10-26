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
    private readonly object _refreshGate = new();
    private List<LogLine> _allLines = [];
    private volatile bool _disposed;
    private bool _refreshPending;

    private string _filterText = string.Empty;
    private string _selectedLevel = "All";
    private List<LogLine> _filteredLines = [];
    private int _totalCount;
    private int _filteredCount;
    private bool _autoScroll = true;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                OnFilterTextChanged(value);
        }
    }

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value))
                OnSelectedLevelChanged(value);
        }
    }

    public List<LogLine> FilteredLines
    {
        get => _filteredLines;
        set => SetProperty(ref _filteredLines, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    public int FilteredCount
    {
        get => _filteredCount;
        set => SetProperty(ref _filteredCount, value);
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public readonly string[] LevelOptions = ["All", "Info", "Warning", "Error", "Debug"];

    public event Action? OnNewLine; // signal UI to scroll

    public LogViewerViewModel(AgentLogReader logReader)
    {
        _logReader = logReader;
        _uiContext = SynchronizationContext.Current;
        _logLineHandler = _ => ScheduleLogRefresh();
        _logReader.OnLogLine += _logLineHandler;
        Refresh();
    }

    private void OnFilterTextChanged(string value) => ApplyFilter();
    private void OnSelectedLevelChanged(string value) => ApplyFilter();

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
            LogExportRedactor.RedactLines(FilteredLines, settings));

    private void RefreshFromLogEvents()
    {
        lock (_refreshGate)
        {
            _refreshPending = false;
            if (_disposed) return;
        }
        var previousTail = FilteredLines.LastOrDefault();
        // The reader owns retention. Do not keep a second, growing event history.
        Refresh();
        var currentTail = FilteredLines.LastOrDefault();
        if (currentTail != null && !ReferenceEquals(previousTail, currentTail))
            OnNewLine?.Invoke();
    }

    private void ApplyFilter()
    {
        var filtered = _allLines.Where(MatchesFilter).ToList();
        if (FilteredLines.SequenceEqual(filtered))
            return;
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

    private void ScheduleLogRefresh()
    {
        lock (_refreshGate)
        {
            if (_disposed || _refreshPending) return;
            _refreshPending = true;
        }
        if (_uiContext == null || SynchronizationContext.Current == _uiContext)
        {
            RefreshFromLogEvents();
            return;
        }

        // One callback captures this owner, never one log payload per event.
        _uiContext.Post(_ => RefreshFromLogEvents(), null);
    }

    public void Dispose()
    {
        lock (_refreshGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _logReader.OnLogLine -= _logLineHandler;
    }
}
