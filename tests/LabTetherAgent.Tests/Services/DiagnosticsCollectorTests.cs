using System.IO.Compression;
using LabTetherAgent.Api;
using LabTetherAgent.Process;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Tests.Services;

public class DiagnosticsCollectorTests
{
    [Fact]
    public async Task ExportRedactsConfiguredAndPatternMatchedSecretsFromEveryTextEntry()
    {
        var settings = new AgentSettings
        {
            ApiToken = "api-secret-value",
            EnrollmentToken = "enrollment-secret-value",
            WebRtcTurnPass = "turn-secret-value",
            LocalApiAuthToken = "local-api-secret-value",
        };
        var logs = new AgentLogReader();
        logs.AppendRaw("Authorization: Bearer api-secret-value");
        logs.AppendRaw("enrollment_token=enrollment-secret-value");
        logs.AppendRaw("turn credential turn-secret-value");
        logs.AppendRaw("local auth local-api-secret-value");
        logs.AppendRaw("request https://hub.test/path?access_token=untracked-secret&ok=true");
        logs.AppendRaw("{\"password\":\"untracked-json-secret\",\"safe\":true}");
        logs.AppendRaw("{'api_token': 'untracked-single-quoted-secret'}");

        using var apiClient = new LocalApiClient();
        var collector = new DiagnosticsCollector(settings, logs, apiClient);
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"labtether-diagnostics-test-{Guid.NewGuid():N}.zip");

        try
        {
            await collector.ExportAsync(outputPath);

            using var archive = ZipFile.OpenRead(outputPath);
            var text = string.Join(
                "\n",
                await Task.WhenAll(archive.Entries.Select(ReadEntryAsync)));

            Assert.DoesNotContain("api-secret-value", text, StringComparison.Ordinal);
            Assert.DoesNotContain("enrollment-secret-value", text, StringComparison.Ordinal);
            Assert.DoesNotContain("turn-secret-value", text, StringComparison.Ordinal);
            Assert.DoesNotContain("local-api-secret-value", text, StringComparison.Ordinal);
            Assert.DoesNotContain("untracked-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("untracked-json-secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("untracked-single-quoted-secret", text, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
