namespace LabTetherAgent.Services;

internal static class DiagnosticsExport
{
    internal static string CreateSuggestedFileName(DateTimeOffset timestamp) =>
        $"labtether-diagnostics-{timestamp:yyyyMMdd-HHmmss}";
}
