using System.Text.Json;
using DND.Electrical.Shared;

namespace DND.Electrical.Licensing;

public enum LicenseState
{
    /// <summary>No licence installed: projects can be opened and viewed, editing and module features are locked.</summary>
    NotActivated,
    Valid,
    /// <summary>Expired or not revalidated in time but still inside the grace period.</summary>
    GracePeriod,
    Expired,
    Revoked,
    Suspended,
    InvalidSignature,
    WrongDevice,
    /// <summary>The system clock was set back: features are locked until the licence is refreshed online.</summary>
    ClockTampered,
}

public sealed record LicenseEvaluation(LicenseState State, LicenseClaims? Claims, string Message, DateTimeOffset? GraceEndsAt = null)
{
    public bool AllowsFeatures => State is LicenseState.Valid or LicenseState.GracePeriod;
}

/// <summary>Persistent client-side licensing state (licence file + validation history).</summary>
public sealed class LicenseStore(string folder)
{
    public string Folder { get; } = folder;
    public string LicensePath => Path.Combine(Folder, "license.dndlic");
    private string StatePath => Path.Combine(Folder, "license-state.json");
    public string RevocationPath => Path.Combine(Folder, "revocations.json");

    public sealed class State
    {
        public DateTimeOffset LastSeenUtc { get; set; }
        public DateTimeOffset? LastOnlineValidationUtc { get; set; }
        public string? FloatingLeaseJson { get; set; }
    }

    public SignedLicense? LoadLicense() => File.Exists(LicensePath) ? SignedLicense.FromJson(File.ReadAllText(LicensePath)) : null;

    public void SaveLicense(SignedLicense license)
    {
        Directory.CreateDirectory(Folder);
        var tmp = LicensePath + ".tmp";
        File.WriteAllText(tmp, license.ToJson());
        File.Move(tmp, LicensePath, overwrite: true);
    }

    public void DeleteLicense()
    {
        if (File.Exists(LicensePath)) File.Delete(LicensePath);
    }

    public State LoadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State() : new State(); }
        catch (JsonException) { return new State(); }
    }

    public void SaveState(State s)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
    }

    public (string payload, string signature, string keyId)? LoadRevocations()
    {
        if (!File.Exists(RevocationPath)) return null;
        var doc = JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(RevocationPath));
        return doc is null ? null : (doc.Payload, doc.Signature, doc.KeyId);
    }

    public void SaveRevocations(SignedEnvelope envelope)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(RevocationPath, JsonSerializer.Serialize(envelope));
    }
}

/// <summary>Generic signed document (revocation lists, seat leases).</summary>
public sealed record SignedEnvelope(string KeyId, string Payload, string Signature);

/// <summary>
/// Evaluates the installed licence and answers entitlement questions. Enforcement is performed by application
/// services calling <see cref="Demand"/> at feature execution time, not by hiding UI elements.
/// </summary>
public sealed class LicenseManager : IEntitlements
{
    private readonly LicenseStore _store;
    private readonly TrustedKeys _keys;
    private readonly IClock _clock;

    public LicenseManager(LicenseStore store, TrustedKeys keys, IClock? clock = null, string? deviceId = null)
    {
        _store = store;
        _keys = keys;
        _clock = clock ?? SystemClock.Instance;
        DeviceId = deviceId ?? DeviceIdentity.Compute(store.Folder);
        Current = Evaluate();
    }

    public string DeviceId { get; }
    public LicenseEvaluation Current { get; private set; }
    public LicenseStore Store => _store;
    public TrustedKeys Keys => _keys;
    public IClock Clock => _clock;

    public event EventHandler? Changed;

    public LicenseEvaluation Refresh()
    {
        Current = Evaluate();
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    /// <summary>Installs a licence document after verifying it (activation, renewal or offline response).</summary>
    public LicenseEvaluation Install(SignedLicense license, bool onlineValidated = false)
    {
        var (claims, error) = LicenseVerifier.Verify(license, _keys);
        if (claims is null) throw new LicenseException(error!);
        if (claims.DeviceId is { } d && !string.Equals(d, DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new LicenseException("This licence was activated for another device.");
        _store.SaveLicense(license);
        var s = _store.LoadState();
        var now = _clock.Now.ToUniversalTime();
        if (onlineValidated) s.LastOnlineValidationUtc = now;
        // An installation counts as a fresh time reference.
        s.LastSeenUtc = now;
        _store.SaveState(s);
        return Refresh();
    }

    public void InstallRevocationList(SignedEnvelope envelope)
    {
        if (!LicenseVerifier.VerifyObject(envelope.Payload, envelope.Signature, envelope.KeyId, _keys))
            throw new LicenseException("The revocation list signature is invalid.");
        _store.SaveRevocations(envelope);
        Refresh();
    }

    public void Uninstall()
    {
        _store.DeleteLicense();
        Refresh();
    }

    private LicenseEvaluation Evaluate()
    {
        SignedLicense? doc;
        try { doc = _store.LoadLicense(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return new(LicenseState.InvalidSignature, null, "The licence file is corrupted."); }
        if (doc is null) return new(LicenseState.NotActivated, null, "No licence activated. Projects can be opened and viewed; engineering features require a licence.");
        var (claims, error) = LicenseVerifier.Verify(doc, _keys);
        if (claims is null) return new(LicenseState.InvalidSignature, null, error!);
        if (claims.DeviceId is { } dev && !string.Equals(dev, DeviceId, StringComparison.OrdinalIgnoreCase))
            return new(LicenseState.WrongDevice, claims, "The licence is activated for another device. Activate it on this device or transfer it.");

        var now = _clock.Now.ToUniversalTime();
        var state = _store.LoadState();
        if (state.LastSeenUtc != default && now < state.LastSeenUtc.AddHours(-24))
            return new(LicenseState.ClockTampered, claims, "The system clock is earlier than the last recorded use. Correct the clock or refresh the licence online.");
        if (now > state.LastSeenUtc)
        {
            state.LastSeenUtc = now;
            _store.SaveState(state);
        }

        if (IsRevoked(claims.LicenseId)) return new(LicenseState.Revoked, claims, "This licence has been revoked. Contact D&D | Data&Development.");
        if (claims.Status == LicenseStatus.Revoked) return new(LicenseState.Revoked, claims, "This licence has been revoked.");
        if (claims.Status == LicenseStatus.Suspended) return new(LicenseState.Suspended, claims, "This licence is suspended.");
        if (now < claims.ValidFrom) return new(LicenseState.Expired, claims, $"The licence becomes valid on {claims.ValidFrom:d}.");

        var grace = TimeSpan.FromDays(claims.GracePeriodDays);
        if (claims.ValidUntil is { } until && now > until)
        {
            var end = until + grace;
            return now <= end
                ? new(LicenseState.GracePeriod, claims, $"The licence expired on {until:d}. Grace period until {end:d}; please renew.", end)
                : new(LicenseState.Expired, claims, $"The licence expired on {until:d}. Projects remain accessible read-only; renew to edit.");
        }
        if (claims.RevalidationDays > 0)
        {
            var last = state.LastOnlineValidationUtc ?? claims.IssuedAt;
            var due = last.AddDays(claims.RevalidationDays);
            if (now > due)
            {
                var end = due + grace;
                return now <= end
                    ? new(LicenseState.GracePeriod, claims, $"Online revalidation was due on {due:d}. Connect to refresh before {end:d}.", end)
                    : new(LicenseState.Expired, claims, "The licence could not be revalidated online within the grace period. Refresh it to continue editing.");
            }
        }
        var expiry = claims.ValidUntil is { } u ? $" until {u:d}" : " (perpetual)";
        return new(LicenseState.Valid, claims, $"{claims.Subscription} licence for {claims.OrganizationName}{expiry}.");
    }

    private bool IsRevoked(string licenseId)
    {
        var rev = _store.LoadRevocations();
        if (rev is not { } r || !LicenseVerifier.VerifyObject(r.payload, r.signature, r.keyId, _keys)) return false;
        var list = JsonSerializer.Deserialize<RevocationList>(Convert.FromBase64String(r.payload), LicenseJson.Options);
        return list?.RevokedLicenseIds.Contains(licenseId, StringComparer.OrdinalIgnoreCase) == true;
    }

    // ------------------------------------------------------------------ entitlements

    public IReadOnlyList<EngineeringModule> LicensedModules =>
        Current.AllowsFeatures && Current.Claims is { } c
            ? ModuleCatalog.All.Where(d => c.Includes(d.Module)).Select(d => d.Module).ToList()
            : Array.Empty<EngineeringModule>();

    public bool IsEnabled(EngineeringModule module) => Current.AllowsFeatures && Current.Claims?.Includes(module) == true;

    public void Demand(EngineeringModule module)
    {
        if (IsEnabled(module)) return;
        var name = ModuleCatalog.Get(module).Name;
        throw new LicenseException(Current.AllowsFeatures
            ? $"The {name} module ({ModuleCatalog.Code(module)}) is not included in your licence."
            : $"{name} requires a valid licence. {Current.Message}", module);
    }

    public ActivationRequest CreateActivationRequest(string licenseId, string user) => new()
    {
        LicenseId = licenseId, DeviceId = DeviceId, DeviceName = DeviceIdentity.DeviceName, User = user, CreatedAt = _clock.Now,
    };
}

/// <summary>Entitlement checks used by application services.</summary>
public interface IEntitlements
{
    bool IsEnabled(EngineeringModule module);
    void Demand(EngineeringModule module);
}

public sealed class LicenseException(string message, EngineeringModule? module = null) : Exception(message)
{
    public EngineeringModule? Module { get; } = module;
}
