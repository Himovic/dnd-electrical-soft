using System.Text.Json;
using System.Text.Json.Serialization;

namespace DND.Electrical.Power;

/// <summary>
/// D&amp;D PowerStudio network model (balanced three-phase, positive sequence plus zero-sequence data for earth faults).
/// Element parameters are entered in engineering units and converted to per unit on <see cref="BaseMva"/> and each
/// bus nominal voltage. <see cref="PuBranch"/> accepts per-unit data directly (benchmark cases).
/// </summary>
public sealed class PowerNetwork
{
    public string Name { get; set; } = "Network";
    public double BaseMva { get; set; } = 100;
    public double FrequencyHz { get; set; } = 50;
    /// <summary>Voltage factor cmax for networks ≤ 1 kV (IEC 60909: 1.05 for +6 % tolerance, 1.10 for +10 %).</summary>
    public double CmaxLowVoltage { get; set; } = 1.05;
    public double GradingMarginS { get; set; } = 0.3;
    public List<Bus> Buses { get; set; } = new();
    public List<ExternalGrid> Grids { get; set; } = new();
    public List<Generator> Generators { get; set; } = new();
    public List<Load> Loads { get; set; } = new();
    public List<Shunt> Shunts { get; set; } = new();
    public List<Line> Lines { get; set; } = new();
    public List<Transformer> Transformers { get; set; } = new();
    public List<PuBranch> PuBranches { get; set; } = new();
    public List<ProtectionDevice> Protection { get; set; } = new();
    public List<HarmonicSource> HarmonicSources { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static PowerNetwork FromJson(string json) => JsonSerializer.Deserialize<PowerNetwork>(json, Json) ?? new PowerNetwork();

    public Bus? Bus(string id) => Buses.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Structural checks before a study: unknown buses, missing data, islands without a source.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Buses)
        {
            if (string.IsNullOrWhiteSpace(b.Id)) errors.Add("A bus has no identifier.");
            else if (!ids.Add(b.Id)) errors.Add($"Duplicate bus '{b.Id}'.");
            if (b.Kv <= 0) errors.Add($"Bus {b.Id}: nominal voltage must be positive.");
        }
        void Ref(string kind, string id, string bus) { if (Bus(bus) is null) errors.Add($"{kind} {id}: unknown bus '{bus}'."); }
        foreach (var g in Grids) { Ref("Grid", g.Id, g.Bus); if (g.SkMaxMva <= 0) errors.Add($"Grid {g.Id}: S\"k must be positive."); }
        foreach (var g in Generators) { Ref("Generator", g.Id, g.Bus); if (g.SnMva <= 0) errors.Add($"Generator {g.Id}: rating must be positive."); }
        foreach (var l in Loads) Ref("Load", l.Id, l.Bus);
        foreach (var s in Shunts) Ref("Shunt", s.Id, s.Bus);
        foreach (var l in Lines)
        {
            Ref("Line", l.Id, l.From); Ref("Line", l.Id, l.To);
            if (l.LengthKm <= 0 || (l.ROhmPerKm <= 0 && l.XOhmPerKm <= 0)) errors.Add($"Line {l.Id}: length and impedance must be positive.");
            if (Bus(l.From) is { } a && Bus(l.To) is { } b && Math.Abs(a.Kv - b.Kv) > 1e-6) errors.Add($"Line {l.Id} connects buses of different nominal voltage ({a.Kv} kV / {b.Kv} kV).");
        }
        foreach (var t in Transformers)
        {
            Ref("Transformer", t.Id, t.HvBus); Ref("Transformer", t.Id, t.LvBus);
            if (t.SnMva <= 0 || t.UkPercent <= 0) errors.Add($"Transformer {t.Id}: rating and uk must be positive.");
            if (t.PcuKw / 1000 / t.SnMva * 100 >= t.UkPercent) errors.Add($"Transformer {t.Id}: load losses are inconsistent with uk (uR ≥ uk).");
        }
        foreach (var p in PuBranches) { Ref("Branch", p.Id, p.From); Ref("Branch", p.Id, p.To); }
        if (Grids.Count == 0 && !Generators.Any(g => g.InService)) errors.Add("The network has no source (external grid or generator).");
        if (errors.Count == 0)
        {
            // Islands: every bus must be connected to a source bus.
            var adj = Buses.ToDictionary(b => b.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var l in Lines.Where(l => l.InService)) { adj[l.From].Add(l.To); adj[l.To].Add(l.From); }
            foreach (var t in Transformers.Where(t => t.InService)) { adj[t.HvBus].Add(t.LvBus); adj[t.LvBus].Add(t.HvBus); }
            foreach (var p in PuBranches.Where(p => p.InService)) { adj[p.From].Add(p.To); adj[p.To].Add(p.From); }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(Grids.Select(g => g.Bus).Concat(Generators.Where(g => g.InService).Select(g => g.Bus)));
            while (stack.Count > 0) { var b = stack.Pop(); if (!seen.Add(b)) continue; foreach (var n in adj[b]) stack.Push(n); }
            foreach (var b in Buses.Where(b => !seen.Contains(b.Id))) errors.Add($"Bus {b.Id} is not connected to any source (island).");
        }
        return errors;
    }
}

public sealed class Bus
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Kv { get; set; } = 0.4;
    public double VminPu { get; set; } = 0.95;
    public double VmaxPu { get; set; } = 1.05;
    /// <summary>Engineering object (e.g. switchboard device) represented by the bus.</summary>
    public Guid? ObjectId { get; set; }
}

/// <summary>Utility network feeder (slack bus in load flow; Thevenin source in short-circuit studies).</summary>
public sealed class ExternalGrid
{
    public string Id { get; set; } = "Grid";
    public string Bus { get; set; } = string.Empty;
    public double SkMaxMva { get; set; } = 500;
    public double SkMinMva { get; set; } = 0;
    public double RxRatio { get; set; } = 0.1;
    public double VoltagePu { get; set; } = 1.0;
    /// <summary>Z0/Z1 of the feeder for earth faults; 0 means the feeder provides no zero-sequence path.</summary>
    public double Z0Z1Ratio { get; set; } = 1.0;
    /// <summary>Voltage factor used for the feeder impedance; default cmax of the feeder voltage level.</summary>
    public double? CFactor { get; set; }
}

public sealed class Generator
{
    public string Id { get; set; } = "G1";
    public string Bus { get; set; } = string.Empty;
    public bool InService { get; set; } = true;
    public double PMw { get; set; }
    public double VsetPu { get; set; } = 1.0;
    public double QminMvar { get; set; } = -9999;
    public double QmaxMvar { get; set; } = 9999;
    public double SnMva { get; set; } = 1;
    /// <summary>Subtransient reactance x"d (pu on the generator rating; manufacturer data).</summary>
    public double XdSubtransientPu { get; set; } = 0.15;
    /// <summary>RG/X"d used in short-circuit studies (IEC 60909 gives 0.05–0.15 depending on rating).</summary>
    public double RxRatio { get; set; } = 0.15;
    public double RatedPowerFactor { get; set; } = 0.8;
    public bool NeutralEarthed { get; set; }
    public double X0Pu { get; set; } = 0.05;
}

public sealed class Load
{
    public string Id { get; set; } = string.Empty;
    public string Bus { get; set; } = string.Empty;
    public double PMw { get; set; }
    public double QMvar { get; set; }
    public Guid? ObjectId { get; set; }
}

/// <summary>Shunt compensation at 1 pu voltage: positive = capacitor bank, negative = reactor.</summary>
public sealed class Shunt
{
    public string Id { get; set; } = string.Empty;
    public string Bus { get; set; } = string.Empty;
    public double QMvar { get; set; }
    public bool InService { get; set; } = true;
}

public sealed class Line
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public bool InService { get; set; } = true;
    public double LengthKm { get; set; } = 0.1;
    /// <summary>Positive-sequence resistance at 20 °C (Ω/km per circuit).</summary>
    public double ROhmPerKm { get; set; }
    public double XOhmPerKm { get; set; }
    public double BMicroSiemensPerKm { get; set; }
    public double? R0OhmPerKm { get; set; }
    public double? X0OhmPerKm { get; set; }
    public int Parallel { get; set; } = 1;
    public double RatingA { get; set; }
    /// <summary>Cable represented by the line (engineering object identity).</summary>
    public Guid? CableId { get; set; }
}

public sealed class Transformer
{
    public string Id { get; set; } = string.Empty;
    public string HvBus { get; set; } = string.Empty;
    public string LvBus { get; set; } = string.Empty;
    public bool InService { get; set; } = true;
    public double SnMva { get; set; } = 1;
    public double UkPercent { get; set; } = 6;
    public double PcuKw { get; set; } = 10;
    /// <summary>Off-nominal ratio on the HV side (1 = rated ratio). Neglected in IEC 60909 short-circuit studies.</summary>
    public double TapRatio { get; set; } = 1.0;
    /// <summary>Vector group, e.g. Dyn11, YNyn0, YNd5, Yd11.</summary>
    public string VectorGroup { get; set; } = "Dyn11";
}

/// <summary>Branch with per-unit data on the system base (π model, optional off-nominal tap and phase shift).</summary>
public sealed class PuBranch
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public bool InService { get; set; } = true;
    public double R { get; set; }
    public double X { get; set; }
    public double B { get; set; }
    /// <summary>Off-nominal turns ratio at the from side; 0 = line.</summary>
    public double Tap { get; set; }
    public double ShiftDeg { get; set; }
    public double RatingMva { get; set; }
}

public sealed class HarmonicSource
{
    public string Id { get; set; } = string.Empty;
    public string Bus { get; set; } = string.Empty;
    /// <summary>Fundamental current of the non-linear load (A).</summary>
    public double FundamentalA { get; set; }
    /// <summary>Harmonic order → current in % of the fundamental (from measurement or manufacturer data).</summary>
    public Dictionary<int, double> SpectrumPercent { get; set; } = new();
}
