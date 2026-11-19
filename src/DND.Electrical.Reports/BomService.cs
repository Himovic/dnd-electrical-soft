using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Reports;

public sealed record BomLine(
    string Manufacturer,
    string PartNumber,
    string Description,
    string Category,
    double Quantity,
    string Unit,
    decimal UnitPrice,
    string Currency,
    string Supplier,
    ProcurementStatus Status,
    IReadOnlyList<string> Tags,
    bool IsDemoData,
    IReadOnlyList<string> Alternatives)
{
    public decimal Total => UnitPrice * (decimal)Quantity;
}

/// <summary>Builds the bill of materials from devices, cabinet accessories, cables and terminals.</summary>
public static class BomService
{
    public static IReadOnlyList<BomLine> Build(ElectricalProject project)
    {
        var acc = new Dictionary<(string m, string p), (double qty, string unit, List<string> tags, string fallbackDesc, string category)>();

        void Add(string manufacturer, string part, double qty, string unit, string? tag, string desc, string category)
        {
            if (string.IsNullOrWhiteSpace(part)) part = "(unspecified)";
            var key = (manufacturer.Trim(), part.Trim());
            if (!acc.TryGetValue(key, out var v)) v = (0, unit, new List<string>(), desc, category);
            v.qty += qty;
            if (tag is not null && !v.tags.Contains(tag)) v.tags.Add(tag);
            acc[key] = v;
        }

        var terminalStripTags = project.TerminalStrips.Select(t => t.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in project.Devices.Where(d => !terminalStripTags.Contains(d.Tag)))
            Add(d.Manufacturer, d.PartNumber, 1, "pcs", d.Tag, d.Function, d.Category);
        foreach (var s in project.TerminalStrips)
            foreach (var t in s.Terminals)
                Add(string.Empty, string.IsNullOrEmpty(s.TerminalPartNumber) ? (t.Type == TerminalType.ProtectiveEarth ? "TERMINAL-PE" : "TERMINAL") : s.TerminalPartNumber,
                    1, "pcs", s.Tag, t.Type == TerminalType.ProtectiveEarth ? "PE terminal" : "Feed-through terminal", "Terminals");
        foreach (var c in project.Cabinets)
        {
            if (!string.IsNullOrEmpty(c.EnclosurePartNumber)) Add(string.Empty, c.EnclosurePartNumber, 1, "pcs", c.Tag, $"Enclosure {c.Width:0}×{c.Height:0}×{c.Depth:0} {c.IpRating}", "Enclosures");
            foreach (var i in c.Items.Where(i => i.DeviceId is null && i.Kind is not CabinetItemKind.Device))
            {
                var isLinear = i.Kind is CabinetItemKind.DinRail or CabinetItemKind.CableDuct;
                Add(string.Empty, i.PartNumber, isLinear ? Math.Round(Math.Max(i.Width, i.Height) / 1000, 3) : 1, isLinear ? "m" : "pcs", c.Tag, i.Name, i.Kind.ToString());
            }
        }
        foreach (var c in project.Cables)
            Add(string.Empty, string.IsNullOrEmpty(c.PartNumber) ? c.Designation : c.PartNumber, c.LengthM, "m", c.Tag, $"Cable {c.Designation}", "Cables");

        var lines = new List<BomLine>();
        foreach (var ((m, p), v) in acc)
        {
            var product = project.FindProduct(m, p);
            var proc = project.Procurement.FirstOrDefault(x => string.Equals(x.PartNumber, p, StringComparison.OrdinalIgnoreCase));
            lines.Add(new BomLine(
                product?.Manufacturer ?? m,
                p,
                product?.Description ?? v.fallbackDesc,
                product?.Category ?? v.category,
                Math.Round(v.qty, 3),
                v.unit,
                product?.UnitPrice ?? 0,
                product?.Currency ?? project.Settings.Currency,
                proc?.Supplier is { Length: > 0 } sup ? sup : product?.Supplier ?? string.Empty,
                proc?.Status ?? ProcurementStatus.NotOrdered,
                v.tags.OrderBy(t => t, NaturalStringComparer.Instance).ToList(),
                product?.IsDemoData ?? false,
                product?.Alternatives ?? new List<string>()));
        }
        return lines.OrderBy(l => l.Category).ThenBy(l => l.Manufacturer).ThenBy(l => l.PartNumber, NaturalStringComparer.Instance).ToList();
    }

    public static decimal TotalCost(IEnumerable<BomLine> lines) => lines.Sum(l => l.Total);
}
