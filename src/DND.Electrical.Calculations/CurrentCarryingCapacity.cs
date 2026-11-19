namespace DND.Electrical.Calculations;

public enum InstallationMethod
{
    /// <summary>Reference method C: single- or multi-core cable clipped direct to a wall.</summary>
    C,
    /// <summary>Reference method E: multi-core cable in free air (perforated tray, spaced from wall).</summary>
    E,
}

public enum CableInsulation { PVC, XLPE }

/// <summary>
/// Current-carrying capacities Iz0 (A) for copper conductors, ambient air 30 °C, reference methods C and E
/// (IEC 60364-5-52 Annex B, Tables B.52.2–B.52.13). Values are transcribed reference data:
/// they must be verified against the licensed edition of the standard before being relied upon.
/// </summary>
public static class CurrentCarryingCapacity
{
    public const string Source = "IEC 60364-5-52:2009 Annex B (copper, methods C and E), transcribed reference values";

    private static readonly double[] Sections = ConductorData.StandardSections;

    // Order: 1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300 mm²
    private static readonly Dictionary<(InstallationMethod, CableInsulation, int), double[]> Tables = new()
    {
        [(InstallationMethod.C, CableInsulation.PVC, 2)] = new[] { 19.5, 27, 36, 46, 63, 85, 112, 138, 168, 213, 258, 299, 344, 392, 461, 530 },
        [(InstallationMethod.C, CableInsulation.PVC, 3)] = new[] { 17.5, 24, 32, 41, 57, 76, 96, 119, 144, 184, 223, 259, 299, 341, 403, 464 },
        [(InstallationMethod.C, CableInsulation.XLPE, 2)] = new[] { 24.0, 33, 45, 58, 80, 107, 138, 171, 209, 269, 328, 382, 441, 506, 599, 693 },
        [(InstallationMethod.C, CableInsulation.XLPE, 3)] = new[] { 22.0, 30, 40, 52, 71, 96, 119, 147, 179, 229, 278, 322, 371, 424, 500, 576 },
        [(InstallationMethod.E, CableInsulation.PVC, 2)] = new[] { 22.0, 30, 40, 51, 70, 94, 119, 148, 180, 232, 282, 328, 379, 434, 514, 593 },
        [(InstallationMethod.E, CableInsulation.PVC, 3)] = new[] { 18.5, 25, 34, 43, 60, 80, 101, 126, 153, 196, 238, 276, 319, 364, 430, 497 },
        [(InstallationMethod.E, CableInsulation.XLPE, 2)] = new[] { 26.0, 36, 49, 63, 86, 115, 149, 185, 225, 289, 352, 410, 473, 542, 641, 741 },
        [(InstallationMethod.E, CableInsulation.XLPE, 3)] = new[] { 23.0, 32, 42, 54, 75, 100, 127, 158, 192, 246, 298, 346, 399, 456, 538, 621 },
    };

    public static double MaxConductorTemperature(CableInsulation i) => i == CableInsulation.PVC ? 70 : 90;

    /// <summary>Tabulated Iz0 for the given configuration, or null when outside the data set.</summary>
    public static double? Lookup(InstallationMethod method, CableInsulation insulation, int loadedConductors, double sectionMm2)
    {
        var lc = loadedConductors <= 2 ? 2 : 3;
        if (!Tables.TryGetValue((method, insulation, lc), out var t)) return null;
        var idx = Array.FindIndex(Sections, s => Math.Abs(s - sectionMm2) < 1e-9);
        return idx < 0 ? null : t[idx];
    }

    /// <summary>
    /// Ambient air temperature correction factor k1 = √((θmax − θa)/(θmax − 30)) (basis of Table B.52.14).
    /// Returns 0 when the ambient temperature reaches the conductor limit.
    /// </summary>
    public static double TemperatureFactor(CableInsulation insulation, double ambientC)
    {
        var tmax = MaxConductorTemperature(insulation);
        if (ambientC >= tmax) return 0;
        return Math.Sqrt((tmax - ambientC) / (tmax - 30));
    }

    private static readonly (int n, double f)[] Grouping =
    {
        (1, 1.00), (2, 0.80), (3, 0.70), (4, 0.65), (5, 0.60), (6, 0.57), (7, 0.54), (8, 0.52), (9, 0.50), (12, 0.45), (16, 0.41), (20, 0.38),
    };

    /// <summary>Grouping factor k2 (Table B.52.17, arrangement 1). Counts between rows use the next higher row (conservative).</summary>
    public static double GroupingFactor(int circuits)
    {
        if (circuits <= 1) return 1;
        foreach (var (n, f) in Grouping) if (circuits <= n) return f;
        return 0.38;
    }
}
