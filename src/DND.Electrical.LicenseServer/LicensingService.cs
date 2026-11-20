using DND.Electrical.Licensing;
using Microsoft.EntityFrameworkCore;

namespace DND.Electrical.LicenseServer;

/// <summary>Holds the private signing key (server only).</summary>
public sealed class SigningKey
{
    private SigningKey(string privatePem)
    {
        PrivatePem = privatePem;
        using var ec = System.Security.Cryptography.ECDsa.Create();
        ec.ImportFromPem(privatePem);
        PublicPem = ec.ExportSubjectPublicKeyInfoPem();
        KeyId = LicenseCrypto.KeyIdOf(PublicPem);
    }

    public string PrivatePem { get; }
    public string PublicPem { get; }
    public string KeyId { get; }

    public static SigningKey Load(string? path, bool generateIfMissing)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Configure Licensing:SigningKeyPath (PKCS#8 PEM ECDSA P-256 private key).");
        if (!File.Exists(path))
        {
            if (!generateIfMissing) throw new InvalidOperationException($"Signing key not found at {path}.");
            var (_, priv, _) = LicenseCrypto.GenerateKeyPair();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, priv);
        }
        return new SigningKey(File.ReadAllText(path));
    }

    public static SigningKey FromPem(string privatePem) => new(privatePem);
}

/// <summary>Licensing business rules: activation limits, renewals, floating seats, offline activation, revocation.</summary>
public sealed class LicensingService(LicensingDb db, SigningKey key, IConfiguration config)
{
    private TimeSpan LeaseDuration => TimeSpan.FromMinutes(config.GetValue("Licensing:LeaseMinutes", 30));

    private LicenseClaims Claims(LicenseRecord l, string? deviceId) => new()
    {
        LicenseId = l.Id, CustomerId = l.CustomerId, OrganizationId = l.OrganizationId, OrganizationName = l.OrganizationName, Owner = l.Owner,
        Subscription = l.Subscription, Seats = l.Seats, Modules = l.ModuleList, IssuedAt = DateTimeOffset.UtcNow, ValidFrom = l.ValidFrom,
        ValidUntil = l.ValidUntil, MaintenanceUntil = l.MaintenanceUntil, MaxUsers = l.MaxUsers, MaxDevices = l.MaxDevices, MaxConcurrent = l.MaxConcurrent,
        OfflineAllowed = l.OfflineAllowed, GracePeriodDays = l.GracePeriodDays, RevalidationDays = l.OfflineAllowed ? 0 : l.RevalidationDays,
        Status = l.Status, DeviceId = deviceId, Notes = string.IsNullOrEmpty(l.Notes) ? null : l.Notes,
    };

    private SignedLicense Sign(LicenseRecord l, string? deviceId) => LicenseCrypto.Sign(Claims(l, deviceId), key.PrivatePem, key.KeyId);

    private void Audit(string actor, string action, string licenseId, string details = "") =>
        db.Audit.Add(new AuditEntry { Actor = actor, Action = action, LicenseId = licenseId, Details = details });

    public async Task<(LicenseRecord?, string?)> Create(CreateLicenseBody b, string actor)
    {
        var cust = await db.Customers.FindAsync(b.CustomerId);
        if (cust is null) return (null, "Unknown customer.");
        var org = b.OrganizationId is null ? await db.Organizations.FirstOrDefaultAsync(o => o.CustomerId == cust.Id) : await db.Organizations.FindAsync(b.OrganizationId);
        if (org is null || org.CustomerId != cust.Id) return (null, "Unknown organization for this customer.");
        if (b.Modules.Count == 0) return (null, "Select at least one module.");
        foreach (var m in b.Modules) if (!ModuleCatalog.TryParse(m, out _)) return (null, $"Unknown module {m}.");
        if (b.Seats == SeatModel.Floating && b.MaxConcurrent < 1) return (null, "Floating licences need a concurrent-user limit.");
        var perpetual = b.Subscription is SubscriptionType.Perpetual or SubscriptionType.PerpetualWithMaintenance;
        if (!perpetual && b.ValidUntil is null) return (null, "Subscriptions, trials and evaluations need an expiry date.");
        var l = new LicenseRecord
        {
            CustomerId = cust.Id, OrganizationId = org.Id, OrganizationName = org.Name, Owner = b.Owner, Subscription = b.Subscription, Seats = b.Seats,
            Modules = string.Join(",", b.Modules.Select(m => m.ToUpperInvariant())), ValidFrom = b.ValidFrom ?? DateTimeOffset.UtcNow,
            ValidUntil = perpetual ? null : b.ValidUntil, MaintenanceUntil = b.Subscription == SubscriptionType.PerpetualWithMaintenance ? (b.ValidUntil ?? DateTimeOffset.UtcNow.AddYears(1)) : null,
            MaxUsers = Math.Max(1, b.MaxUsers), MaxDevices = Math.Max(1, b.MaxDevices), MaxConcurrent = b.MaxConcurrent, OfflineAllowed = b.OfflineAllowed,
            GracePeriodDays = Math.Clamp(b.GracePeriodDays, 0, 90), RevalidationDays = Math.Clamp(b.RevalidationDays, 0, 365), Notes = b.Notes ?? "",
        };
        db.Licenses.Add(l);
        Audit(actor, "Licence created", l.Id, $"{l.Subscription} {l.Modules} for {l.OrganizationName}");
        await db.SaveChangesAsync();
        return (l, null);
    }

    public async Task<bool> Update(string id, string actor, string action, Action<LicenseRecord> change)
    {
        var l = await db.Licenses.FindAsync(id);
        if (l is null) return false;
        change(l);
        Audit(actor, action, id);
        await db.SaveChangesAsync();
        return true;
    }

    private static string? CheckUsable(LicenseRecord l)
    {
        if (l.Status == LicenseStatus.Revoked) return "The licence has been revoked.";
        if (l.Status == LicenseStatus.Suspended) return "The licence is suspended.";
        if (l.ValidUntil is { } u && u.AddDays(l.GracePeriodDays) < DateTimeOffset.UtcNow) return "The licence has expired.";
        return null;
    }

    public async Task<(ActivationResponse?, string?)> Activate(ActivateBody b)
    {
        var l = await db.Licenses.FirstOrDefaultAsync(x => x.LicenseKey == b.LicenseKey);
        if (l is null) return (null, "Unknown licence key.");
        if (CheckUsable(l) is { } err) return (null, err);
        if (string.IsNullOrWhiteSpace(b.DeviceId) || b.DeviceId.Length > 128) return (null, "Invalid device identifier.");
        var active = await db.Activations.Where(a => a.LicenseId == l.Id && a.DeactivatedAt == null).ToListAsync();
        var existing = active.FirstOrDefault(a => a.DeviceId == b.DeviceId);
        if (existing is null)
        {
            if (l.Seats != SeatModel.Floating && active.Count >= l.MaxDevices)
                return (null, $"Device limit reached ({l.MaxDevices}). Deactivate another device first.");
            db.Activations.Add(new Activation { LicenseId = l.Id, DeviceId = b.DeviceId, DeviceName = b.DeviceName, User = b.User });
        }
        else existing.LastSeenAt = DateTimeOffset.UtcNow;
        Audit(b.User, "Activated", l.Id, $"{b.DeviceName} {b.DeviceId}");
        await db.SaveChangesAsync();
        return (new ActivationResponse(Sign(l, l.Seats == SeatModel.Floating ? null : b.DeviceId), "Licence activated."), null);
    }

    public async Task<(ActivationResponse?, string?)> Refresh(string licenseId, string deviceId)
    {
        var l = await db.Licenses.FindAsync(licenseId);
        if (l is null) return (null, "Unknown licence.");
        var act = await db.Activations.FirstOrDefaultAsync(a => a.LicenseId == licenseId && a.DeviceId == deviceId && a.DeactivatedAt == null);
        if (act is null) return (null, "This device is not activated for the licence.");
        act.LastSeenAt = DateTimeOffset.UtcNow;
        Audit(act.User, "Refreshed", l.Id, deviceId);
        await db.SaveChangesAsync();
        // Revoked/suspended status is delivered inside the signed licence so the client locks itself.
        return (new ActivationResponse(Sign(l, l.Seats == SeatModel.Floating ? null : deviceId), l.Status == LicenseStatus.Active ? "Licence refreshed." : $"Licence status: {l.Status}."), null);
    }

    public async Task<(ActivationResponse?, string?)> Deactivate(string licenseId, string deviceId, string actor)
    {
        var l = await db.Licenses.FindAsync(licenseId);
        if (l is null) return (null, "Unknown licence.");
        var act = await db.Activations.FirstOrDefaultAsync(a => a.LicenseId == licenseId && a.DeviceId == deviceId && a.DeactivatedAt == null);
        if (act is null) return (null, "This device is not activated.");
        act.DeactivatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "Deactivated", l.Id, deviceId);
        await db.SaveChangesAsync();
        return (new ActivationResponse(Sign(l, deviceId), "Device deactivated; the seat is free."), null);
    }

    public async Task<(LeaseResponse?, string?)> Checkout(CheckoutBody b)
    {
        var l = await db.Licenses.FindAsync(b.LicenseId);
        if (l is null) return (null, "Unknown licence.");
        if (l.Seats != SeatModel.Floating) return (null, "Not a floating licence.");
        if (CheckUsable(l) is { } err) return (null, err);
        var now = DateTimeOffset.UtcNow;
        var live = (await db.Leases.Where(x => x.LicenseId == l.Id && !x.Released).ToListAsync()).Where(x => x.ExpiresAt > now).ToList();
        var lease = live.FirstOrDefault(x => x.DeviceId == b.DeviceId);
        if (lease is null)
        {
            if (live.Count >= l.MaxConcurrent) return (null, $"All {l.MaxConcurrent} concurrent seats are in use. Try again later.");
            lease = new Lease { LicenseId = l.Id, DeviceId = b.DeviceId, User = b.User };
            db.Leases.Add(lease);
        }
        lease.ExpiresAt = now + LeaseDuration;
        Audit(b.User, "Seat checked out", l.Id, b.DeviceId);
        await db.SaveChangesAsync();
        return (new LeaseResponse(SignLease(lease), $"Seat granted until {lease.ExpiresAt:t} UTC."), null);
    }

    public async Task<(LeaseResponse?, string?)> Heartbeat(string leaseId)
    {
        var lease = await db.Leases.FindAsync(leaseId);
        if (lease is null || lease.Released || lease.ExpiresAt < DateTimeOffset.UtcNow) return (null, "Lease expired; check out a seat again.");
        var l = await db.Licenses.FindAsync(lease.LicenseId);
        if (l is null || CheckUsable(l) is not null) return (null, "The licence is no longer valid.");
        lease.ExpiresAt = DateTimeOffset.UtcNow + LeaseDuration;
        await db.SaveChangesAsync();
        return (new LeaseResponse(SignLease(lease), "Lease extended."), null);
    }

    public async Task<(LeaseResponse?, string?)> Release(string leaseId)
    {
        var lease = await db.Leases.FindAsync(leaseId);
        if (lease is null) return (null, "Unknown lease.");
        lease.Released = true;
        Audit(lease.User, "Seat released", lease.LicenseId, lease.DeviceId);
        await db.SaveChangesAsync();
        return (new LeaseResponse(SignLease(lease), "Seat released."), null);
    }

    private SignedEnvelope SignLease(Lease lease)
    {
        var (payload, sig) = LicenseCrypto.SignObject(new SeatLease { LeaseId = lease.Id, LicenseId = lease.LicenseId, DeviceId = lease.DeviceId, User = lease.User, ExpiresAt = lease.ExpiresAt }, key.PrivatePem);
        return new SignedEnvelope(key.KeyId, payload, sig);
    }

    public async Task<(SignedLicense?, string?)> OfflineActivate(ActivationRequest req, string actor)
    {
        var l = await db.Licenses.FindAsync(req.LicenseId);
        if (l is null) return (null, "Unknown licence.");
        if (!l.OfflineAllowed) return (null, "This licence does not allow offline activation.");
        if (CheckUsable(l) is { } err) return (null, err);
        var active = await db.Activations.Where(a => a.LicenseId == l.Id && a.DeactivatedAt == null).ToListAsync();
        if (active.All(a => a.DeviceId != req.DeviceId))
        {
            if (active.Count >= l.MaxDevices) return (null, $"Device limit reached ({l.MaxDevices}).");
            db.Activations.Add(new Activation { LicenseId = l.Id, DeviceId = req.DeviceId, DeviceName = req.DeviceName, User = req.User, Offline = true });
        }
        Audit(actor, "Offline activation", l.Id, $"{req.DeviceName} {req.DeviceId}");
        await db.SaveChangesAsync();
        return (Sign(l, req.DeviceId), null);
    }

    public async Task<SignedEnvelope> Revocations()
    {
        var ids = await db.Licenses.Where(l => l.Status == LicenseStatus.Revoked).Select(l => l.Id).ToListAsync();
        var (payload, sig) = LicenseCrypto.SignObject(new RevocationList { IssuedAt = DateTimeOffset.UtcNow, RevokedLicenseIds = ids }, key.PrivatePem);
        return new SignedEnvelope(key.KeyId, payload, sig);
    }
}
