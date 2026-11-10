using CommunityToolkit.Mvvm.Input;
using LabTetherAgent.Services;
using LabTetherAgent.Settings;

namespace LabTetherAgent.Presentation;

/// <summary>
/// "Trust this Hub" in setup. A home Hub uses its own CA, which Windows does
/// not know. Setup then shows that CA's fingerprint and asks. Nothing is
/// trusted or saved until the user chooses Trust this Hub, and the token is
/// sent only after the Hub has verified against the accepted CA.
/// </summary>
public partial class OnboardingViewModel
{
    public const string OwnCertificateTitle = "This Hub uses its own certificate";
    public const string CertificateChangedTitle = "This Hub's certificate changed";

    private readonly Func<string, CancellationToken, Task<HubTrustDecision>> _probeHubCertificate;
    private HubTrustOffer? _trustOffer;
    private HubTrustOffer? _trustedHub;

    /// <summary>True while the panel asks about a Hub CA; Connect waits for the answer.</summary>
    public bool HasTrustPrompt => _trustOffer != null;

    public string TrustTitle => IsChangedCertificate(_trustOffer) ? CertificateChangedTitle : OwnCertificateTitle;

    /// <summary>
    /// The CA's SHA-256 fingerprint in groups of 4, as the console shows it
    /// under Add device, on two lines of eight groups.
    /// </summary>
    public string TrustFingerprint => _trustOffer is { } offer ? TwoLines(offer.GroupedFingerprint) : string.Empty;

    private static string TwoLines(string grouped) =>
        grouped.Length == 79 ? grouped[..39] + "\n" + grouped[40..] : grouped;

    private HubTrustOffer? TrustOffer
    {
        get => _trustOffer;
        set
        {
            if (ReferenceEquals(_trustOffer, value))
                return;
            _trustOffer = value;
            OnPropertyChanged(nameof(HasTrustPrompt));
            OnPropertyChanged(nameof(TrustTitle));
            OnPropertyChanged(nameof(TrustFingerprint));
            OnPropertyChanged(nameof(CanConnect));
        }
    }

    [RelayCommand]
    private async Task TrustHubAsync()
    {
        if (_trustOffer is not { } offer)
            return;
        _trustedHub = offer;
        TrustOffer = null;
        await ConnectAsync();
    }

    [RelayCommand]
    private void CancelTrust() => TrustOffer = null;

#if DEBUG
    /// <summary>Design snapshots only: shows the panel without a Hub.</summary>
    internal void ShowTrustPromptForSnapshot(HubTrustOffer offer) => TrustOffer = offer;
#endif

    /// <summary>
    /// After Windows or the chosen CA rejected the Hub's certificate: shows the
    /// panel when the Hub's own CA can be offered, otherwise returns the error.
    /// </summary>
    private async Task<string?> AskAboutRejectedCertificateAsync(CancellationToken cancellationToken)
    {
        // An accepted CA that no longer verifies the Hub is never kept quietly.
        _trustedHub = null;
        var decision = await _probeHubCertificate(HubUrl, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision.Offer is not { } offer)
            return decision.Problem ?? HubCertificateTrust.NotTrusted;
        TrustOffer = offer;
        return null;
    }

    // The trust belongs to one Hub address and one set of TLS choices.
    private void ForgetHubTrust()
    {
        _trustedHub = null;
        TrustOffer = null;
    }

    // "Changed" is judged against the CA recorded when this device enrolled,
    // for the same Hub address. A CA trusted now never proves it is that Hub.
    private bool IsChangedCertificate(HubTrustOffer? offer) =>
        offer != null &&
        _settings.EnrolledHubCaFingerprint.Length > 0 &&
        SettingsValidator.IsSameHub(_settings.HubUrl, HubUrl) &&
        !string.Equals(_settings.EnrolledHubCaFingerprint, offer.Fingerprint, StringComparison.Ordinal);
}
