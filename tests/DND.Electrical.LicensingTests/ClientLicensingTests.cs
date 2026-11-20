using DND.Electrical.Licensing;
using DND.Electrical.Shared;

namespace DND.Electrical.LicensingTests;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}

public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("dnd-lic-").FullName;
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class ClientLicensingTests
{
    private static readonly (string id, string priv, string pub) Key = LicenseCrypto.GenerateKeyPair();
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static LicenseClaims Claims(params string[] modules) => new()
    {
        LicenseId = "LIC-TEST", CustomerId = "C1", OrganizationId = "O1", OrganizationName = "Test Org", Owner = "Owner",
        Subscription = SubscriptionType.Annual, Modules = modules.ToList(), IssuedAt = T0, ValidFrom = T0, ValidUntil = T0.AddDays(365), GracePeriodDays = 14,
    };

    private static (LicenseManager mgr, FakeClock clock) Manager(TempFolder f, DateTimeOffset? now = null, string device = "dev-1")
    {
        var clock = new FakeClock(now ?? T0.AddDays(1));
        return (new LicenseManager(new LicenseStore(f.Path), new TrustedKeys().Add(Key.pub), clock, device), clock);
    }

    [Fact]
    public void Bundled_evaluation_licence_verifies_with_product_key()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "resources", "Licensing", "evaluation.dndlic");
        var (claims, error) = LicenseVerifier.Verify(SignedLicense.FromJson(File.ReadAllText(path)), TrustedKeys.Product());
        Assert.Null(error);
        Assert.Equal(SubscriptionType.Evaluation, claims!.Subscription);
        Assert.Equal(12, claims.Modules.Count);
    }

    [Fact]
    public void No_licence_means_viewer_mode()
    {
        using var f = new TempFolder();
        var (m, _) = Manager(f);
        Assert.Equal(LicenseState.NotActivated, m.Current.State);
        Assert.False(m.IsEnabled(EngineeringModule.ELEC));
        Assert.Throws<LicenseException>(() => m.Demand(EngineeringModule.ELEC));
    }

    [Fact]
    public void Modules_are_enforced_individually()
    {
        using var f = new TempFolder();
        var (m, _) = Manager(f);
        m.Install(LicenseCrypto.Sign(Claims("ELEC", "AUTO"), Key.priv, Key.id));
        Assert.Equal(LicenseState.Valid, m.Current.State);
        m.Demand(EngineeringModule.ELEC);
        Assert.True(m.IsEnabled(EngineeringModule.AUTO));
        var ex = Assert.Throws<LicenseException>(() => m.Demand(EngineeringModule.POWER));
        Assert.Equal(EngineeringModule.POWER, ex.Module);
        Assert.Contains("not included", ex.Message);
    }

    [Fact]
    public void Tampered_or_foreign_licences_are_rejected()
    {
        using var f = new TempFolder();
        var (m, _) = Manager(f);
        var good = LicenseCrypto.Sign(Claims("ELEC"), Key.priv, Key.id);
        var tamperedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(LicenseCrypto.Canonical(Claims("ELEC", "POWER", "3D"))));
        Assert.Throws<LicenseException>(() => m.Install(good with { Payload = tamperedPayload }));
        var other = LicenseCrypto.GenerateKeyPair();
        Assert.Throws<LicenseException>(() => m.Install(LicenseCrypto.Sign(Claims("ELEC"), other.privatePem, other.keyId)));
        // Editing the stored file directly is detected at evaluation time.
        m.Install(good);
        File.WriteAllText(m.Store.LicensePath, (good with { Payload = tamperedPayload }).ToJson());
        Assert.Equal(LicenseState.InvalidSignature, m.Refresh().State);
        Assert.False(m.IsEnabled(EngineeringModule.ELEC));
    }

    [Fact]
    public void Expiry_grace_period_and_read_only_after_grace()
    {
        using var f = new TempFolder();
        var (m, clock) = Manager(f);
        m.Install(LicenseCrypto.Sign(Claims("ELEC"), Key.priv, Key.id));
        clock.Now = T0.AddDays(370);
        Assert.Equal(LicenseState.GracePeriod, m.Refresh().State);
        Assert.True(m.IsEnabled(EngineeringModule.ELEC));
        clock.Now = T0.AddDays(380);
        Assert.Equal(LicenseState.Expired, m.Refresh().State);
        Assert.False(m.IsEnabled(EngineeringModule.ELEC));
    }

    [Fact]
    public void Clock_rollback_is_detected()
    {
        using var f = new TempFolder();
        var (m, clock) = Manager(f, T0.AddDays(100));
        m.Install(LicenseCrypto.Sign(Claims("ELEC"), Key.priv, Key.id));
        clock.Now = T0.AddDays(10);
        Assert.Equal(LicenseState.ClockTampered, m.Refresh().State);
        Assert.False(m.IsEnabled(EngineeringModule.ELEC));
    }

    [Fact]
    public void Device_bound_licence_only_works_on_its_device()
    {
        using var f = new TempFolder();
        var (m, _) = Manager(f, device: "dev-1");
        Assert.Throws<LicenseException>(() => m.Install(LicenseCrypto.Sign(Claims("ELEC") with { DeviceId = "dev-2" }, Key.priv, Key.id)));
        m.Install(LicenseCrypto.Sign(Claims("ELEC") with { DeviceId = "dev-1" }, Key.priv, Key.id));
        Assert.Equal(LicenseState.Valid, m.Current.State);
        using var f2 = new TempFolder();
        File.Copy(m.Store.LicensePath, System.IO.Path.Combine(f2.Path, "license.dndlic"));
        var (copy, _) = Manager(f2, device: "dev-9");
        Assert.Equal(LicenseState.WrongDevice, copy.Current.State);
    }

    [Fact]
    public void Online_revalidation_deadline_with_grace()
    {
        using var f = new TempFolder();
        var (m, clock) = Manager(f);
        m.Install(LicenseCrypto.Sign(Claims("ELEC") with { RevalidationDays = 30, OfflineAllowed = false, GracePeriodDays = 7 }, Key.priv, Key.id), onlineValidated: true);
        clock.Now = T0.AddDays(35);
        Assert.Equal(LicenseState.GracePeriod, m.Refresh().State);
        clock.Now = T0.AddDays(40);
        Assert.Equal(LicenseState.Expired, m.Refresh().State);
    }

    [Fact]
    public void Signed_revocation_list_revokes_offline()
    {
        using var f = new TempFolder();
        var (m, _) = Manager(f);
        m.Install(LicenseCrypto.Sign(Claims("ELEC"), Key.priv, Key.id));
        var (payload, sig) = LicenseCrypto.SignObject(new RevocationList { IssuedAt = T0, RevokedLicenseIds = { "LIC-TEST" } }, Key.priv);
        Assert.Throws<LicenseException>(() => m.InstallRevocationList(new SignedEnvelope(Key.id, payload, "AAAA")));
        m.InstallRevocationList(new SignedEnvelope(Key.id, payload, sig));
        Assert.Equal(LicenseState.Revoked, m.Current.State);
    }

    [Fact]
    public void Device_identity_is_stable_and_not_reversible()
    {
        using var f = new TempFolder();
        var a = DeviceIdentity.Compute(f.Path);
        Assert.Equal(a, DeviceIdentity.Compute(f.Path));
        Assert.Equal(32, a.Length);
        Assert.DoesNotContain(Environment.MachineName.ToLowerInvariant(), a);
    }
}
