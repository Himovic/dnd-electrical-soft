using System.Globalization;
using System.Text.RegularExpressions;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;

namespace DND.Electrical.Schematic.Services;

public sealed record WireNumberingOptions
{
    /// <summary>Replace existing (unlocked) numbers. When false only wires without a number are numbered.</summary>
    public bool RenumberExisting { get; init; }

    /// <summary>Wires on named potentials (L1, N, PE, 24V…) receive the potential name.</summary>
    public bool UsePotentialNames { get; init; } = true;
}

public sealed record WireNumberingResult(int Numbered, int Unchanged, IReadOnlyList<string> Warnings);

/// <summary>
/// Automatic wire numbering per potential or per wire using a configurable format. Numbers are
/// persisted on wires so that they remain stable; manual numbers can be locked.
/// </summary>
public static partial class WireNumberingService
{
    [GeneratedRegex(@"\{(GSEQ|SEQ|SHEET|COL)(?::(0+))?\}")]
    private static partial Regex Token();

    public static string FormatNumber(string format, int globalSeq, int sheet, int column, int sheetSeq) =>
        Token().Replace(format, m =>
        {
            var value = m.Groups[1].Value switch
            {
                "GSEQ" => globalSeq,
                "SEQ" => sheetSeq,
                "SHEET" => sheet,
                _ => column,
            };
            return m.Groups[2].Success ? value.ToString(new string('0', m.Groups[2].Value.Length), CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
        });

    public static WireNumberingResult Apply(ElectricalProject project, ConnectivityGraph graph, WireNumberingOptions? options = null)
    {
        options ??= new WireNumberingOptions();
        var s = project.Settings;
        var warnings = new List<string>();
        int numbered = 0, unchanged = 0;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!options.RenumberExisting)
            foreach (var w in graph.Wires.Where(w => w.Wire.WireNumber.Length > 0)) used.Add(w.Wire.WireNumber);
        foreach (var w in graph.Wires.Where(w => w.Wire.NumberLocked && w.Wire.WireNumber.Length > 0)) used.Add(w.Wire.WireNumber);

        int gseq = 0;
        var sheetSeq = new Dictionary<int, int>();

        string Next(WireRecord first)
        {
            var layout = SheetLayout.For(first.Sheet, s);
            var col = layout.ColumnAt(first.Wire.Points.Count > 0 ? first.Wire.Points.Min(p => p.X) : 0);
            while (true)
            {
                gseq++;
                var ss = sheetSeq[first.Sheet.Number] = sheetSeq.GetValueOrDefault(first.Sheet.Number) + 1;
                var n = FormatNumber(s.WireNumberFormat, gseq, first.Sheet.Number, col, ss);
                if (used.Add(n)) return n;
            }
        }

        static (int, double, double) Order(WireRecord w) =>
            (w.Sheet.Number, w.Wire.Points.Count > 0 ? w.Wire.Points.Min(p => p.X) : 0, w.Wire.Points.Count > 0 ? -w.Wire.Points.Max(p => p.Y) : 0);

        if (s.WireNumberingMode == WireNumberingMode.PerPotential)
        {
            foreach (var pot in graph.Potentials.Where(p => p.Wires.Count > 0).OrderBy(p => p.Wires.Min(Order)))
            {
                if (pot.HasNameConflict)
                    warnings.Add($"Potential connects different supplies ({string.Join(", ", pot.SourceNames)}) – check for a short circuit.");
                var locked = pot.Wires.Where(w => w.Wire.NumberLocked && w.Wire.WireNumber.Length > 0).Select(w => w.Wire.WireNumber).Distinct().ToList();
                string number;
                if (locked.Count > 0) number = locked[0];
                else if (options.UsePotentialNames && pot.Name is { } name) number = name;
                else if (!options.RenumberExisting && pot.WireNumbers.Count == 1) number = pot.WireNumbers[0];
                else number = Next(pot.Wires.OrderBy(Order).First());
                if (locked.Count > 1) warnings.Add($"Potential has conflicting locked wire numbers: {string.Join(", ", locked)}.");
                foreach (var w in pot.Wires)
                {
                    if (w.Wire.NumberLocked) { unchanged++; continue; }
                    if (w.Wire.WireNumber == number) { unchanged++; continue; }
                    if (!options.RenumberExisting && w.Wire.WireNumber.Length > 0 && pot.WireNumbers.Count == 1) { unchanged++; continue; }
                    w.Wire.WireNumber = number;
                    numbered++;
                }
            }
        }
        else
        {
            foreach (var w in graph.Wires.OrderBy(Order))
            {
                if (w.Wire.NumberLocked || (!options.RenumberExisting && w.Wire.WireNumber.Length > 0)) { unchanged++; continue; }
                var pot = graph.PotentialOfWire(w.Wire.Id);
                w.Wire.WireNumber = options.UsePotentialNames && pot?.Name is { } name ? name : Next(w);
                numbered++;
            }
        }
        return new WireNumberingResult(numbered, unchanged, warnings);
    }
}
