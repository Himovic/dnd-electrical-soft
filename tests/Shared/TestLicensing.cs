using DND.Electrical.Licensing;

namespace DND.Electrical.TestSupport;

/// <summary>
/// Issues a genuinely signed licence with an ephemeral test key so that tests run through the same
/// verification and entitlement path as production (no licensing bypass).
/// </summary>
public static class TestLicensing
{
    private static readonly (string keyId, string privatePem, string publicPem) Key = LicenseCrypto.GenerateKeyPair();

    public static LicenseManager Create(string folder, params EngineeringModule[] modules)
    {
        var mods = modules.Length == 0 ? ModuleCatalog.All.Select(m => m.Code).ToList() : modules.Select(ModuleCatalog.Code).ToList();
        var mgr = new LicenseManager(new LicenseStore(folder), new TrustedKeys().Add(Key.publicPem), null, "test-device");
        var now = DateTimeOffset.UtcNow;
        mgr.Install(LicenseCrypto.Sign(new LicenseClaims
        {
            LicenseId = "LIC-TEST-SUITE", CustomerId = "C-TEST", OrganizationId = "O-TEST", OrganizationName = "Test suite", Owner = "tests",
            Subscription = SubscriptionType.Enterprise, Modules = mods, IssuedAt = now, ValidFrom = now.AddMinutes(-5), ValidUntil = now.AddDays(30),
        }, Key.privatePem, Key.keyId));
        return mgr;
    }
}
