using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Reports;

namespace DND.Electrical.Application.Services;

public enum ConnectorCategory { Erp, Plm, Cmms, Dms, Bim, Scada, Generic }

/// <summary>Available = implemented and tested; Planned = listed in the roadmap, cannot be executed.</summary>
public enum ConnectorStatus { Available, Planned }

public sealed record ConnectorInfo(string Id, string Name, ConnectorCategory Category, ConnectorStatus Status, string Description, string TargetHint);

/// <summary>Target and credentials of one connector run. Secrets are read from secure storage by the caller.</summary>
public sealed record ConnectorSettings(string Target, string? Token = null, string? SigningSecret = null);

public sealed record ConnectorResult(bool Success, string Message, IReadOnlyList<string> Outputs);

public interface IIntegrationConnector
{
    ConnectorInfo Info { get; }
    Task<ConnectorResult> ExportAsync(ElectricalProject project, ConnectorSettings settings, CancellationToken ct = default);
}

/// <summary>Shared export payloads (BOM and asset register) built from project data only.</summary>
public static class IntegrationPayloads
{
    public static object Bom(ElectricalProject p) => new
    {
        project = p.Info.Number, revision = p.Info.CurrentRevision, generated = DateTimeOffset.UtcNow, demoData = p.Info.IsDemo,
        lines = BomService.Build(p).Select(l => new
        {
            l.Manufacturer, l.PartNumber, l.Description, l.Category, l.Quantity, l.Unit, l.UnitPrice, l.Currency, l.Supplier, status = l.Status.ToString(), l.Tags, l.IsDemoData,
        }),
    };

    /// <summary>Maintainable assets: devices with location, product, maintenance history and next due date; instruments with calibration due.</summary>
    public static object Assets(ElectricalProject p) => new
    {
        project = p.Info.Number, site = p.Info.Site, generated = DateTimeOffset.UtcNow, demoData = p.Info.IsDemo,
        assets = p.Devices.OrderBy(d => d.Tag, StringComparer.OrdinalIgnoreCase).Select(d => new
        {
            id = d.Id, tag = d.Tag, function = d.Function, location = d.Location, manufacturer = d.Manufacturer, partNumber = d.PartNumber,
            lastMaintenance = p.Maintenance.Where(m => m.ObjectId == d.Id).OrderByDescending(m => m.Date).Select(m => (DateTimeOffset?)m.Date).FirstOrDefault(),
            nextDue = p.Maintenance.Where(m => m.ObjectId == d.Id && m.NextDue is not null).Select(m => m.NextDue).OrderBy(x => x).FirstOrDefault(),
            commissioning = p.Commissioning.Where(c => c.ObjectId == d.Id).Select(c => new { c.Test, result = c.Result.ToString(), c.Date }),
        }).Concat(p.Instruments.OrderBy(i => i.Tag).Select(i => new
        {
            id = i.Id, tag = i.Tag, function = i.Description, location = i.Area, manufacturer = i.Manufacturer, partNumber = i.Model,
            lastMaintenance = (DateTimeOffset?)null, nextDue = i.CalibrationDue, commissioning = Enumerable.Empty<object>().Select(_ => new { Test = "", result = "", Date = (DateTimeOffset?)null }),
        })),
    };

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>ERP import by file drop: BOM as CSV (semicolon, invariant culture) and JSON in a target folder.</summary>
public sealed class ErpBomFileConnector : IIntegrationConnector
{
    public ConnectorInfo Info { get; } = new("erp-bom-file", "ERP – BOM file drop", ConnectorCategory.Erp, ConnectorStatus.Available,
        "Writes the bill of materials (CSV and JSON) to a folder watched by the ERP import job.", "Target folder");

    public Task<ConnectorResult> ExportAsync(ElectricalProject p, ConnectorSettings s, CancellationToken ct = default)
    {
        Directory.CreateDirectory(s.Target);
        var stem = Path.Combine(s.Target, $"{Safe(p.Info.Number)}-{Safe(p.Info.CurrentRevision)}-BOM");
        var sb = new StringBuilder("Manufacturer;PartNumber;Description;Quantity;Unit;UnitPrice;Currency;Supplier;Status;Tags;DemoData\n");
        foreach (var l in BomService.Build(p))
            sb.AppendLine(string.Join(';', new[] { l.Manufacturer, l.PartNumber, l.Description, l.Quantity.ToString(CultureInfo.InvariantCulture), l.Unit, l.UnitPrice.ToString(CultureInfo.InvariantCulture), l.Currency, l.Supplier, l.Status.ToString(), string.Join(' ', l.Tags), l.IsDemoData ? "yes" : "no" }.Select(Csv)));
        File.WriteAllText(stem + ".csv", sb.ToString(), new UTF8Encoding(true));
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(IntegrationPayloads.Bom(p), IntegrationPayloads.Json));
        p.Audit("Integration", $"BOM exported to {s.Target}");
        return Task.FromResult(new ConnectorResult(true, $"BOM written to {s.Target}.", new[] { stem + ".csv", stem + ".json" }));
    }

    internal static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static string Csv(string v) => v.Contains(';') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
}

/// <summary>CMMS asset register export (JSON) for import into a maintenance management system.</summary>
public sealed class CmmsAssetRegisterConnector : IIntegrationConnector
{
    public ConnectorInfo Info { get; } = new("cmms-assets", "CMMS – asset register", ConnectorCategory.Cmms, ConnectorStatus.Available,
        "Writes the maintainable asset register (devices, instruments, maintenance and commissioning history) as JSON.", "Target folder");

    public Task<ConnectorResult> ExportAsync(ElectricalProject p, ConnectorSettings s, CancellationToken ct = default)
    {
        Directory.CreateDirectory(s.Target);
        var path = Path.Combine(s.Target, $"{ErpBomFileConnector.Safe(p.Info.Number)}-assets.json");
        File.WriteAllText(path, JsonSerializer.Serialize(IntegrationPayloads.Assets(p), IntegrationPayloads.Json));
        p.Audit("Integration", $"Asset register exported to {s.Target}");
        return Task.FromResult(new ConnectorResult(true, $"Asset register written ({p.Devices.Count + p.Instruments.Count} assets).", new[] { path }));
    }
}

/// <summary>
/// Generic HTTPS webhook: POSTs a JSON document (project, BOM, assets) to the target URL. Optional bearer token and
/// HMAC-SHA256 body signature (header X-DND-Signature: sha256=&lt;hex&gt;) so the receiver can verify integrity.
/// </summary>
public sealed class WebhookConnector(HttpClient? http = null) : IIntegrationConnector
{
    public ConnectorInfo Info { get; } = new("webhook", "Generic webhook (HTTPS POST)", ConnectorCategory.Generic, ConnectorStatus.Available,
        "Posts project, BOM and asset data as JSON to a URL (e.g. an integration platform or middleware).", "https:// URL");

    public static string Sign(byte[] body, string secret) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public async Task<ConnectorResult> ExportAsync(ElectricalProject p, ConnectorSettings s, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(s.Target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            return new ConnectorResult(false, "The webhook URL must be an absolute https:// address.", Array.Empty<string>());
        var body = JsonSerializer.SerializeToUtf8Bytes(new { @event = "dnd.project.export", project = new { p.Info.Number, p.Info.Name, revision = p.Info.CurrentRevision, demo = p.Info.IsDemo }, bom = IntegrationPayloads.Bom(p), assets = IntegrationPayloads.Assets(p) }, IntegrationPayloads.Json);
        using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (!string.IsNullOrEmpty(s.Token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        if (!string.IsNullOrEmpty(s.SigningSecret)) req.Headers.Add("X-DND-Signature", Sign(body, s.SigningSecret));
        var client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            var ok = resp.IsSuccessStatusCode;
            p.Audit("Integration", $"Webhook {uri.Host}: HTTP {(int)resp.StatusCode}");
            return new ConnectorResult(ok, ok ? $"Delivered ({body.Length:N0} bytes, HTTP {(int)resp.StatusCode})." : $"Receiver answered HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}.", Array.Empty<string>());
        }
        catch (HttpRequestException ex) { return new ConnectorResult(false, $"Delivery failed: {ex.Message}", Array.Empty<string>()); }
        catch (TaskCanceledException) { return new ConnectorResult(false, "Delivery timed out.", Array.Empty<string>()); }
        finally { if (http is null) client.Dispose(); }
    }
}

/// <summary>Registered connectors. Planned connectors are listed for transparency and cannot be executed.</summary>
public sealed class IntegrationService(IEntitlements entitlements, HttpClient? http = null)
{
    public IReadOnlyList<IIntegrationConnector> Available { get; } = new IIntegrationConnector[] { new ErpBomFileConnector(), new CmmsAssetRegisterConnector(), new WebhookConnector(http) };

    public static IReadOnlyList<ConnectorInfo> Planned { get; } = new[]
    {
        new ConnectorInfo("sap", "SAP S/4HANA (material master, purchase requisitions)", ConnectorCategory.Erp, ConnectorStatus.Planned, "Requires SAP OData/BAPI access and customer-specific mapping.", ""),
        new ConnectorInfo("plm", "PLM systems (e.g. Teamcenter, Windchill)", ConnectorCategory.Plm, ConnectorStatus.Planned, "Requires vendor APIs and licences.", ""),
        new ConnectorInfo("maximo", "IBM Maximo (direct API)", ConnectorCategory.Cmms, ConnectorStatus.Planned, "Use the CMMS asset register export until then.", ""),
        new ConnectorInfo("sharepoint", "SharePoint / Microsoft 365 document libraries", ConnectorCategory.Dms, ConnectorStatus.Planned, "Requires Microsoft Graph application registration.", ""),
        new ConnectorInfo("opcua", "OPC UA (digital twin)", ConnectorCategory.Scada, ConnectorStatus.Planned, "Requires the OPC UA stack with certificate management.", ""),
    };

    public IEnumerable<ConnectorInfo> All => Available.Select(c => c.Info).Concat(Planned);

    public Task<ConnectorResult> RunAsync(string connectorId, ElectricalProject p, ConnectorSettings settings, CancellationToken ct = default)
    {
        entitlements.Demand(EngineeringModule.INTEGRATION);
        var c = Available.FirstOrDefault(x => x.Info.Id == connectorId);
        if (c is null)
            return Task.FromResult(new ConnectorResult(false, Planned.Any(x => x.Id == connectorId) ? "This connector is planned and not available in this version." : $"Unknown connector '{connectorId}'.", Array.Empty<string>()));
        if (string.IsNullOrWhiteSpace(settings.Target)) return Task.FromResult(new ConnectorResult(false, $"{c.Info.TargetHint} is required.", Array.Empty<string>()));
        return c.ExportAsync(p, settings, ct);
    }
}
