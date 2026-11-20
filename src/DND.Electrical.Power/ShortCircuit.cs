using System.Numerics;

namespace DND.Electrical.Power;

public sealed record FaultResult(string Bus, string Name, double Kv, double Ik3MaxKa, double IpKa, double Sk3Mva, double RxRatio, double Ik3MinKa, double Ik2Ka, double Ik1Ka, double Z1Ohm, double Z0Ohm, bool EarthFaultPath);

public sealed class ShortCircuitResult
{
    public IReadOnlyList<FaultResult> Faults { get; init; } = Array.Empty<FaultResult>();
    public List<string> Assumptions { get; } = new();
    public FaultResult At(string bus) => Faults.First(f => string.Equals(f.Bus, bus, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Short-circuit currents by the equivalent voltage source method of IEC 60909-0: c·Un/√3 at the fault location,
/// network feeders, generators (KG) and network transformers (KT) with correction factors, loads, line capacitances
/// and shunts neglected, rated transformation ratios. Driving-point impedances come from the bus impedance matrix
/// (LU factorisation of the bus admittance matrix), so meshed networks are handled.
/// Unbalanced faults use symmetrical components with Z2 = Z1.
/// </summary>
public static class ShortCircuitSolver
{
    public static ShortCircuitResult Solve(PowerNetwork network)
    {
        var m = new NetworkModel(network);
        var n = m.N;
        var luMax = new ComplexLu(m.Ybus(AdmittanceMode.ShortCircuitMax));
        var luMin = new ComplexLu(m.Ybus(AdmittanceMode.ShortCircuitMin));
        var y0 = m.Ybus(AdmittanceMode.ZeroSequence);
        // Buses without an earthed path make Y0 singular: a negligible conductance keeps it invertible (Z0 → very large).
        for (var i = 0; i < n; i++) y0[i, i] += new Complex(1e-9, 0);
        var lu0 = new ComplexLu(y0);
        if (luMax.Singular || luMin.Singular) throw new InvalidOperationException("The short-circuit admittance matrix is singular: check that every bus is connected to a source.");

        var faults = new List<FaultResult>();
        for (var k = 0; k < n; k++)
        {
            var b = m.Buses[k];
            var z1 = luMax.InverseColumn(k)[k];
            var z1min = luMin.InverseColumn(k)[k];
            var z0 = lu0.InverseColumn(k)[k];
            double cmax = m.Cmax(k), cmin = m.Cmin(k), ib = m.Ibase(k), zb = m.Zbase(k);
            var ik3 = cmax / z1.Magnitude * ib;
            var ik3min = cmin / z1min.Magnitude * ib;
            var rx = z1.Real / z1.Imaginary;
            var kappa = 1.02 + 0.98 * Math.Exp(-3 * rx);
            // Method B (meshed networks): 1.15·κ, limited to 1.8 in LV and 2.0 in MV/HV networks.
            var kappaB = Math.Min(1.15 * kappa, b.Kv <= 1 ? 1.8 : 2.0);
            var ip = kappaB * Math.Sqrt(2) * ik3;
            var ik2 = Math.Sqrt(3) * cmax / (2 * z1).Magnitude * ib;
            var earthPath = z0.Magnitude < 1e6;
            var ik1 = earthPath ? 3 * cmax / (2 * z1 + z0).Magnitude * ib : 0;
            faults.Add(new FaultResult(b.Id, b.Name, b.Kv, ik3, ip, Math.Sqrt(3) * b.Kv * ik3, rx, ik3min, ik2, ik1, z1.Magnitude * zb, earthPath ? z0.Magnitude * zb : double.PositiveInfinity, earthPath));
        }
        var r = new ShortCircuitResult { Faults = faults };
        r.Assumptions.Add($"IEC 60909-0 equivalent voltage source: cmax = {network.CmaxLowVoltage:0.00} (≤ 1 kV) / 1.10 (> 1 kV), cmin = 0.95 / 1.00; far-from-generator faults (Ik = I\"k).");
        r.Assumptions.Add("Loads, line capacitances and shunt elements neglected; transformers at rated ratio with KT; generators with KG (rated voltage = nominal voltage).");
        r.Assumptions.Add("Minimum currents: cmin, minimum feeder S\"k where entered, line resistance at 80 °C.");
        r.Assumptions.Add("Peak current ip by method B (1.15·κ, limited to 1.8 LV / 2.0 MV-HV). Asynchronous motor contributions are not included.");
        r.Assumptions.AddRange(m.Assumptions);
        return r;
    }
}
