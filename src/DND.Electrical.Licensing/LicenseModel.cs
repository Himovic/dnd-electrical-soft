using System.Text.Json;
using System.Text.Json.Serialization;

namespace DND.Electrical.Licensing;

public enum SubscriptionType { Monthly, Annual, Perpetual, PerpetualWithMaintenance, Enterprise, Trial, Evaluation }

public enum SeatModel { NamedUser, Device, Floating }

public enum LicenseStatus { Active, Suspended, Revoked }

/// <summary>Entitlements carried by a signed licence. The canonical JSON of this record is what gets signed.</summary>
public sealed record LicenseClaims
{
    public int SchemaVersion { get; init; } = 1;
    public string LicenseId { get; init; } = string.Empty;
    public string ProductId { get; init; } = Shared.ProductInfo.ProductId;
    public string CustomerId { get; init; } = string.Empty;
    public string OrganizationId { get; init; } = string.Empty;
    public string OrganizationName { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public SubscriptionType Subscription { get; init; }
    public SeatModel Seats { get; init; } = SeatModel.Device;
    public List<string> Modules { get; init; } = new();
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    /// <summary>End of validity; null for perpetual licences.</summary>
    public DateTimeOffset? ValidUntil { get; init; }
    /// <summary>End of maintenance (updates) for perpetual licences.</summary>
    public DateTimeOffset? MaintenanceUntil { get; init; }
    public int MaxUsers { get; init; } = 1;
    public int MaxDevices { get; init; } = 1;
    public int MaxConcurrent { get; init; }
    public bool OfflineAllowed { get; init; } = true;
    /// <summary>Days the licence keeps working after expiry or without online revalidation.</summary>
    public int GracePeriodDays { get; init; } = 14;
    /// <summary>Required online revalidation interval for online licences (0 = never).</summary>
    public int RevalidationDays { get; init; }
    public LicenseStatus Status { get; init; } = LicenseStatus.Active;
    /// <summary>Device binding (activated licences). Null = not bound (evaluation media, floating parent).</summary>
    public string? DeviceId { get; init; }
    public string? Notes { get; init; }

    public bool Includes(EngineeringModule m) => Modules.Contains(ModuleCatalog.Code(m), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Signed licence document (file extension .dndlic).</summary>
public sealed record SignedLicense
{
    public string Format { get; init; } = "dnd-license/1";
    public string KeyId { get; init; } = string.Empty;
    public string Algorithm { get; init; } = LicenseCrypto.Algorithm;
    /// <summary>Base64 of the canonical UTF-8 JSON claims (exact bytes that were signed).</summary>
    public string Payload { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;

    public string ToJson() => JsonSerializer.Serialize(this, LicenseJson.Indented);
    public static SignedLicense FromJson(string json) =>
        JsonSerializer.Deserialize<SignedLicense>(json, LicenseJson.Options) ?? throw new InvalidDataException("Empty licence file.");
}

/// <summary>Revoked licence identifiers, signed by the server (for offline revocation).</summary>
public sealed record RevocationList
{
    public DateTimeOffset IssuedAt { get; init; }
    public List<string> RevokedLicenseIds { get; init; } = new();
}

/// <summary>Short-lived lease for a floating (concurrent) seat.</summary>
public sealed record SeatLease
{
    public string LeaseId { get; init; } = string.Empty;
    public string LicenseId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Offline activation request exported by a workstation without internet access.</summary>
public sealed record ActivationRequest
{
    public string LicenseId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string ProductVersion { get; init; } = Shared.ProductInfo.VersionString;
}

public static class LicenseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };
}
