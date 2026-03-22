using System.IO.Compression;
using System.Text.Json;
using LabTetherAgent.Api;
using LabTetherAgent.Process;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Services;

/// <summary>
/// Collects diagnostic information and bundles it into a .zip file for support.
/// Mirrors mac-agent/Sources/LabTetherAgent/Services/DiagnosticsCollector.swift.
/// </summary>
public class DiagnosticsCollector
{
    private readonly AgentSettings _settings;
    private readonly AgentLogReader _logReader;
    private readonly LocalApiClient _apiClient;

    public DiagnosticsCollector(AgentSettings settings, AgentLogReader logReader, LocalApiClient apiClient)
    {
        _settings = settings;
        _logReader = logReader;
        _apiClient = apiClient;
    }

    /// <summary>
    /// Export diagnostics bundle to the specified path.
    /// </summary>
    public async Task ExportAsync(string outputPath)
    {
        using var stream = File.Create(outputPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // Agent logs
        var logs = _logReader.GetSnapshot();
        var logEntry = archive.CreateEntry("agent-logs.txt");
        await using (var writer = new StreamWriter(logEntry.Open()))
        {
            foreach (var line in logs)
                await writer.WriteLineAsync(line.Raw);
        }

        // Settings (secrets redacted)
        var settingsEntry = archive.CreateEntry("settings.json");
        await using (var writer = new StreamWriter(settingsEntry.Open()))
        {
            var redacted = new
            {
                _settings.HubUrl,
                _settings.AssetId,
                _settings.GroupId,
                _settings.AgentPort,
                _settings.TlsSkipVerify,
                _settings.DockerEnabled,
                _settings.DockerEndpoint,
                _settings.DockerDiscoveryInterval,
                _settings.FilesRootMode,
                _settings.AutoUpdateEnabled,
                _settings.AllowRemoteOverrides,
                _settings.LowPowerMode,
                _settings.LogLevel,
                _settings.WebRtcEnabled,
                ApiToken = _settings.ApiToken.Length > 0 ? "[REDACTED]" : "",
                EnrollmentToken = _settings.EnrollmentToken.Length > 0 ? "[REDACTED]" : "",
            };
            var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
            await writer.WriteAsync(json);
        }

        // AGENT_VERSION
        var versionPath = FindAgentVersionFile();
        if (versionPath != null && File.Exists(versionPath))
        {
            var versionEntry = archive.CreateEntry("AGENT_VERSION");
            await using var writer = new StreamWriter(versionEntry.Open());
            await writer.WriteAsync(await File.ReadAllTextAsync(versionPath));
        }

        // System info
        var sysEntry = archive.CreateEntry("system-info.txt");
        await using (var writer = new StreamWriter(sysEntry.Open()))
        {
            await writer.WriteLineAsync($"OS: {Environment.OSVersion}");
            await writer.WriteLineAsync($"Machine: {Environment.MachineName}");
            await writer.WriteLineAsync($"Processors: {Environment.ProcessorCount}");
            await writer.WriteLineAsync($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            await writer.WriteLineAsync($".NET: {Environment.Version}");
            await writer.WriteLineAsync($"Timestamp: {DateTime.UtcNow:O}");
        }

        // Agent info (from API)
        try
        {
            var info = await _apiClient.FetchInfoAsync();
            if (info != null)
            {
                var infoEntry = archive.CreateEntry("agent-info.json");
                await using var writer = new StreamWriter(infoEntry.Open());
                await writer.WriteAsync(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { /* API may not be reachable */ }
    }

    private static string? FindAgentVersionFile()
    {
        // Walk up from app directory looking for AGENT_VERSION
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 5; i++)
        {
            var candidate = Path.Combine(dir, "AGENT_VERSION");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }
}
