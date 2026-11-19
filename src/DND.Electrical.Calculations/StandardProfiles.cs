namespace DND.Electrical.Calculations;

public enum ImplementationStatus { Implemented, Partial, NotImplemented }

public sealed record StandardClause(string Reference, string Topic, ImplementationStatus Status, string Notes);

/// <summary>
/// Describes which parts of a standard are implemented by the calculation engine. The application never
/// claims compliance; profiles document the formulas and data sources used so that engineers can verify them.
/// </summary>
public sealed record StandardProfile(string Id, string Name, string Scope, IReadOnlyList<StandardClause> Clauses)
{
    public static readonly IReadOnlyList<StandardProfile> All = new[]
    {
        new StandardProfile("IEC", "IEC 60364 (Low-voltage electrical installations)", "General LV installation design",
            new[]
            {
                new StandardClause("IEC 60364-5-52 Annex G", "Voltage drop (formula G.1, Table G.52.1 limits)", ImplementationStatus.Implemented, "ρ1 = 1.25·ρ20, λ = 0.08 mΩ/m."),
                new StandardClause("IEC 60364-5-52 Annex B", "Current-carrying capacity", ImplementationStatus.Partial, "Copper, methods C and E, PVC/XLPE, 2 or 3 loaded conductors. Values transcribed from Tables B.52.2–B.52.13; verify against the licensed standard."),
                new StandardClause("IEC 60364-5-52 B.52.14", "Ambient temperature correction (air)", ImplementationStatus.Implemented, "k1 = √((θmax − θa)/(θmax − 30)); matches tabulated factors."),
                new StandardClause("IEC 60364-5-52 B.52.17", "Grouping correction", ImplementationStatus.Partial, "Arrangement 1 (bunched, on surface, enclosed); conservative rounding to next tabulated count."),
                new StandardClause("IEC 60364-4-43 433.1", "Coordination conductor/overload device", ImplementationStatus.Implemented, "Ib ≤ In ≤ Iz and I2 ≤ 1.45·Iz."),
                new StandardClause("IEC 60364-4-41 411.4.4", "Automatic disconnection in TN systems", ImplementationStatus.Partial, "Zs·Ia ≤ U0 check and conventional maximum-length method (0.8·U0 at origin)."),
                new StandardClause("IEC 60364-5-54", "Earthing arrangements / PE sizing", ImplementationStatus.NotImplemented, "Planned."),
            }),
        new StandardProfile("IEC60204", "IEC 60204-1 (Safety of machinery – electrical equipment)", "Machine electrical equipment",
            new[]
            {
                new StandardClause("IEC 60204-1 (voltage drop)", "Voltage drop limit 5 % under normal operation", ImplementationStatus.Implemented, "Applied as default limit in the voltage drop check."),
                new StandardClause("IEC 60204-1 Annex D", "Current-carrying capacity for machine wiring", ImplementationStatus.NotImplemented, "Use the IEC 60364 tables as approximation only."),
            }),
        new StandardProfile("IEC60909", "IEC 60909-0 (Short-circuit currents in three-phase AC systems)", "Short-circuit estimation",
            new[]
            {
                new StandardClause("IEC 60909-0 equivalent voltage source", "Maximum three-phase short-circuit current I\"k3", ImplementationStatus.Partial, "Radial LV network fed by one transformer; network feeder, transformer (with KT correction) and cable impedances."),
                new StandardClause("IEC 60909-0 4.3.1.1", "Peak short-circuit current ip (κ method)", ImplementationStatus.Implemented, "κ = 1.02 + 0.98·e^(−3R/X)."),
                new StandardClause("IEC 60909-0 line-to-earth", "Single-phase short-circuit", ImplementationStatus.NotImplemented, "Requires zero-sequence data; planned."),
            }),
        new StandardProfile("IEC61439", "IEC 61439 (LV switchgear and controlgear assemblies)", "Assemblies",
            new[]
            {
                new StandardClause("IEC 61439-1 10.10", "Temperature-rise verification", ImplementationStatus.NotImplemented, "Framework only. No compliance verification is performed."),
            }),
        new StandardProfile("NFC15100", "NF C 15-100 (French LV installations)", "French installations",
            new[]
            {
                new StandardClause("NF C 15-100 525", "Voltage drop limits (3 %/5 % public supply, 6 %/8 % private)", ImplementationStatus.Implemented, "Same limits as IEC 60364-5-52 Table G.52.1."),
                new StandardClause("NF C 15-100 Part 5-52", "Current-carrying capacity", ImplementationStatus.Partial, "Uses the IEC 60364-5-52 data set; national reference methods and letters are not mapped."),
            }),
    };

    public static StandardProfile Get(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
