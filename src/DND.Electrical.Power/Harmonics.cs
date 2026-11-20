using System.Numerics;

namespace DND.Electrical.Power;

public sealed record ScanPoint(double Order, double ZOhm, double AngleDeg);
public sealed record Resonance(string Bus, double Order, double ZOhm);
public sealed record HarmonicVoltage(string Bus, double ThdPercent, IReadOnlyDictionary<int, double> VoltagePercent);

public sealed class HarmonicResult
{
    public IReadOnlyDictionary<string, IReadOnlyList<ScanPoint>> Scans { get; init; } = new Dictionary<string, IReadOnlyList<ScanPoint>>();
    public IReadOnlyList<Resonance> Resonances { get; init; } = Array.Empty<Resonance>();
    public IReadOnlyList<HarmonicVoltage> Distortion { get; init; } = Array.Empty<HarmonicVoltage>();
    public List<string> Assumptions { get; } = new();
}

/// <summary>
/// Frequency scan of the driving-point impedance Z(h) at every bus (resonance detection) and harmonic voltage
/// distortion from harmonic current injections (current-source method): V(h) = Z(h)·I(h), THDv = √ΣV(h)²/V1.
/// </summary>
public static class HarmonicStudy
{
    public static HarmonicResult Run(PowerNetwork network, double maxOrder = 50, double step = 0.1)
    {
        var m = new NetworkModel(network);
        var scans = new Dictionary<string, List<ScanPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in m.Buses) scans[b.Id] = new List<ScanPoint>();
        var orders = new List<double>();
        for (var h = 1.0; h <= maxOrder + 1e-9; h += step) orders.Add(Math.Round(h, 6));
        var integer = Enumerable.Range(2, (int)maxOrder - 1).Select(i => (double)i);
        var zAt = new Dictionary<double, ComplexLu>();
        foreach (var h in orders.Union(integer).Distinct().OrderBy(x => x))
        {
            var lu = new ComplexLu(m.Ybus(AdmittanceMode.Harmonic, h));
            if (lu.Singular) continue;
            zAt[h] = lu;
            if (!orders.Contains(h)) continue;
            for (var k = 0; k < m.N; k++)
            {
                var z = lu.InverseColumn(k)[k] * m.Zbase(k);
                scans[m.Buses[k].Id].Add(new ScanPoint(h, z.Magnitude, z.Phase * 180 / Math.PI));
            }
        }
        var res = new List<Resonance>();
        foreach (var (bus, pts) in scans)
            for (var i = 1; i + 1 < pts.Count; i++)
                if (pts[i].ZOhm > pts[i - 1].ZOhm && pts[i].ZOhm > pts[i + 1].ZOhm && pts[i].ZOhm > 1.5 * pts[0].ZOhm)
                    res.Add(new Resonance(bus, pts[i].Order, pts[i].ZOhm));

        var distortion = new List<HarmonicVoltage>();
        if (network.HarmonicSources.Count > 0)
            foreach (var b in m.Buses)
            {
                var k = m.Index[b.Id];
                var vh = new Dictionary<int, double>();
                foreach (var h in network.HarmonicSources.SelectMany(s => s.SpectrumPercent.Keys).Distinct().Where(h => h > 1 && zAt.ContainsKey(h)).OrderBy(h => h))
                {
                    var inj = new Complex[m.N];
                    foreach (var s in network.HarmonicSources.Where(s => s.SpectrumPercent.ContainsKey(h)))
                    {
                        var j = m.Index[s.Bus];
                        inj[j] += s.FundamentalA * s.SpectrumPercent[h] / 100 / 1000 / m.Ibase(j); // pu current
                    }
                    var v = zAt[h].Solve(inj)[k];
                    vh[h] = v.Magnitude * 100; // % of nominal (V1 ≈ 1 pu)
                }
                distortion.Add(new HarmonicVoltage(b.Id, Math.Sqrt(vh.Values.Sum(x => x * x)), vh));
            }
        var r = new HarmonicResult { Scans = scans.ToDictionary(x => x.Key, x => (IReadOnlyList<ScanPoint>)x.Value), Resonances = res, Distortion = distortion };
        r.Assumptions.Add("Series reactances scale with h, resistances are frequency-independent (no skin effect); capacitors scale with h, reactors with 1/h.");
        r.Assumptions.Add("Loads are represented as parallel resistances at their active power (damping); motors and converters are not modelled as impedances.");
        r.Assumptions.Add("Harmonic sources are ideal current sources in phase (worst case summation); V1 taken as 1 pu.");
        r.Assumptions.AddRange(m.Assumptions);
        return r;
    }

    /// <summary>Theoretical spectrum of an ideal six-pulse converter (Ih = I1/h for h = 6k ± 1). Real spectra are lower; prefer measured or manufacturer data.</summary>
    public static Dictionary<int, double> IdealSixPulse(int maxOrder = 25) =>
        Enumerable.Range(1, maxOrder / 6 + 1).SelectMany(k => new[] { 6 * k - 1, 6 * k + 1 }).Where(h => h <= maxOrder).ToDictionary(h => h, h => 100.0 / h);
}
