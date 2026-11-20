using System.Numerics;

namespace DND.Electrical.Power;

/// <summary>Dense complex LU factorisation with partial pivoting.</summary>
public sealed class ComplexLu
{
    private readonly Complex[,] _a;
    private readonly int[] _p;
    public int N { get; }
    public bool Singular { get; }

    public ComplexLu(Complex[,] matrix)
    {
        N = matrix.GetLength(0);
        _a = (Complex[,])matrix.Clone();
        _p = Enumerable.Range(0, N).ToArray();
        for (var k = 0; k < N; k++)
        {
            var piv = k; var max = _a[k, k].Magnitude;
            for (var i = k + 1; i < N; i++) if (_a[i, k].Magnitude > max) { max = _a[i, k].Magnitude; piv = i; }
            if (max < 1e-14) { Singular = true; return; }
            if (piv != k)
            {
                (_p[k], _p[piv]) = (_p[piv], _p[k]);
                for (var j = 0; j < N; j++) (_a[k, j], _a[piv, j]) = (_a[piv, j], _a[k, j]);
            }
            for (var i = k + 1; i < N; i++)
            {
                var f = _a[i, k] / _a[k, k];
                _a[i, k] = f;
                if (f == Complex.Zero) continue;
                for (var j = k + 1; j < N; j++) _a[i, j] -= f * _a[k, j];
            }
        }
    }

    public Complex[] Solve(Complex[] b)
    {
        if (Singular) throw new InvalidOperationException("Singular admittance matrix.");
        var x = new Complex[N];
        for (var i = 0; i < N; i++) x[i] = b[_p[i]];
        for (var i = 0; i < N; i++) for (var j = 0; j < i; j++) x[i] -= _a[i, j] * x[j];
        for (var i = N - 1; i >= 0; i--) { for (var j = i + 1; j < N; j++) x[i] -= _a[i, j] * x[j]; x[i] /= _a[i, i]; }
        return x;
    }

    /// <summary>Column k of the inverse (driving-point and transfer impedances for bus k).</summary>
    public Complex[] InverseColumn(int k) { var e = new Complex[N]; e[k] = 1; return Solve(e); }
}

public static class RealLu
{
    /// <summary>Solves A·x = b in place (Gaussian elimination with partial pivoting).</summary>
    public static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length; var m = (double[,])a.Clone(); var x = (double[])b.Clone();
        for (var k = 0; k < n; k++)
        {
            var piv = k; var max = Math.Abs(m[k, k]);
            for (var i = k + 1; i < n; i++) if (Math.Abs(m[i, k]) > max) { max = Math.Abs(m[i, k]); piv = i; }
            if (max < 1e-14) throw new InvalidOperationException("Singular Jacobian (the network may be islanded or ill-conditioned).");
            if (piv != k) { for (var j = 0; j < n; j++) (m[k, j], m[piv, j]) = (m[piv, j], m[k, j]); (x[k], x[piv]) = (x[piv], x[k]); }
            for (var i = k + 1; i < n; i++)
            {
                var f = m[i, k] / m[k, k];
                if (f == 0) continue;
                for (var j = k; j < n; j++) m[i, j] -= f * m[k, j];
                x[i] -= f * x[k];
            }
        }
        for (var i = n - 1; i >= 0; i--) { for (var j = i + 1; j < n; j++) x[i] -= m[i, j] * x[j]; x[i] /= m[i, i]; }
        return x;
    }
}

public enum AdmittanceMode { LoadFlow, ShortCircuitMax, ShortCircuitMin, ZeroSequence, Harmonic }

/// <summary>Branch admittance stamp (from/to bus indices and the 2×2 π-model terms).</summary>
public sealed record BranchStamp(string Id, string Kind, int From, int To, Complex Yff, Complex Yft, Complex Ytf, Complex Ytt, double RatingA, double RatingMva);

/// <summary>Builds per-unit admittance matrices of a <see cref="PowerNetwork"/> for the different studies.</summary>
public sealed class NetworkModel
{
    public PowerNetwork Network { get; }
    public IReadOnlyList<Bus> Buses { get; }
    public IReadOnlyDictionary<string, int> Index { get; }
    public List<string> Assumptions { get; } = new();

    public NetworkModel(PowerNetwork network)
    {
        var errors = network.Validate();
        if (errors.Count > 0) throw new InvalidOperationException("Network data errors:\n" + string.Join("\n", errors));
        Network = network;
        Buses = network.Buses;
        Index = network.Buses.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.OrdinalIgnoreCase);
    }

    public int N => Buses.Count;
    public double Zbase(int bus) => Buses[bus].Kv * Buses[bus].Kv / Network.BaseMva;
    public double Ibase(int bus) => Network.BaseMva / (Math.Sqrt(3) * Buses[bus].Kv); // kA
    public double Cmax(int bus) => Buses[bus].Kv <= 1.0 ? Network.CmaxLowVoltage : 1.10;
    public double Cmin(int bus) => Buses[bus].Kv <= 1.0 ? 0.95 : 1.00;

    /// <summary>Series impedance of a transformer in pu on the system base (positive sequence).</summary>
    public Complex TransformerZ(Transformer t)
    {
        var z = t.UkPercent / 100 * Network.BaseMva / t.SnMva;
        var r = t.PcuKw / 1000 / t.SnMva * Network.BaseMva / t.SnMva;
        return new Complex(r, Math.Sqrt(Math.Max(0, z * z - r * r)));
    }

    /// <summary>IEC 60909 impedance correction factor KT = 0.95·cmax/(1 + 0.6·xT).</summary>
    public double TransformerKt(Transformer t)
    {
        var zOwn = TransformerZ(t) * (t.SnMva / Network.BaseMva);
        return 0.95 * Cmax(Index[t.LvBus]) / (1 + 0.6 * zOwn.Imaginary);
    }

    /// <summary>IEC 60909 generator correction factor KG = cmax/(1 + x"d·sin φrG) (rated voltage = nominal voltage assumed).</summary>
    public double GeneratorKg(Generator g) => Cmax(Index[g.Bus]) / (1 + g.XdSubtransientPu * Math.Sin(Math.Acos(g.RatedPowerFactor)));

    public Complex GridZ(ExternalGrid g, bool minimum)
    {
        var bus = Index[g.Bus];
        var c = g.CFactor ?? (minimum ? Cmin(bus) : Cmax(bus));
        var sk = minimum && g.SkMinMva > 0 ? g.SkMinMva : g.SkMaxMva;
        var z = c * Network.BaseMva / sk;
        var x = z / Math.Sqrt(1 + g.RxRatio * g.RxRatio);
        return new Complex(g.RxRatio * x, x);
    }

    public IEnumerable<BranchStamp> Branches(AdmittanceMode mode, double h = 1)
    {
        var n = Network;
        foreach (var l in n.Lines.Where(l => l.InService))
        {
            int f = Index[l.From], t = Index[l.To];
            var zb = Zbase(f);
            var rFactor = mode == AdmittanceMode.ShortCircuitMin ? 1 + 0.004 * (80 - 20) : 1.0;
            double r, x;
            if (mode == AdmittanceMode.ZeroSequence)
            {
                r = (l.R0OhmPerKm ?? 3 * l.ROhmPerKm); x = l.X0OhmPerKm ?? 3 * l.XOhmPerKm;
                if (l.R0OhmPerKm is null || l.X0OhmPerKm is null) Note($"Line {l.Id}: zero-sequence impedance not entered – Z0 = 3·Z1 assumed.");
            }
            else { r = l.ROhmPerKm * rFactor; x = l.XOhmPerKm; }
            var z = new Complex(r, x * (mode == AdmittanceMode.Harmonic ? h : 1)) * l.LengthKm / l.Parallel / zb;
            var y = 1 / z;
            var bsh = mode is AdmittanceMode.LoadFlow or AdmittanceMode.Harmonic ? l.BMicroSiemensPerKm * 1e-6 * l.LengthKm * l.Parallel * zb * (mode == AdmittanceMode.Harmonic ? h : 1) : 0;
            var half = new Complex(0, bsh / 2);
            yield return new BranchStamp(l.Id, "Line", f, t, y + half, -y, -y, y + half, l.RatingA * l.Parallel, 0);
        }
        foreach (var tr in n.Transformers.Where(t => t.InService))
        {
            int f = Index[tr.HvBus], t = Index[tr.LvBus];
            var z = TransformerZ(tr);
            if (mode == AdmittanceMode.Harmonic) z = new Complex(z.Real, z.Imaginary * h);
            if (mode is AdmittanceMode.ShortCircuitMax or AdmittanceMode.ShortCircuitMin or AdmittanceMode.ZeroSequence) z *= TransformerKt(tr);
            var y = 1 / z;
            var ratingA = tr.SnMva / (Math.Sqrt(3) * Buses[t].Kv) * 1000;
            if (mode == AdmittanceMode.ZeroSequence)
            {
                var g = Group(tr.VectorGroup);
                if (g.hv == 'N' && g.lv == 'N') yield return new BranchStamp(tr.Id, "Transformer", f, t, y, -y, -y, y, ratingA, tr.SnMva);
                // Earthed star facing a delta (or Dyn): zero-sequence current circulates in the delta → shunt at the earthed side.
                else if (g.lv == 'N' && g.hv == 'D') yield return new BranchStamp(tr.Id, "Transformer", t, t, y, 0, 0, 0, ratingA, tr.SnMva);
                else if (g.hv == 'N' && g.lv == 'D') yield return new BranchStamp(tr.Id, "Transformer", f, f, y, 0, 0, 0, ratingA, tr.SnMva);
                else if (g.unsupported) Note($"Transformer {tr.Id}: vector group {tr.VectorGroup} is not modelled in the zero-sequence network (treated as open).");
                continue;
            }
            var tap = mode == AdmittanceMode.LoadFlow ? tr.TapRatio : 1.0;
            yield return new BranchStamp(tr.Id, "Transformer", f, t, y / (tap * tap), -y / tap, -y / tap, y, ratingA, tr.SnMva);
        }
        foreach (var b in n.PuBranches.Where(b => b.InService))
        {
            int f = Index[b.From], t = Index[b.To];
            var z = new Complex(b.R, b.X * (mode == AdmittanceMode.Harmonic ? h : 1));
            if (mode == AdmittanceMode.ZeroSequence) { Note($"Branch {b.Id}: per-unit branch without zero-sequence data – Z0 = 3·Z1 assumed."); z *= 3; }
            var y = 1 / z;
            var bsh = mode is AdmittanceMode.LoadFlow or AdmittanceMode.Harmonic ? b.B * (mode == AdmittanceMode.Harmonic ? h : 1) : 0;
            var half = new Complex(0, bsh / 2);
            var tau = mode == AdmittanceMode.LoadFlow && b.Tap > 0 ? b.Tap : 1.0;
            var shift = mode == AdmittanceMode.LoadFlow ? b.ShiftDeg * Math.PI / 180 : 0;
            var tc = Complex.FromPolarCoordinates(tau, shift);
            var ratingA = b.RatingMva > 0 ? b.RatingMva / (Math.Sqrt(3) * Buses[f].Kv) * 1000 : 0;
            yield return new BranchStamp(b.Id, b.Tap > 0 ? "Transformer" : "Line", f, t, (y + half) / (tau * tau), -y / Complex.Conjugate(tc), -y / tc, y + half, ratingA, b.RatingMva);
        }
    }

    private static (char hv, char lv, bool unsupported) Group(string vectorGroup)
    {
        var v = vectorGroup.Trim();
        char hv = v.StartsWith("YN", StringComparison.Ordinal) ? 'N' : v.StartsWith('D') ? 'D' : v.StartsWith('Y') ? 'Y' : '?';
        var rest = v[(hv == 'N' ? 2 : 1)..];
        char lv = rest.StartsWith("yn", StringComparison.Ordinal) ? 'N' : rest.StartsWith('d') ? 'D' : rest.StartsWith('y') ? 'Y' : '?';
        // dyn / dzn: an earthed LV star behind an HV delta
        if (hv == 'D' && rest.StartsWith("yn", StringComparison.Ordinal)) lv = 'N';
        var unsupported = hv == '?' || lv == '?' || rest.StartsWith('z');
        return (hv, lv, unsupported);
    }

    private void Note(string s) { if (!Assumptions.Contains(s)) Assumptions.Add(s); }

    /// <summary>Assembles the bus admittance matrix including the shunt elements relevant for <paramref name="mode"/>.</summary>
    public Complex[,] Ybus(AdmittanceMode mode, double h = 1)
    {
        var y = new Complex[N, N];
        foreach (var b in Branches(mode, h))
        {
            y[b.From, b.From] += b.Yff;
            if (b.From == b.To) continue;
            y[b.From, b.To] += b.Yft; y[b.To, b.From] += b.Ytf; y[b.To, b.To] += b.Ytt;
        }
        var n = Network;
        switch (mode)
        {
            case AdmittanceMode.LoadFlow:
                foreach (var s in n.Shunts.Where(s => s.InService)) y[Index[s.Bus], Index[s.Bus]] += new Complex(0, s.QMvar / n.BaseMva);
                break;
            case AdmittanceMode.ShortCircuitMax or AdmittanceMode.ShortCircuitMin:
                foreach (var g in n.Grids) y[Index[g.Bus], Index[g.Bus]] += 1 / GridZ(g, mode == AdmittanceMode.ShortCircuitMin);
                foreach (var g in n.Generators.Where(g => g.InService))
                    y[Index[g.Bus], Index[g.Bus]] += 1 / (new Complex(g.RxRatio * g.XdSubtransientPu, g.XdSubtransientPu) * (n.BaseMva / g.SnMva) * GeneratorKg(g));
                break;
            case AdmittanceMode.ZeroSequence:
                foreach (var g in n.Grids.Where(g => g.Z0Z1Ratio > 0)) y[Index[g.Bus], Index[g.Bus]] += 1 / (GridZ(g, false) * g.Z0Z1Ratio);
                foreach (var g in n.Generators.Where(g => g.InService && g.NeutralEarthed))
                    y[Index[g.Bus], Index[g.Bus]] += 1 / (new Complex(0, g.X0Pu) * (n.BaseMva / g.SnMva));
                break;
            case AdmittanceMode.Harmonic:
                foreach (var g in n.Grids) { var z = GridZ(g, false); y[Index[g.Bus], Index[g.Bus]] += 1 / new Complex(z.Real, z.Imaginary * h); }
                foreach (var g in n.Generators.Where(g => g.InService))
                {
                    var z = new Complex(g.RxRatio * g.XdSubtransientPu, g.XdSubtransientPu * h) * (n.BaseMva / g.SnMva);
                    y[Index[g.Bus], Index[g.Bus]] += 1 / z;
                }
                foreach (var s in n.Shunts.Where(s => s.InService))
                    y[Index[s.Bus], Index[s.Bus]] += s.QMvar >= 0 ? new Complex(0, s.QMvar / n.BaseMva * h) : new Complex(0, s.QMvar / n.BaseMva / h);
                // Loads as parallel resistances (damping) at their active power.
                foreach (var l in n.Loads.Where(l => l.PMw > 0)) y[Index[l.Bus], Index[l.Bus]] += new Complex(l.PMw / n.BaseMva, 0);
                break;
        }
        return y;
    }
}
