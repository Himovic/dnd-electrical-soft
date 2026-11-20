using System.Numerics;

namespace DND.Electrical.Power;

public enum BusType { PQ, PV, Slack }

public sealed record BusResult(string Bus, string Name, double Kv, BusType Type, double VPu, double AngleDeg, double PGenMw, double QGenMvar, double PLoadMw, double QLoadMvar, bool VoltageViolation)
{
    public double VKv => VPu * Kv;
}

public sealed record BranchResult(string Id, string Kind, string From, string To, double PFromMw, double QFromMvar, double PToMw, double QToMvar, double CurrentA, double LoadingPercent)
{
    public double LossMw => PFromMw + PToMw;
}

public sealed class LoadFlowResult
{
    public bool Converged { get; init; }
    public int Iterations { get; init; }
    public double MaxMismatchMva { get; init; }
    public IReadOnlyList<BusResult> Buses { get; init; } = Array.Empty<BusResult>();
    public IReadOnlyList<BranchResult> Branches { get; init; } = Array.Empty<BranchResult>();
    public double LossesMw => Branches.Sum(b => b.LossMw);
    public double LossesMvar => Branches.Sum(b => b.QFromMvar + b.QToMvar);
    public List<string> Messages { get; } = new();
    public List<string> Assumptions { get; } = new();
    public BusResult Bus(string id) => Buses.First(b => string.Equals(b.Bus, id, StringComparison.OrdinalIgnoreCase));
}

public sealed record LoadFlowOptions(double ToleranceMva = 1e-6, int MaxIterations = 30, bool EnforceQLimits = true, double OverloadPercent = 100);

/// <summary>
/// AC load flow by the Newton–Raphson method in polar coordinates (full Jacobian from the complex power
/// derivatives). Loads are constant power; generators regulate their bus voltage within reactive limits
/// (PV → PQ switching); the external grid (or the first generator) is the slack bus.
/// </summary>
public static class LoadFlowSolver
{
    public static LoadFlowResult Solve(PowerNetwork network, LoadFlowOptions? options = null)
    {
        options ??= new LoadFlowOptions();
        var m = new NetworkModel(network);
        var n = m.N;
        var y = m.Ybus(AdmittanceMode.LoadFlow);
        var type = new BusType[n];
        var vset = Enumerable.Repeat(1.0, n).ToArray();
        var pSpec = new double[n]; var qSpec = new double[n];
        var pLoad = new double[n]; var qLoad = new double[n];
        foreach (var l in network.Loads) { var i = m.Index[l.Bus]; pLoad[i] += l.PMw; qLoad[i] += l.QMvar; }
        var gens = network.Generators.Where(g => g.InService).ToList();
        var slackSet = false;
        foreach (var g in network.Grids) { var i = m.Index[g.Bus]; type[i] = BusType.Slack; vset[i] = g.VoltagePu; slackSet = true; }
        foreach (var g in gens)
        {
            var i = m.Index[g.Bus];
            if (type[i] == BusType.Slack) continue;
            if (!slackSet) { type[i] = BusType.Slack; slackSet = true; }
            else type[i] = BusType.PV;
            vset[i] = g.VsetPu;
        }
        for (var i = 0; i < n; i++) { pSpec[i] = -pLoad[i] / network.BaseMva; qSpec[i] = -qLoad[i] / network.BaseMva; }
        foreach (var g in gens) pSpec[m.Index[g.Bus]] += g.PMw / network.BaseMva;

        var v = new Complex[n];
        var angle0 = network.Grids.FirstOrDefault()?.VoltagePu is not null ? 0 : 0;
        for (var i = 0; i < n; i++) v[i] = Complex.FromPolarCoordinates(type[i] == BusType.PQ ? 1.0 : vset[i], angle0);

        var result = new LoadFlowResult();
        int totalIterations = 0; double mismatch = double.MaxValue; bool converged = false;
        var fixedQ = new Dictionary<int, double>();
        for (var outer = 0; outer < 10; outer++)
        {
            (converged, var it, mismatch) = Newton(y, v, type, pSpec, qSpec, fixedQ, options);
            totalIterations += it;
            if (!converged || !options.EnforceQLimits) break;
            // Reactive limits of PV buses
            var s = Injection(y, v);
            var changed = false;
            foreach (var grp in gens.GroupBy(g => m.Index[g.Bus]).Where(g => type[g.Key] == BusType.PV))
            {
                var qGen = (s[grp.Key].Imaginary * network.BaseMva) + qLoad[grp.Key];
                double qmin = grp.Sum(g => g.QminMvar), qmax = grp.Sum(g => g.QmaxMvar);
                if (qGen > qmax + 1e-6 || qGen < qmin - 1e-6)
                {
                    type[grp.Key] = BusType.PQ;
                    var lim = qGen > qmax ? qmax : qmin;
                    qSpec[grp.Key] = (lim - qLoad[grp.Key]) / network.BaseMva;
                    fixedQ[grp.Key] = lim;
                    result.Messages.Add($"Bus {m.Buses[grp.Key].Id}: generator reactive power {qGen:0.##} Mvar outside [{qmin:0.##}, {qmax:0.##}] – limited to {lim:0.##} Mvar (PV → PQ).");
                    changed = true;
                }
            }
            if (!changed) break;
        }

        var sInj = Injection(y, v);
        var buses = new List<BusResult>();
        for (var i = 0; i < n; i++)
        {
            var b = m.Buses[i];
            double pg = 0, qg = 0;
            if (type[i] == BusType.Slack || gens.Any(g => m.Index[g.Bus] == i) || network.Grids.Any(g => m.Index[g.Bus] == i))
            {
                pg = sInj[i].Real * network.BaseMva + pLoad[i];
                qg = sInj[i].Imaginary * network.BaseMva + qLoad[i];
            }
            var vm = v[i].Magnitude;
            buses.Add(new BusResult(b.Id, b.Name, b.Kv, type[i], vm, v[i].Phase * 180 / Math.PI, pg, qg, pLoad[i], qLoad[i], vm < b.VminPu - 1e-9 || vm > b.VmaxPu + 1e-9));
        }
        var branches = new List<BranchResult>();
        foreach (var br in m.Branches(AdmittanceMode.LoadFlow))
        {
            var vf = v[br.From]; var vt = v[br.To];
            var sf = vf * Complex.Conjugate(br.Yff * vf + br.Yft * vt) * network.BaseMva;
            var st = vt * Complex.Conjugate(br.Ytf * vf + br.Ytt * vt) * network.BaseMva;
            var iA = Math.Max(sf.Magnitude / (Math.Sqrt(3) * m.Buses[br.From].Kv * vf.Magnitude), st.Magnitude / (Math.Sqrt(3) * m.Buses[br.To].Kv * vt.Magnitude)) * 1000;
            var iFromA = sf.Magnitude / (Math.Sqrt(3) * m.Buses[br.From].Kv * vf.Magnitude) * 1000;
            var iToA = st.Magnitude / (Math.Sqrt(3) * m.Buses[br.To].Kv * vt.Magnitude) * 1000;
            double loading = 0;
            if (br.Kind == "Transformer" && br.RatingMva > 0) loading = Math.Max(sf.Magnitude, st.Magnitude) / br.RatingMva * 100;
            else if (br.RatingA > 0) loading = iA / br.RatingA * 100;
            branches.Add(new BranchResult(br.Id, br.Kind, m.Buses[br.From].Id, m.Buses[br.To].Id, sf.Real, sf.Imaginary, st.Real, st.Imaginary, br.Kind == "Transformer" ? iToA : Math.Max(iFromA, iToA), loading));
        }
        var res = new LoadFlowResult { Converged = converged, Iterations = totalIterations, MaxMismatchMva = mismatch * network.BaseMva, Buses = buses, Branches = branches };
        res.Messages.AddRange(result.Messages);
        res.Assumptions.Add("Balanced three-phase operation; constant-power loads; line charging and shunts included.");
        res.Assumptions.AddRange(m.Assumptions);
        if (!converged) res.Messages.Add($"Load flow did not converge after {totalIterations} iterations (max mismatch {mismatch * network.BaseMva:0.###} MVA). Check data, loading and voltage set points.");
        foreach (var b in buses.Where(b => b.VoltageViolation)) res.Messages.Add($"Bus {b.Bus}: voltage {b.VPu:0.###} pu outside [{m.Buses.First(x => x.Id == b.Bus).VminPu}, {m.Buses.First(x => x.Id == b.Bus).VmaxPu}] pu.");
        foreach (var br in branches.Where(b => b.LoadingPercent > options.OverloadPercent)) res.Messages.Add($"{br.Kind} {br.Id}: loading {br.LoadingPercent:0.#} %.");
        return res;
    }

    private static Complex[] Injection(Complex[,] y, Complex[] v)
    {
        var n = v.Length; var s = new Complex[n];
        for (var i = 0; i < n; i++)
        {
            var cur = Complex.Zero;
            for (var k = 0; k < n; k++) if (y[i, k] != Complex.Zero) cur += y[i, k] * v[k];
            s[i] = v[i] * Complex.Conjugate(cur);
        }
        return s;
    }

    private static (bool converged, int iterations, double mismatch) Newton(Complex[,] y, Complex[] v, BusType[] type, double[] pSpec, double[] qSpec, Dictionary<int, double> fixedQ, LoadFlowOptions o)
    {
        var n = v.Length;
        var pvpq = Enumerable.Range(0, n).Where(i => type[i] != BusType.Slack).ToArray();
        var pq = Enumerable.Range(0, n).Where(i => type[i] == BusType.PQ).ToArray();
        var vmag = v.Select(c => c.Magnitude).ToArray();
        double mismatch = double.MaxValue;
        for (var it = 0; it <= o.MaxIterations; it++)
        {
            // Mismatch F = S(V) − Sspec
            var cur = new Complex[n];
            for (var i = 0; i < n; i++) { var c = Complex.Zero; for (var k = 0; k < n; k++) if (y[i, k] != Complex.Zero) c += y[i, k] * v[k]; cur[i] = c; }
            var s = new Complex[n];
            for (var i = 0; i < n; i++) s[i] = v[i] * Complex.Conjugate(cur[i]);
            var f = new double[pvpq.Length + pq.Length];
            for (var a = 0; a < pvpq.Length; a++) f[a] = s[pvpq[a]].Real - pSpec[pvpq[a]];
            for (var a = 0; a < pq.Length; a++) f[pvpq.Length + a] = s[pq[a]].Imaginary - qSpec[pq[a]];
            mismatch = f.Length == 0 ? 0 : f.Max(Math.Abs);
            if (mismatch < o.ToleranceMva / 100) return (true, it, mismatch);
            if (it == o.MaxIterations || double.IsNaN(mismatch)) break;

            // dS/dθ = j·diag(V)·conj(diag(I) − Y·diag(V)) ; dS/d|V| = diag(V)·conj(Y·diag(V/|V|)) + conj(diag(I))·diag(V/|V|)
            var dim = f.Length;
            var j = new double[dim, dim];
            var vn = v.Select(c => c / c.Magnitude).ToArray();
            var posA = new int[n]; Array.Fill(posA, -1); for (var a = 0; a < pvpq.Length; a++) posA[pvpq[a]] = a;
            var posV = new int[n]; Array.Fill(posV, -1); for (var a = 0; a < pq.Length; a++) posV[pq[a]] = a;
            for (var r = 0; r < n; r++)
            {
                if (type[r] == BusType.Slack) continue;
                for (var k = 0; k < n; k++)
                {
                    var yrk = y[r, k];
                    if (yrk == Complex.Zero && r != k) continue;
                    // dS_r/dθ_k
                    var dTheta = r == k
                        ? Complex.ImaginaryOne * v[r] * Complex.Conjugate(cur[r] - yrk * v[k])
                        : Complex.ImaginaryOne * v[r] * Complex.Conjugate(-yrk * v[k]);
                    var dVm = v[r] * Complex.Conjugate(yrk * vn[k]) + (r == k ? Complex.Conjugate(cur[r]) * vn[r] : Complex.Zero);
                    var rowP = posA[r]; var rowQ = posV[r] >= 0 ? pvpq.Length + posV[r] : -1;
                    if (posA[k] >= 0)
                    {
                        j[rowP, posA[k]] = dTheta.Real;
                        if (rowQ >= 0) j[rowQ, posA[k]] = dTheta.Imaginary;
                    }
                    if (posV[k] >= 0)
                    {
                        j[rowP, pvpq.Length + posV[k]] = dVm.Real;
                        if (rowQ >= 0) j[rowQ, pvpq.Length + posV[k]] = dVm.Imaginary;
                    }
                }
            }
            var dx = RealLu.Solve(j, f);
            var theta = v.Select(c => c.Phase).ToArray();
            for (var a = 0; a < pvpq.Length; a++) theta[pvpq[a]] -= dx[a];
            for (var a = 0; a < pq.Length; a++) vmag[pq[a]] -= dx[pvpq.Length + a];
            for (var i = 0; i < n; i++) v[i] = Complex.FromPolarCoordinates(vmag[i], theta[i]);
        }
        return (false, o.MaxIterations, mismatch);
    }
}
