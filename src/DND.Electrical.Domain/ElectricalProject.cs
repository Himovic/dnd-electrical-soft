using System.Text.Json;
using System.Text.Json.Serialization;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;

namespace DND.Electrical.Domain;

/// <summary>
/// Aggregate root of an electrical engineering project. All engineering data lives here;
/// persistence, rendering and UI are layered on top.
/// </summary>
public sealed class ElectricalProject
{
    private readonly Dictionary<string, SymbolDefinition> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

    public Guid Id { get; set; } = Guid.NewGuid();
    public int FormatVersion { get; set; } = ProductInfo.ProjectFormatVersion;
    public ProjectInfo Info { get; set; } = new();
    public ProjectSettings Settings { get; set; } = new();

    public List<ProjectRevision> Revisions { get; set; } = new();
    public List<Sheet> Sheets { get; set; } = new();
    public List<Device> Devices { get; set; } = new();
    public List<Cable> Cables { get; set; } = new();
    public List<TerminalStrip> TerminalStrips { get; set; } = new();
    public List<PlcStation> PlcStations { get; set; } = new();
    public List<Motor> Motors { get; set; } = new();
    public List<Cabinet> Cabinets { get; set; } = new();
    public List<CatalogProduct> Catalog { get; set; } = new();
    public List<ProcurementEntry> Procurement { get; set; } = new();
    public List<ElectricalLoad> Loads { get; set; } = new();
    public List<CalculationRecord> Calculations { get; set; } = new();
    public List<Attachment> Attachments { get; set; } = new();
    public List<AuditEvent> AuditEvents { get; set; } = new();
    public List<ValidationRunRecord> ValidationRuns { get; set; } = new();
    public List<ProjectSymbol> Symbols { get; set; } = new();

    // ---- lifecycle and project management
    public List<ProjectMember> Members { get; set; } = new();
    public List<ProjectTask> Tasks { get; set; } = new();
    public List<Milestone> Milestones { get; set; } = new();
    public List<EngineeringIssue> Issues { get; set; } = new();
    public List<ApprovalRecord> Approvals { get; set; } = new();
    public List<CommentRecord> Comments { get; set; } = new();
    public List<DocumentRegisterEntry> DocumentRegister { get; set; } = new();
    public List<MaintenanceRecord> Maintenance { get; set; } = new();
    public List<CommissioningRecord> Commissioning { get; set; } = new();

    // ------------------------------------------------------------------ lookups

    [JsonIgnore]
    public IEnumerable<Sheet> OrderedSheets => Sheets.OrderBy(s => s.Number);

    public Sheet? FindSheet(Guid id) => Sheets.FirstOrDefault(s => s.Id == id);
    public Device? FindDevice(Guid id) => Devices.FirstOrDefault(d => d.Id == id);

    public Device? FindDeviceByTag(string tag) =>
        Devices.FirstOrDefault(d => string.Equals(d.Tag, tag, StringComparison.OrdinalIgnoreCase));

    public Cable? FindCable(Guid id) => Cables.FirstOrDefault(c => c.Id == id);
    public TerminalStrip? FindTerminalStrip(string tag) =>
        TerminalStrips.FirstOrDefault(t => string.Equals(t.Tag, tag, StringComparison.OrdinalIgnoreCase));

    public PlcChannel? FindPlcChannel(Guid id) => PlcStations.SelectMany(p => p.Channels).FirstOrDefault(c => c.Id == id);

    public CatalogProduct? FindProduct(string manufacturer, string partNumber) =>
        Catalog.FirstOrDefault(p => string.Equals(p.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase)
                                    && (string.IsNullOrEmpty(manufacturer) || string.Equals(p.Manufacturer, manufacturer, StringComparison.OrdinalIgnoreCase)));

    public int NextSheetNumber() => Sheets.Count == 0 ? 1 : Sheets.Max(s => s.Number) + 1;

    // ------------------------------------------------------------------ symbol cache

    /// <summary>Stores a copy of the symbol definition in the project (upgrading to newer versions).</summary>
    public void CacheSymbol(SymbolDefinition definition, string libraryId = "")
    {
        var existing = Symbols.FirstOrDefault(s => string.Equals(s.SymbolId, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Version >= definition.Version) return;
        var json = JsonSerializer.Serialize(definition, CadJson.Options);
        if (existing is null)
            Symbols.Add(new ProjectSymbol { SymbolId = definition.Id, Version = definition.Version, LibraryId = libraryId, DefinitionJson = json });
        else
        {
            existing.Version = definition.Version;
            existing.DefinitionJson = json;
            existing.LibraryId = libraryId;
        }
        _symbolCache[definition.Id] = definition;
    }

    /// <summary>Resolves a symbol from the project cache.</summary>
    public SymbolDefinition? GetCachedSymbol(string symbolId)
    {
        if (_symbolCache.TryGetValue(symbolId, out var s)) return s;
        var ps = Symbols.FirstOrDefault(x => string.Equals(x.SymbolId, symbolId, StringComparison.OrdinalIgnoreCase));
        if (ps is null) return null;
        s = JsonSerializer.Deserialize<SymbolDefinition>(ps.DefinitionJson, CadJson.Options);
        if (s is not null) _symbolCache[symbolId] = s;
        return s;
    }

    // ------------------------------------------------------------------ audit

    public void Audit(string action, string details, string? user = null)
    {
        AuditEvents.Add(new AuditEvent { Action = action, Details = details, User = user ?? Environment.UserName });
        if (AuditEvents.Count > 5000) AuditEvents.RemoveRange(0, AuditEvents.Count - 5000);
    }

    public void Touch() => Info.Modified = DateTimeOffset.Now;
}
