using System.Globalization;
using System.Text;

namespace DND.Electrical.Power;

public enum CurveType
{
    IecStandardInverse, IecVeryInverse, IecExtremelyInverse, IecLongTimeInverse, DefiniteTime,
    /// <summary>Miniature circuit-breaker type B/C/D band (instantaneous thresholds of IEC 60898-1).</summary>
    MiniatureBreakerB, MiniatureBreakerC, MiniatureBreakerD,
    /// <summary>Time-current points from manufacturer data (fuses, MCCB/ACB releases).</summary>
    UserDefined,
}

public sealed class CurvePoint
{
    public double CurrentA { get; set; }
    public double MinTimeS { get; set; }
    public double MaxTimeS { get; set; }
}

public sealed class ProtectionDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Bus where the device is installed (its maximum fault current is taken from the short-circuit study).</summary>
    public string Bus { get; set; } = string.Empty;
    public string? UpstreamId { get; set; }
    public CurveType Curve { get; set; } = CurveType.IecStandardInverse;
    /// <summary>Overcurrent pickup Is (relays) or rated current In (MCB) in amperes, referred to the device's bus voltage.</summary>
    public double PickupA { get; set; } = 100;
    public double Tms { get; set; } = 0.1;
    public double DefiniteTimeS { get; set; } = 0.5;
    /// <summary>Instantaneous / high-set element pickup (0 = disabled).</summary>
    public double InstantaneousA { get; set; }
    public double InstantaneousTimeS { get; set; } = 0.05;
    public List<CurvePoint> Points { get; set; } = new();
    public Guid? ObjectId { get; set; }
}

public sealed record CoordinationFinding(string Downstream, string Upstream, bool Selective, double MinMarginS, double AtCurrentA, string Message);

/// <summary>
/// Time-current characteristics and selectivity checks. IEC 60255-151 inverse curves t = TMS·k/((I/Is)^α − 1),
/// held constant above 20 × Is;
/// MCB bands from the conventional currents and instantaneous ranges of IEC 60898-1; manufacturer curves as points.
/// Banded devices are checked with their maximum trip time downstream and minimum trip time upstream.
/// </summary>
public static class ProtectionStudy
{
    private static (double k, double a) Iec(CurveType c) => c switch
    {
        CurveType.IecStandardInverse => (0.14, 0.02),
        CurveType.IecVeryInverse => (13.5, 1.0),
        CurveType.IecExtremelyInverse => (80.0, 2.0),
        CurveType.IecLongTimeInverse => (120.0, 1.0),
        _ => (0, 0),
    };

    public static double MinTime(ProtectionDevice d, double currentA) => Time(d, currentA, max: false);
    public static double MaxTime(ProtectionDevice d, double currentA) => Time(d, currentA, max: true);

    private static double Time(ProtectionDevice d, double i, bool max)
    {
        double t;
        switch (d.Curve)
        {
            case CurveType.IecStandardInverse or CurveType.IecVeryInverse or CurveType.IecExtremelyInverse or CurveType.IecLongTimeInverse:
                {
                    var (k, a) = Iec(d.Curve);
                    // Inverse characteristics are specified up to 20 × Is; above that the operate time is held constant.
                    var m = Math.Min(i / d.PickupA, 20);
                    t = i <= d.PickupA ? double.PositiveInfinity : d.Tms * k / (Math.Pow(m, a) - 1);
                    break;
                }
            case CurveType.DefiniteTime:
                t = i >= d.PickupA ? d.DefiniteTimeS : double.PositiveInfinity;
                break;
            case CurveType.MiniatureBreakerB or CurveType.MiniatureBreakerC or CurveType.MiniatureBreakerD:
                t = Mcb(d, i, max);
                break;
            case CurveType.UserDefined:
                t = Interpolate(d.Points, i, max);
                break;
            default:
                t = double.PositiveInfinity;
                break;
        }
        if (d.InstantaneousA > 0 && i >= d.InstantaneousA) t = Math.Min(t, d.InstantaneousTimeS);
        return t;
    }

    /// <summary>
    /// MCB band: no trip below 1.13·In; tripping within 1 h at 1.45·In; 2.55·In between 1 s and 60 s (120 s above 32 A);
    /// instantaneous between the lower and upper magnetic thresholds (B 3–5, C 5–10, D 10–20 × In) in less than 0.1 s.
    /// Intermediate values are interpolated on log–log axes (band envelope, not a manufacturer curve).
    /// </summary>
    private static double Mcb(ProtectionDevice d, double i, bool max)
    {
        var inr = d.PickupA; var m = i / inr;
        var (lo, hi) = d.Curve switch { CurveType.MiniatureBreakerB => (3.0, 5.0), CurveType.MiniatureBreakerC => (5.0, 10.0), _ => (10.0, 20.0) };
        var longT = inr <= 32 ? 60.0 : 120.0;
        if (max)
        {
            if (m < 1.45) return double.PositiveInfinity;
            if (m >= hi) return 0.1;
            var pts = new[] { (1.45, 3600.0), (2.55, longT), (hi * 0.999, longT * Math.Pow(2.55 / (hi * 0.999), 2)) };
            return LogInterp(pts, m);
        }
        if (m < 1.13) return double.PositiveInfinity;
        if (m >= lo) return 0.005;
        var minPts = new[] { (1.13, 3600.0), (2.55, 1.0), (lo * 0.999, Math.Max(0.1, 1.0 * Math.Pow(2.55 / (lo * 0.999), 2))) };
        return LogInterp(minPts, m);
    }

    private static double LogInterp((double x, double y)[] pts, double x)
    {
        if (x <= pts[0].x) return pts[0].y;
        for (var i = 0; i + 1 < pts.Length; i++)
            if (x <= pts[i + 1].x)
            {
                var f = (Math.Log(x) - Math.Log(pts[i].x)) / (Math.Log(pts[i + 1].x) - Math.Log(pts[i].x));
                return Math.Exp(Math.Log(pts[i].y) + f * (Math.Log(pts[i + 1].y) - Math.Log(pts[i].y)));
            }
        return pts[^1].y;
    }

    private static double Interpolate(List<CurvePoint> points, double i, bool max)
    {
        var p = points.Where(x => x.CurrentA > 0).OrderBy(x => x.CurrentA).ToList();
        if (p.Count == 0 || i < p[0].CurrentA) return double.PositiveInfinity;
        var arr = p.Select(x => (x.CurrentA, Math.Max(1e-3, max ? (x.MaxTimeS > 0 ? x.MaxTimeS : x.MinTimeS) : x.MinTimeS))).ToArray();
        return LogInterp(arr, i);
    }

    /// <summary>Checks every device against its upstream device between the downstream pickup and the maximum fault current at the downstream bus.</summary>
    public static IReadOnlyList<CoordinationFinding> Coordinate(PowerNetwork network, ShortCircuitResult? faults = null)
    {
        var findings = new List<CoordinationFinding>();
        var byId = network.Protection.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var down in network.Protection.Where(p => p.UpstreamId is not null))
        {
            if (!byId.TryGetValue(down.UpstreamId!, out var up)) { findings.Add(new(down.Id, down.UpstreamId!, false, double.NaN, 0, $"{down.Id}: upstream device '{down.UpstreamId}' not found.")); continue; }
            var iMax = faults?.Faults.FirstOrDefault(f => string.Equals(f.Bus, down.Bus, StringComparison.OrdinalIgnoreCase))?.Ik3MaxKa * 1000;
            if (iMax is null or <= 0) { findings.Add(new(down.Id, up.Id, false, double.NaN, 0, $"{down.Id} → {up.Id}: no fault current at bus {down.Bus}; run the short-circuit study first.")); continue; }
            var iStart = down.PickupA * 1.05;
            double worst = double.PositiveInfinity, at = 0; bool instantaneousOverlap = false;
            for (var s = 0; s <= 200; s++)
            {
                var i = iStart * Math.Pow(iMax.Value / iStart, s / 200.0);
                var td = MaxTime(down, i); var tu = MinTime(up, i);
                if (double.IsInfinity(td) || double.IsInfinity(tu)) continue;
                var margin = tu - td;
                if (tu <= 0.1 && td <= 0.1) instantaneousOverlap = true;
                if (margin < worst) { worst = margin; at = i; }
            }
            var marginReq = network.GradingMarginS;
            if (double.IsPositiveInfinity(worst))
                findings.Add(new(down.Id, up.Id, true, worst, 0, $"{down.Id} → {up.Id}: upstream device does not operate below {iMax.Value / 1000:0.##} kA – selective."));
            else if (instantaneousOverlap)
                findings.Add(new(down.Id, up.Id, false, worst, at, $"{down.Id} → {up.Id}: both devices are instantaneous at {at / 1000:0.##} kA – selectivity cannot be shown from time-current curves; use the manufacturer's selectivity tables."));
            else
            {
                var ok = worst >= marginReq - 1e-9;
                findings.Add(new(down.Id, up.Id, ok, worst, at, ok
                    ? $"{down.Id} → {up.Id}: minimum margin {worst:0.###} s at {at:0} A ≥ {marginReq:0.###} s – selective."
                    : $"{down.Id} → {up.Id}: margin {worst:0.###} s at {at:0} A is below {marginReq:0.###} s – not selective."));
            }
        }
        return findings;
    }

    /// <summary>Log–log time-current chart as SVG (curves, bands and fault currents).</summary>
    public static string TccSvg(PowerNetwork network, ShortCircuitResult? faults, int width = 900, int height = 650)
    {
        var devices = network.Protection;
        var iMin = Math.Max(1, devices.Select(d => d.PickupA).DefaultIfEmpty(10).Min() / 2);
        var iMax = Math.Max(iMin * 100, (faults?.Faults.Max(f => f.Ik3MaxKa) ?? 1) * 1000 * 2);
        double tMin = 0.005, tMax = 10000;
        const int ml = 70, mr = 20, mt = 30, mb = 50;
        double X(double i) => ml + (Math.Log10(i) - Math.Log10(iMin)) / (Math.Log10(iMax) - Math.Log10(iMin)) * (width - ml - mr);
        double Y(double t) => mt + (Math.Log10(tMax) - Math.Log10(t)) / (Math.Log10(tMax) - Math.Log10(tMin)) * (height - mt - mb);
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" font-family=\"DejaVu Sans, Arial\" font-size=\"11\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{width}\" height=\"{height}\" fill=\"white\"/><text x=\"{ml}\" y=\"18\" font-size=\"13\" font-weight=\"bold\">Time-current characteristics – {System.Security.SecurityElement.Escape(network.Name)}</text>");
        for (var e = (int)Math.Floor(Math.Log10(iMin)); e <= Math.Ceiling(Math.Log10(iMax)); e++)
            for (var mlt = 1; mlt < 10; mlt++)
            {
                var i = mlt * Math.Pow(10, e); if (i < iMin || i > iMax) continue;
                sb.Append($"<line x1=\"{F(X(i))}\" y1=\"{mt}\" x2=\"{F(X(i))}\" y2=\"{height - mb}\" stroke=\"{(mlt == 1 ? "#9CA3AF" : "#E5E7EB")}\"/>");
                if (mlt == 1) sb.Append($"<text x=\"{F(X(i))}\" y=\"{height - mb + 15}\" text-anchor=\"middle\">{(i >= 1000 ? F(i / 1000) + " kA" : F(i) + " A")}</text>");
            }
        for (var e = -2; e <= 4; e++)
        {
            var t = Math.Pow(10, e);
            sb.Append($"<line x1=\"{ml}\" y1=\"{F(Y(t))}\" x2=\"{width - mr}\" y2=\"{F(Y(t))}\" stroke=\"#9CA3AF\"/><text x=\"{ml - 6}\" y=\"{F(Y(t) + 4)}\" text-anchor=\"end\">{F(t)} s</text>");
        }
        var colors = new[] { "#2563EB", "#DC2626", "#059669", "#D97706", "#7C3AED", "#DB2777", "#0891B2", "#4B5563" };
        var ci = 0;
        foreach (var d in devices)
        {
            var col = colors[ci++ % colors.Length];
            foreach (var max in new[] { true, false })
            {
                var pts = new List<string>();
                for (var s = 0; s <= 300; s++)
                {
                    var i = iMin * Math.Pow(iMax / iMin, s / 300.0);
                    var t = max ? MaxTime(d, i) : MinTime(d, i);
                    if (double.IsInfinity(t) || t > tMax) continue;
                    pts.Add($"{F(X(i))},{F(Y(Math.Max(tMin, t)))}");
                }
                if (pts.Count > 1) sb.Append($"<polyline fill=\"none\" stroke=\"{col}\" stroke-width=\"{(max ? 2 : 1)}\"{(max ? "" : " stroke-dasharray=\"4 3\"")} points=\"{string.Join(' ', pts)}\"/>");
            }
            sb.Append($"<text x=\"{width - mr - 4}\" y=\"{mt + 14 * ci}\" text-anchor=\"end\" fill=\"{col}\">{System.Security.SecurityElement.Escape(d.Id)} {d.Curve} {(d.Curve is CurveType.MiniatureBreakerB or CurveType.MiniatureBreakerC or CurveType.MiniatureBreakerD ? "In" : "Is")}={F(d.PickupA)} A</text>");
        }
        if (faults is not null)
        {
            var row = 0;
            foreach (var f in faults.Faults.Where(f => network.Protection.Any(p => string.Equals(p.Bus, f.Bus, StringComparison.OrdinalIgnoreCase))).OrderBy(f => f.Ik3MaxKa))
            {
                var x = X(f.Ik3MaxKa * 1000);
                sb.Append($"<line x1=\"{F(x)}\" y1=\"{mt}\" x2=\"{F(x)}\" y2=\"{height - mb}\" stroke=\"#111827\" stroke-dasharray=\"2 4\"/><text x=\"{F(x - 3)}\" y=\"{height - mb - 6 - 12 * row++}\" font-size=\"10\" text-anchor=\"end\">I\"k3 {System.Security.SecurityElement.Escape(f.Bus)} {F(f.Ik3MaxKa)} kA</text>");
            }
        }
        sb.Append($"<text x=\"{width / 2}\" y=\"{height - 10}\" text-anchor=\"middle\">Current (A, referred to each device's voltage) – solid: maximum, dashed: minimum trip time</text></svg>");
        return sb.ToString();
    }
}
