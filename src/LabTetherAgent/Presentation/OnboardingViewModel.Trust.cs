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
    private bool _offerReplacesTrustedCa;

    /// <summary>True while the panel asks about a Hub CA; Connect waits for the answer.</summary>
    public bool HasTrustPrompt => _trustOffer != null;

    public string TrustTitle =>
        _offerReplacesTrustedCa || IsChangedCertificate(_trustOffer) ? CertificateChangedTitle : OwnCertificateTitle;

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
            if (value == null)
                _offerReplacesTrustedCa = false;
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
    /// <param name="hubUrl">The Hub URL this attempt was started with.</param>
    /// <param name="rejectedTrust">The CA accepted in the panel that the Hub just failed to verify against, if any.</param>
    private async Task<string?> AskAboutRejectedCertificateAsync(
        string hubUrl, HubTrustOffer? rejectedTrust, CancellationToken cancellationToken)
    {
        // An accepted CA that no longer verifies the Hub is never kept quietly.
        _trustedHub = null;
        var decision = await _probeHubCertificate(hubUrl, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // The answer is about the address that was submitted, not one typed since.
        if (HubUrl != hubUrl)
            return null;
        if (decision.Offer is not { } offer)
            return decision.Problem ?? HubCertificateTrust.NotTrusted;
        // The same CA again cannot verify this Hub; asking once more would loop.
        if (rejectedTrust != null && rejectedTrust.Fingerprint == offer.Fingerprint)
            return HubCertificateTrust.TrustedCaDidNotVerify;
        TrustOffer = offer;
        // A different CA right after one was accepted is a change, said as one.
        _offerReplacesTrustedCa = rejectedTrust != null;
        OnPropertyChanged(nameof(TrustTitle));
        return null;
    }

    // The trust belongs to one Hub address and one set of TLS choices.
    private void ForgetHubTrust()
    {
        _trustedHub = null;
        TrustOffer = null;
    }

    // "Changed" is judged against the CA recorded when this device enrolled,
    // for the same Hub address. A device enrolled before that record existed
    // falls back to its saved CA file. This only words the question; a CA
    // offered now never proves it is that Hub.
    private bool IsChangedCertificate(HubTrustOffer? offer)
    {
        if (offer == null || !SettingsValidator.IsSameHub(_settings.HubUrl, HubUrl))
            return false;
        var enrolled = _settings.EnrolledHubCaFingerprint.Length > 0
            ? _settings.EnrolledHubCaFingerprint
            : SavedCaFileFingerprint();
        return !string.IsNullOrEmpty(enrolled) && !string.Equals(enrolled, offer.Fingerprint, StringComparison.Ordinal);
    }

    private string? SavedCaFileFingerprint()
    {
        if (string.IsNullOrWhiteSpace(_settings.TlsCaFile))
            return null;
        try
        {
            return HubCertificateTrust.FingerprintOfPem(File.ReadAllText(_settings.TlsCaFile.Trim()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
