using System.Globalization;
using System.Text;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Power;

namespace DND.Electrical.Application.Services;

/// <summary>
/// D&amp;D PowerStudio application service: network storage in the project, studies (load flow, short circuit,
/// protection coordination, harmonics) and study reports. Every study requires the POWER licence module.
/// </summary>
public sealed class PowerStudioService(IEntitlements entitlements)
{
    public static PowerNetwork Load(PowerNetworkDocument doc) { var n = PowerNetwork.FromJson(doc.NetworkJson); n.Name = doc.Name; return n; }

    public PowerNetworkDocument Save(ElectricalProject p, PowerNetwork network, Guid? id = null)
    {
        entitlements.Demand(EngineeringModule.POWER);
        var doc = id is { } i ? p.PowerNetworks.FirstOrDefault(d => d.Id == i) : null;
        if (doc is null) { doc = new PowerNetworkDocument(); p.PowerNetworks.Add(doc); }
        doc.Name = network.Name; doc.NetworkJson = network.ToJson(); doc.Modified = DateTimeOffset.Now;
        return doc;
    }

    public LoadFlowResult LoadFlow(PowerNetwork n) { entitlements.Demand(EngineeringModule.POWER); return LoadFlowSolver.Solve(n); }
    public ShortCircuitResult ShortCircuit(PowerNetwork n) { entitlements.Demand(EngineeringModule.POWER); return ShortCircuitSolver.Solve(n); }
    public IReadOnlyList<CoordinationFinding> Coordination(PowerNetwork n, ShortCircuitResult sc) { entitlements.Demand(EngineeringModule.POWER); return ProtectionStudy.Coordinate(n, sc); }
    public HarmonicResult Harmonics(PowerNetwork n) { entitlements.Demand(EngineeringModule.POWER); return HarmonicStudy.Run(n); }

    /// <summary>
    /// Creates a starting network from the project's load list: utility feeder, one transformer, the main LV board and
    /// one bus per distribution board with aggregated loads. Feeder, transformer and cable data are placeholders that
    /// must be replaced with the real values (listed in the returned notes).
    /// </summary>
    public (PowerNetwork network, IReadOnlyList<string> notes) BuildFromProject(ElectricalProject p, double transformerKva, double feederSkMva = 250, double mvKv = 20)
    {
        entitlements.Demand(EngineeringModule.POWER);
        var lvKv = Math.Max(0.1, p.Settings.NominalVoltage / 1000.0);
        var n = new PowerNetwork { Name = $"{p.Info.Number} network", CmaxLowVoltage = 1.05 };
        var notes = new List<string>
        {
            $"Utility feeder S\"k = {feederSkMva:0} MVA at {mvKv:0.#} kV (placeholder – obtain from the utility).",
            $"Transformer {transformerKva:0} kVA, uk 6 %, Dyn11 (placeholder – use nameplate data).",
        };
        n.Buses.Add(new Bus { Id = "MV", Name = "MV supply", Kv = mvKv });
        n.Buses.Add(new Bus { Id = "MDB", Name = "Main LV board", Kv = lvKv });
        n.Grids.Add(new ExternalGrid { Id = "Utility", Bus = "MV", SkMaxMva = feederSkMva, RxRatio = 0.1 });
        var pcu = transformerKva * 0.011; // ≈1.1 % load losses placeholder
        n.Transformers.Add(new Transformer { Id = "TR1", HvBus = "MV", LvBus = "MDB", SnMva = transformerKva / 1000, UkPercent = 6, PcuKw = pcu, VectorGroup = "Dyn11" });
        var boards = p.Loads.Select(l => string.IsNullOrWhiteSpace(l.Board) ? "MDB" : l.Board.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var b in boards.Where(b => !string.Equals(b, "MDB", StringComparison.OrdinalIgnoreCase)))
        {
            var id = b.Replace(' ', '_');
            n.Buses.Add(new Bus { Id = id, Name = b, Kv = lvKv });
            n.Lines.Add(new Line { Id = $"C-{id}", From = "MDB", To = id, LengthKm = 0.05, ROhmPerKm = 0.193, XOhmPerKm = 0.08, R0OhmPerKm = 0.772, X0OhmPerKm = 0.32, RatingA = 200 });
            notes.Add($"Feeder cable MDB → {b}: 50 m, 95 mm² Cu placeholder – enter the real cable data.");
        }
        foreach (var g in p.Loads.GroupBy(l => string.IsNullOrWhiteSpace(l.Board) ? "MDB" : l.Board.Trim().Replace(' ', '_'), StringComparer.OrdinalIgnoreCase))
        {
            double pk = g.Sum(l => l.PowerKw * l.DemandFactor), qk = g.Sum(l => l.PowerKw * l.DemandFactor * Math.Tan(Math.Acos(Math.Clamp(l.PowerFactor, 0.1, 1))));
            n.Loads.Add(new Load { Id = $"Loads {g.Key}", Bus = n.Buses.First(b => string.Equals(b.Id, g.Key, StringComparison.OrdinalIgnoreCase)).Id, PMw = pk / 1000, QMvar = qk / 1000 });
        }
        if (p.Motors.Count > 0)
        {
            double pk = p.Motors.Sum(m => m.PowerKw / Math.Max(0.1, m.Efficiency)), qk = p.Motors.Sum(m => m.PowerKw / Math.Max(0.1, m.Efficiency) * Math.Tan(Math.Acos(Math.Clamp(m.PowerFactor, 0.1, 1))));
            n.Loads.Add(new Load { Id = "Motors", Bus = "MDB", PMw = pk / 1000, QMvar = qk / 1000 });
            notes.Add($"{p.Motors.Count} motor(s) aggregated at the main board at rated input power.");
        }
        return (n, notes);
    }

    public static string LoadFlowReport(PowerNetwork n, LoadFlowResult r)
    {
        var sb = Header("Load flow (Newton–Raphson)", n);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Converged: {(r.Converged ? "yes" : "NO")} in {r.Iterations} iterations, max mismatch {r.MaxMismatchMva:E2} MVA");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Losses: {r.LossesMw * 1000:0.##} kW, {r.LossesMvar * 1000:0.##} kvar").AppendLine();
        sb.AppendLine("Bus        kV       V [pu]   V [kV]    Angle [°]   Pgen [MW]  Qgen [Mvar]  Pload [MW]  Qload [Mvar]");
        foreach (var b in r.Buses)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{b.Bus,-10} {b.Kv,7:0.###}  {b.VPu,7:0.0000}  {b.VKv,8:0.####}  {b.AngleDeg,9:0.000}  {b.PGenMw,10:0.####}  {b.QGenMvar,11:0.####}  {b.PLoadMw,10:0.####}  {b.QLoadMvar,11:0.####}{(b.VoltageViolation ? "  ⚠ voltage" : "")}");
        sb.AppendLine().AppendLine("Branch          From → To        P [MW]     Q [Mvar]   I [A]      Loading [%]  Loss [kW]");
        foreach (var b in r.Branches)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{b.Id,-15} {b.From + " → " + b.To,-15} {b.PFromMw,9:0.####}  {b.QFromMvar,9:0.####}  {b.CurrentA,9:0.#}  {b.LoadingPercent,10:0.#}  {b.LossMw * 1000,9:0.###}");
        return Footer(sb, r.Messages, r.Assumptions);
    }

    public static string ShortCircuitReport(PowerNetwork n, ShortCircuitResult r)
    {
        var sb = Header("Short-circuit study (IEC 60909-0 method)", n);
        sb.AppendLine("Bus        kV      I\"k3 max [kA]  ip [kA]   S\"k [MVA]  R/X     I\"k3 min [kA]  I\"k2 [kA]  I\"k1 [kA]");
        foreach (var f in r.Faults)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{f.Bus,-10} {f.Kv,6:0.###}  {f.Ik3MaxKa,12:0.###}  {f.IpKa,8:0.###}  {f.Sk3Mva,9:0.##}  {f.RxRatio,6:0.###}  {f.Ik3MinKa,12:0.###}  {f.Ik2Ka,9:0.###}  {(f.EarthFaultPath ? f.Ik1Ka.ToString("0.###", CultureInfo.InvariantCulture) : "no earth path"),9}");
        return Footer(sb, Array.Empty<string>(), r.Assumptions);
    }

    private static StringBuilder Header(string title, PowerNetwork n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"D&D PowerStudio – {title}").AppendLine($"Network: {n.Name}  ·  base {n.BaseMva:0.##} MVA  ·  {n.FrequencyHz:0} Hz").AppendLine();
        return sb;
    }

    private static string Footer(StringBuilder sb, IEnumerable<string> messages, IEnumerable<string> assumptions)
    {
        var msgs = messages.ToList();
        if (msgs.Count > 0) { sb.AppendLine().AppendLine("Findings"); foreach (var m in msgs) sb.AppendLine("  - " + m); }
        sb.AppendLine().AppendLine("Assumptions");
        foreach (var a in assumptions) sb.AppendLine("  - " + a);
        sb.AppendLine().AppendLine(DND.Electrical.Calculations.CalculationResult.Disclaimer);
        sb.AppendLine($"{DND.Electrical.Shared.ProductInfo.ProductName} — {DND.Electrical.Shared.ProductInfo.PoweredBy}");
        return sb.ToString();
    }
}
