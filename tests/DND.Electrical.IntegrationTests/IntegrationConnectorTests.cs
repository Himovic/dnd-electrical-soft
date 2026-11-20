using System.Net;
using System.Text.Json;
using DND.Electrical.Application.Services;
using DND.Electrical.Licensing;

namespace DND.Electrical.IntegrationTests;

internal sealed class CaptureHandler(HttpStatusCode status) : HttpMessageHandler
{
    public HttpRequestMessage? Request; public byte[] Body = Array.Empty<byte>();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request; Body = await request.Content!.ReadAsByteArrayAsync(ct);
        return new HttpResponseMessage(status);
    }
}

public class IntegrationConnectorTests
{
    [Fact]
    public async Task Erp_and_cmms_exports_write_parsable_files_from_project_data()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-OG-009");
        var svc = new IntegrationService(env.Licensing);
        var dir = env.PathFor("erp");
        var r = await svc.RunAsync("erp-bom-file", p, new ConnectorSettings(dir));
        Assert.True(r.Success, r.Message);
        var csv = File.ReadAllLines(r.Outputs.Single(o => o.EndsWith(".csv")));
        Assert.Equal(DND.Electrical.Reports.BomService.Build(p).Count + 1, csv.Length);
        using var bom = JsonDocument.Parse(File.ReadAllText(r.Outputs.Single(o => o.EndsWith(".json"))));
        Assert.True(bom.RootElement.GetProperty("demoData").GetBoolean());

        var a = await svc.RunAsync("cmms-assets", p, new ConnectorSettings(env.PathFor("cmms")));
        using var assets = JsonDocument.Parse(File.ReadAllText(a.Outputs.Single()));
        Assert.Equal(p.Devices.Count + p.Instruments.Count, assets.RootElement.GetProperty("assets").GetArrayLength());
    }

    [Fact]
    public async Task Webhook_posts_signed_json_and_reports_receiver_errors()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-MFG-006");
        var ok = new CaptureHandler(HttpStatusCode.Accepted);
        var svc = new IntegrationService(env.Licensing, new HttpClient(ok));
        var r = await svc.RunAsync("webhook", p, new ConnectorSettings("https://hooks.example.test/dnd", "tok", "s3cret"));
        Assert.True(r.Success, r.Message);
        Assert.Equal("Bearer", ok.Request!.Headers.Authorization!.Scheme);
        Assert.Equal(WebhookConnector.Sign(ok.Body, "s3cret"), ok.Request.Headers.GetValues("X-DND-Signature").Single());
        using var doc = JsonDocument.Parse(ok.Body);
        Assert.Equal("dnd.project.export", doc.RootElement.GetProperty("event").GetString());

        var bad = new IntegrationService(env.Licensing, new HttpClient(new CaptureHandler(HttpStatusCode.Unauthorized)));
        Assert.Contains("401", (await bad.RunAsync("webhook", p, new ConnectorSettings("https://hooks.example.test/dnd"))).Message);
        Assert.False((await svc.RunAsync("webhook", p, new ConnectorSettings("http://insecure.example.test/x"))).Success);
    }

    [Fact]
    public async Task Planned_connectors_cannot_run_and_the_module_is_enforced()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var svc = new IntegrationService(env.Licensing);
        Assert.Contains(svc.All, c => c.Id == "sap" && c.Status == ConnectorStatus.Planned);
        var r = await svc.RunAsync("sap", p, new ConnectorSettings("x"));
        Assert.False(r.Success);
        Assert.Contains("planned", r.Message);
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(env.PathFor("lim"), EngineeringModule.ELEC);
        await Assert.ThrowsAsync<LicenseException>(() => new IntegrationService(limited).RunAsync("erp-bom-file", p, new ConnectorSettings(env.PathFor("x"))));
        Assert.False(Directory.Exists(env.PathFor("x")));
    }
}
