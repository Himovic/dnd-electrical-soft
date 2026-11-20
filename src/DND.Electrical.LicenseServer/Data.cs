using DND.Electrical.Licensing;
using Microsoft.EntityFrameworkCore;

namespace DND.Electrical.LicenseServer;

public sealed class Customer
{
    public string Id { get; set; } = "CUST-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Organization
{
    public string Id { get; set; } = "ORG-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string CustomerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class LicenseRecord
{
    public string Id { get; set; } = LicenseIssuer.NewLicenseId();
    /// <summary>Activation key given to the customer (random, not derived from the id).</summary>
    public string LicenseKey { get; set; } = KeyGen.ActivationKey();
    public string CustomerId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public SubscriptionType Subscription { get; set; }
    public SeatModel Seats { get; set; } = SeatModel.Device;
    public string Modules { get; set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? MaintenanceUntil { get; set; }
    public int MaxUsers { get; set; } = 1;
    public int MaxDevices { get; set; } = 1;
    public int MaxConcurrent { get; set; }
    public bool OfflineAllowed { get; set; } = true;
    public int GracePeriodDays { get; set; } = 14;
    public int RevalidationDays { get; set; } = 30;
    public LicenseStatus Status { get; set; } = LicenseStatus.Active;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public List<string> ModuleList => Modules.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}

public sealed class Activation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LicenseId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public bool Offline { get; set; }
    public DateTimeOffset ActivatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeactivatedAt { get; set; }
}

public sealed class Lease
{
    public string Id { get; set; } = "LEASE-" + Guid.NewGuid().ToString("N");
    public string LicenseId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Released { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class AdminUser
{
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Administrator";
}

public sealed class AdminSession
{
    public string TokenHash { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class LicensingDb(DbContextOptions<LicensingDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    public DbSet<Activation> Activations => Set<Activation>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<AdminUser> Admins => Set<AdminUser>();
    public DbSet<AdminSession> Sessions => Set<AdminSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LicenseRecord>(e =>
        {
            e.HasIndex(x => x.LicenseKey).IsUnique();
            e.Property(x => x.Subscription).HasConversion<string>();
            e.Property(x => x.Seats).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Ignore(x => x.ModuleList);
        });
        b.Entity<Activation>().HasIndex(x => new { x.LicenseId, x.DeviceId });
        b.Entity<Lease>().HasIndex(x => x.LicenseId);
        b.Entity<AdminUser>().HasKey(x => x.UserName);
        b.Entity<AdminSession>().HasKey(x => x.TokenHash);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                prop.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToStringConverter());
    }
}

public static class KeyGen
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string ActivationKey()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
        var chars = bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray();
        return string.Join("-", Enumerable.Range(0, 5).Select(i => new string(chars, i * 4, 4)));
    }

    public static string HashPassword(string password)
    {
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(parts[1]), System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string Sha256(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
}
