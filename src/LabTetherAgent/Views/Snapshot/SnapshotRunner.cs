#if DEBUG
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using LabTetherAgent.App;
using LabTetherAgent.Presentation;
using LabTetherAgent.State;
using LabTetherAgent.Views.LogViewer;
using LabTetherAgent.Views.Onboarding;
using LabTetherAgent.Views.PopOut;
using LabTetherAgent.Views.Settings;
using LabTetherAgent.Views.TrayIcon;

namespace LabTetherAgent.Views.Snapshot;

/// <summary>
/// Debug builds only (--render-snapshots DIR): opens each window with demo
/// data, renders its content in light and dark with RenderTargetBitmap, writes
/// PNGs and exits. For design review on a lab machine; never in release builds.
/// </summary>
internal static class SnapshotRunner
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(900);

    public static async Task RunAsync(AppState appState, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var log = new List<string>();
        var keepAlive = new Window();
        keepAlive.AppWindow.Hide();
        SeedLogs(appState);

        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var suffix = theme == ElementTheme.Light ? "light" : "dark";
            await CaptureAsync(log, outputDirectory, $"win-flyout-{suffix}", theme, () => Flyout(appState, Connected()));
            await CaptureAsync(log, outputDirectory, $"win-flyout-offline-{suffix}", theme, () => Flyout(appState, Offline()));
            await CaptureAsync(log, outputDirectory, $"win-settings-{suffix}", theme, () => Settings(appState));
            await CaptureAsync(log, outputDirectory, $"win-onboarding-{suffix}", theme, () => Setup(appState, expand: false));
            await CaptureAsync(log, outputDirectory, $"win-onboarding-advanced-{suffix}", theme, () => Setup(appState, expand: true));
            await CaptureAsync(log, outputDirectory, $"win-onboarding-trust-{suffix}", theme, () => SetupTrust(appState));
            await CaptureAsync(log, outputDirectory, $"win-popout-{suffix}", theme, () => PopOut(appState));
            await CaptureAsync(log, outputDirectory, $"win-logs-{suffix}", theme, () => new LogViewerWindow(appState));
        }

        try { TraySnapshot.Write(outputDirectory, log); }
        catch (Exception ex) { log.Add($"win-tray: FAILED {ex.GetType().Name}: {ex.Message}"); }
        await CaptureAboutAsync(log, outputDirectory, appState);

        File.WriteAllLines(Path.Combine(outputDirectory, "snapshot-log.txt"), log);
        keepAlive.Close();
        Application.Current.Exit();
    }

    private static async Task CaptureAsync(List<string> log, string directory, string name, ElementTheme theme, Func<Window> open)
    {
        Window? window = null;
        try
        {
            window = open();
            if (window.Content is not FrameworkElement root)
                throw new InvalidOperationException("The window has no content.");
            root.RequestedTheme = theme;
            window.Activate();
            await Task.Delay(Settle);

            var (width, height) = await SaveAsync(root, directory, name);
            log.Add($"{name}: {width}x{height}");
        }
        catch (Exception ex)
        {
            log.Add($"{name}: FAILED {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            window?.Close();
        }
    }

    // The About dialog lives in the flyout's popup layer, so render the dialog itself.
    private static async Task CaptureAboutAsync(List<string> log, string directory, AppState appState)
    {
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var name = theme == ElementTheme.Light ? "win-about-light" : "win-about-dark";
            FlyoutWindow? flyout = null;
            try
            {
                flyout = Flyout(appState, Connected());
                ((FrameworkElement)flyout.Content).RequestedTheme = theme;
                flyout.HoldOpenForDialog(true);
                await Task.Delay(Settle);
                var dialog = new LabTetherAgent.Views.About.AboutDialog(appState,
                    WinRT.Interop.WindowNative.GetWindowHandle(flyout))
                {
                    XamlRoot = flyout.Content.XamlRoot,
                    RequestedTheme = theme,
                };
                _ = dialog.ShowAsync();
                await Task.Delay(Settle * 2);
                await SaveAsync(dialog, directory, name);
                log.Add($"{name}: rendered");
                dialog.Hide();
            }
            catch (Exception ex)
            {
                log.Add($"{name}: FAILED {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                flyout?.Close();
            }
        }
    }

    private static async Task<(int Width, int Height)> SaveAsync(UIElement element, string directory, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(directory);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            var dpi = 96.0 * element.XamlRoot.RasterizationScale;
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, dpi, dpi, pixels.ToArray());
            await encoder.FlushAsync();
        }
        return (bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static FlyoutWindow Flyout(AppState appState, AgentStatus status)
    {
        var window = new FlyoutWindow(appState) { DismissOnDeactivate = false };
        window.ViewModel.HubUrl = "wss://hub.example.lan/ws/agent";
        window.ViewModel.UpdateFromStatus(status);
        window.ShowNearTray();
        return window;
    }

    private static SettingsWindow Settings(AppState appState)
    {
        var window = new SettingsWindow(appState);
        var vm = window.ViewModel;
        vm.HubUrl = "https://hub.example.lan";
        vm.TlsCaFile = @"C:\ProgramData\LabTether\hub-ca.pem";
        vm.TlsSkipVerify = false;
        vm.StartAtLogin = true;
        vm.LowPowerMode = false;
        vm.AllowRemoteOverrides = false;
        vm.WebRtcEnabled = true;
        vm.LogLevel = "info";
        vm.DockerEndpoint = "";
        vm.DockerDiscoveryInterval = "30";
        return window;
    }

    private static OnboardingWindow Setup(AppState appState, bool expand)
    {
        var window = new OnboardingWindow(appState);
        window.ViewModel.HubUrl = "https://hub.example.lan";
        window.ViewModel.Token = "demo-enrollment-token-0000";
        window.ViewModel.AssetId = "win-desktop-01";
        window.ExpandAdvanced(expand);
        return window;
    }

    private static OnboardingWindow SetupTrust(AppState appState)
    {
        var window = Setup(appState, expand: false);
        window.ViewModel.ShowTrustPromptForSnapshot(new LabTetherAgent.Services.HubTrustOffer(
            "", "fd44a8fbb823d34a35f0d72ced949251c15896094d449781c9600eec2de80b32"));
        return window;
    }

    private static PopOutWindow PopOut(AppState appState)
    {
        var window = new PopOutWindow(appState);
        window.ViewModel.HubUrl = "wss://hub.example.lan/ws/agent";
        window.ViewModel.UpdateFromStatus(Connected());
        return window;
    }

    private static AgentStatus Connected() => new()
    {
        IsConnected = true,
        HubConnectionState = "connected",
        Uptime = "52h14m0s",
        CpuPercent = 23,
        MemoryPercent = 68,
        DiskPercent = 91,
        Alerts = [new AlertSnapshot("Disk is 91% full", "firing", "warning", "218 of 238 GB used", Today(21, 4))],
    };

    private static AgentStatus Offline() => new()
    {
        IsConnected = false,
        HubConnectionState = "connecting",
        LastErrorReason = "connection_refused",
        DisconnectedAt = Today(21, 12),
        CpuPercent = 4,
        MemoryPercent = 41,
        DiskPercent = 62,
    };

    private static DateTimeOffset Today(int hour, int minute)
    {
        var now = DateTimeOffset.Now;
        return new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset);
    }

    private static void SeedLogs(AppState appState)
    {
        foreach (var line in new[]
                 {
                     "agent started, version 2.0.0",
                     "connecting to wss://hub.example.lan/ws/agent",
                     "connected to hub",
                     "docker: 6 containers discovered",
                     "warning: disk C: is 91% full",
                     "error: connection refused, retrying in 5s",
                 })
        {
            appState.AgentProcess.LogReader.AppendRaw(line);
        }
    }
}
#endif
