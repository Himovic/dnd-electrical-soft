using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DND.Electrical.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DND.Electrical.LicensingTests;

public sealed class ServerFixture : IDisposable
{
    public ServerFixture()
    {
        Folder = Directory.CreateTempSubdirectory("dnd-ls-").FullName;
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Licensing:Database", $"Data Source={Path.Combine(Folder, "lic.db")}");
            b.UseSetting("Licensing:SigningKeyPath", Path.Combine(Folder, "signing.pem"));
            b.UseSetting("Licensing:GenerateKeyIfMissing", "true");
            b.UseSetting("Licensing:BootstrapAdminPassword", "Correct-Horse-42!");
        });
        Client = Factory.CreateClient();
        PublicPem = Factory.Services.GetService(typeof(DND.Electrical.LicenseServer.SigningKey)) is DND.Electrical.LicenseServer.SigningKey k ? k.PublicPem : throw new InvalidOperationException();
    }

    public string Folder { get; }
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }
    public string PublicPem { get; }
    private static readonly JsonSerializerOptions Json = LicenseJson.Options;

    public async Task<HttpClient> Admin()
    {
        var c = Factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/admin/login", new { user = "admin", password = "Correct-Horse-42!" });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<(string id, string key)> CreateLicense(HttpClient admin, string seats = "Device", int devices = 1, int concurrent = 0, string[]? modules = null)
    {
        var cust = await (await admin.PostAsJsonAsync("/api/admin/customers", new { name = "Acme Engineering", email = "x@example.invalid", country = "FR", organization = "Acme Lyon" })).Content.ReadFromJsonAsync<JsonElement>();
        var custId = cust.GetProperty("customer").GetProperty("id").GetString();
        var r = await admin.PostAsJsonAsync("/api/admin/licenses", new
        {
            customerId = custId, owner = "Lead engineer", subscription = "Annual", seats, modules = modules ?? new[] { "ELEC", "AUTO" },
            validUntil = DateTimeOffset.UtcNow.AddDays(365), maxUsers = 5, maxDevices = devices, maxConcurrent = concurrent, offlineAllowed = true, gracePeriodDays = 14, revalidationDays = 30,
        }, Json);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, body);
        var lic = JsonDocument.Parse(body).RootElement;
        return (lic.GetProperty("id").GetString()!, lic.GetProperty("licenseKey").GetString()!);
    }

    public void Dispose()
    {
        Factory.Dispose();
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public class ServerTests(ServerFixture fx) : IClassFixture<ServerFixture>
{
    private LicenseManager Device(string deviceId, out TempFolder folder)
    {
        folder = new TempFolder();
        return new LicenseManager(new LicenseStore(folder.Path), new TrustedKeys().Add(fx.PublicPem), null, deviceId);
    }

    [Fact]
    public async Task Admin_api_requires_authentication_and_portal_is_served()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Client.GetAsync("/api/admin/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fx.Client.PostAsJsonAsync("/api/admin/login", new { user = "admin", password = "wrong" })).StatusCode);
        var html = await fx.Client.GetStringAsync("/");
        Assert.Contains("D&amp;D License Administration Portal", html);
    }

    [Fact]
    public async Task Activation_refresh_device_limit_and_revocation()
    {
        var admin = await fx.Admin();
        var (id, key) = await fx.CreateLicense(admin, devices: 1);
        var api = new LicenseServerClient(fx.Client);

        var dev1 = Device("device-AAA", out var f1);
        using (f1)
        {
            var act = await api.ActivateAsync(key, dev1.DeviceId, "WS-01", "alice");
            dev1.Install(act.License, onlineValidated: true);
            Assert.Equal(LicenseState.Valid, dev1.Current.State);
            Assert.True(dev1.IsEnabled(EngineeringModule.ELEC));
            Assert.False(dev1.IsEnabled(EngineeringModule.POWER));

            // Device limit
            var ex = await Assert.ThrowsAsync<LicenseException>(() => api.ActivateAsync(key, "device-BBB", "WS-02", "bob"));
            Assert.Contains("Device limit", ex.Message);
            // Re-activating the same device is allowed.
            await api.ActivateAsync(key, dev1.DeviceId, "WS-01", "alice");

            // Module upgrade through admin + refresh
            (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/modules", new { modules = new[] { "ELEC", "AUTO", "POWER" } })).EnsureSuccessStatusCode();
            dev1.Install((await api.RefreshAsync(id, dev1.DeviceId)).License, true);
            Assert.True(dev1.IsEnabled(EngineeringModule.POWER));

            // Revocation reaches the device through refresh (signed status) and the revocation list.
            (await admin.PostAsync($"/api/admin/licenses/{id}/revoke", null)).EnsureSuccessStatusCode();
            dev1.Install((await api.RefreshAsync(id, dev1.DeviceId)).License, true);
            Assert.Equal(LicenseState.Revoked, dev1.Current.State);
            Assert.False(dev1.IsEnabled(EngineeringModule.ELEC));
            var rev = await api.RevocationsAsync();
            Assert.True(LicenseVerifier.VerifyObject(rev.Payload, rev.Signature, rev.KeyId, new TrustedKeys().Add(fx.PublicPem)));
            await Assert.ThrowsAsync<LicenseException>(() => api.ActivateAsync(key, "device-CCC", "WS-03", "carol"));
        }
    }

    [Fact]
    public async Task Deactivation_frees_the_seat()
    {
        var admin = await fx.Admin();
        var (id, key) = await fx.CreateLicense(admin, devices: 1);
        var api = new LicenseServerClient(fx.Client);
        await api.ActivateAsync(key, "dev-X", "X", "u");
        await api.DeactivateAsync(id, "dev-X");
        var ok = await api.ActivateAsync(key, "dev-Y", "Y", "u");
        Assert.NotNull(ok.License);
        var acts = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{id}/activations");
        Assert.Equal(2, acts.GetArrayLength());
    }

    [Fact]
    public async Task Floating_seats_respect_concurrency()
    {
        var admin = await fx.Admin();
        var (id, _) = await fx.CreateLicense(admin, seats: "Floating", devices: 10, concurrent: 2);
        var api = new LicenseServerClient(fx.Client);
        var a = await api.CheckoutAsync(id, "d1", "u1");
        await api.CheckoutAsync(id, "d2", "u2");
        var ex = await Assert.ThrowsAsync<LicenseException>(() => api.CheckoutAsync(id, "d3", "u3"));
        Assert.Contains("concurrent seats", ex.Message);
        Assert.True(LicenseVerifier.VerifyObject(a.Lease.Payload, a.Lease.Signature, a.Lease.KeyId, new TrustedKeys().Add(fx.PublicPem)));
        var lease = JsonSerializer.Deserialize<SeatLease>(Convert.FromBase64String(a.Lease.Payload), LicenseJson.Options)!;
        await api.HeartbeatAsync(lease.LeaseId);
        await api.ReleaseAsync(lease.LeaseId);
        Assert.NotNull(await api.CheckoutAsync(id, "d3", "u3"));
    }

    [Fact]
    public async Task Offline_activation_roundtrip()
    {
        var admin = await fx.Admin();
        var (id, _) = await fx.CreateLicense(admin, devices: 2);
        var dev = Device("offline-device-01", out var f);
        using (f)
        {
            var request = dev.CreateActivationRequest(id, "field engineer");
            var r = await admin.PostAsJsonAsync("/api/admin/offline-activation", request, LicenseJson.Options);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            var lic = await r.Content.ReadFromJsonAsync<SignedLicense>(LicenseJson.Options);
            dev.Install(lic!);
            Assert.Equal(LicenseState.Valid, dev.Current.State);
            Assert.Equal("offline-device-01", dev.Current.Claims!.DeviceId);
        }
    }

    [Fact]
    public async Task Validation_audit_usage_and_csv()
    {
        var admin = await fx.Admin();
        var bad = await admin.PostAsJsonAsync("/api/admin/licenses", new { customerId = "nope", owner = "x", subscription = "Annual", seats = "Device", modules = new[] { "ELEC" }, validUntil = DateTimeOffset.UtcNow.AddDays(10), maxUsers = 1, maxDevices = 1, maxConcurrent = 0, offlineAllowed = true, gracePeriodDays = 0, revalidationDays = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var (id, _) = await fx.CreateLicense(admin);
        (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/extend", new { days = 30 })).EnsureSuccessStatusCode();
        var audit = await admin.GetStringAsync("/api/admin/audit");
        Assert.Contains("Extended 30 days", audit);
        var usage = await admin.GetFromJsonAsync<JsonElement>("/api/admin/usage");
        Assert.True(usage.GetArrayLength() > 0);
        var csv = await admin.GetStringAsync("/api/admin/reports/licenses.csv");
        Assert.StartsWith("LicenseId,Customer", csv);
        Assert.Contains(id, csv);
    }
}
