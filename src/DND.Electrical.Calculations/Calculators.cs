using static DND.Electrical.Calculations.CalculationResult;

namespace DND.Electrical.Calculations;

public static class PowerCalculator
{
    /// <summary>P, Q, S from voltage, current and power factor (single-phase: U = U L-N; three-phase: U = U L-L).</summary>
    public static CalculationResult PowerFromCurrent(SupplySystem system, double voltage, double current, double powerFactor)
    {
        Validate(voltage > 0, "Voltage must be positive.");
        Validate(current >= 0, "Current must not be negative.");
        Validate(powerFactor is > 0 and <= 1, "Power factor must be in (0, 1].");
        var k = system == SupplySystem.ThreePhase ? Math.Sqrt(3) : 1;
        var kTxt = system == SupplySystem.ThreePhase ? "√3·" : string.Empty;
        var s = k * voltage * current;
        var p = s * powerFactor;
        var sinPhi = Math.Sqrt(1 - powerFactor * powerFactor);
        var q = s * sinPhi;
        var r = new CalculationResult
        {
            CalculationType = "power",
            Title = system == SupplySystem.ThreePhase ? "Three-phase power" : "Single-phase power",
            StandardProfile = "Electrical theory (sinusoidal steady state)",
            Status = ResultStatus.Informational,
        };
        r.Input("Voltage" + (system == SupplySystem.ThreePhase ? " (line-to-line)" : ""), voltage, "V", 1).Input("Current", current, "A").Input("Power factor cos φ", powerFactor, "", 3);
        r.Step("Apparent power", $"S = {kTxt}U·I", $"{kTxt}{F(voltage, 1)}·{F(current)}", s / 1000, "kVA");
        r.Step("Active power", "P = S·cos φ", $"{F(s / 1000)}·{F(powerFactor)}", p / 1000, "kW");
        r.Step("Reactive power", "Q = S·sin φ", $"{F(s / 1000)}·{F(sinPhi)}", q / 1000, "kvar");
        r.Out("Active power P", p / 1000, "kW", 3).Out("Reactive power Q", q / 1000, "kvar", 3).Out("Apparent power S", s / 1000, "kVA", 3);
        r.Assumptions.Add("Balanced, sinusoidal supply (no harmonics).");
        r.Summary = $"P = {F(p / 1000)} kW, Q = {F(q / 1000)} kvar, S = {F(s / 1000)} kVA";
        return r;
    }

    /// <summary>Line current from active power: I = P / (k·U·cos φ·η), k = √3 (three-phase) or 1.</summary>
    public static CalculationResult CurrentFromPower(SupplySystem system, double powerKw, double voltage, double powerFactor, double efficiency = 1)
    {
        Validate(powerKw >= 0, "Power must not be negative.");
        Validate(voltage > 0, "Voltage must be positive.");
        Validate(powerFactor is > 0 and <= 1, "Power factor must be in (0, 1].");
        Validate(efficiency is > 0 and <= 1, "Efficiency must be in (0, 1].");
        var k = system == SupplySystem.ThreePhase ? Math.Sqrt(3) : 1;
        var i = powerKw * 1000 / (k * voltage * powerFactor * efficiency);
        var r = new CalculationResult
        {
            CalculationType = "current",
            Title = "Load current from power",
            StandardProfile = "Electrical theory",
            Status = ResultStatus.Informational,
        };
        r.Input("Active power (output)", powerKw, "kW", 3).Input("Voltage", voltage, "V", 1).Input("Power factor cos φ", powerFactor, "", 3).Input("Efficiency η", efficiency, "", 3);
        var kTxt = system == SupplySystem.ThreePhase ? "√3·" : "";
        r.Step("Line current", $"I = P / ({kTxt}U·cos φ·η)", $"{F(powerKw * 1000, 0)} / ({kTxt}{F(voltage, 0)}·{F(powerFactor)}·{F(efficiency)})", i, "A", 2);
        r.Out("Current I", i, "A");
        r.Out("Apparent power input S", k * voltage * i / 1000, "kVA", 3);
        r.Summary = $"I = {F(i, 2)} A";
        return r;
    }

    /// <summary>Power triangle from P and Q.</summary>
    public static CalculationResult PowerTriangle(double activeKw, double reactiveKvar)
    {
        var s = Math.Sqrt(activeKw * activeKw + reactiveKvar * reactiveKvar);
        Validate(s > 0, "P and Q cannot both be zero.");
        var r = new CalculationResult { CalculationType = "power-factor", Title = "Power factor and apparent power", StandardProfile = "Electrical theory", Status = ResultStatus.Informational };
        r.Input("Active power P", activeKw, "kW", 3).Input("Reactive power Q", reactiveKvar, "kvar", 3);
        r.Step("Apparent power", "S = √(P² + Q²)", $"√({F(activeKw)}² + {F(reactiveKvar)}²)", s, "kVA");
        r.Step("Power factor", "cos φ = P / S", $"{F(activeKw)} / {F(s)}", activeKw / s, "", 4);
        r.Out("Apparent power S", s, "kVA", 3).Out("Power factor cos φ", activeKw / s, "", 4);
        r.Summary = $"S = {F(s)} kVA, cos φ = {F(activeKw / s, 3)}";
        return r;
    }

    internal static void Validate(bool ok, string message)
    {
        if (!ok) throw new ArgumentException(message);
    }
}

public sealed record VoltageDropInput(
    SupplySystem System,
    double NominalVoltage,
    double CurrentA,
    double LengthM,
    double SectionMm2,
    Conductor Material = Conductor.Copper,
    double PowerFactor = 0.85,
    double ReactancePerMetre = ConductorData.ReactancePerMetre,
    int ParallelConductors = 1,
    double LimitPercent = 5);

public static class VoltageDropCalculator
{
    /// <summary>
    /// IEC 60364-5-52 Annex G: u = b·(ρ1·L/S·cos φ + λ·L·sin φ)·Ib ; Δu = 100·u/U0.
    /// b = 1 for three-phase (U0 = U/√3), b = 2 for single-phase (U0 = U).
    /// </summary>
    public static CalculationResult Calculate(VoltageDropInput i, string profile = "IEC 60364-5-52 Annex G")
    {
        PowerCalculator.Validate(i.NominalVoltage > 0 && i.CurrentA >= 0 && i.LengthM >= 0 && i.SectionMm2 > 0, "Voltage, section must be positive; current and length non-negative.");
        PowerCalculator.Validate(i.PowerFactor is > 0 and <= 1, "Power factor must be in (0, 1].");
        PowerCalculator.Validate(i.ParallelConductors >= 1, "Parallel conductors must be ≥ 1.");
        var b = i.System == SupplySystem.ThreePhase ? 1 : 2;
        var u0 = i.System == SupplySystem.ThreePhase ? i.NominalVoltage / Math.Sqrt(3) : i.NominalVoltage;
        var rho = ConductorData.RhoService(i.Material);
        var s = i.SectionMm2 * i.ParallelConductors;
        var sin = Math.Sqrt(1 - i.PowerFactor * i.PowerFactor);
        var lambda = i.ReactancePerMetre / i.ParallelConductors;
        var rTerm = rho * i.LengthM / s * i.PowerFactor;
        var xTerm = lambda * i.LengthM * sin;
        var u = b * (rTerm + xTerm) * i.CurrentA;
        var pct = 100 * u / u0;

        var r = new CalculationResult
        {
            CalculationType = "voltage-drop",
            Title = "Voltage drop",
            StandardProfile = profile,
            Method = "u = b·(ρ1·L/S·cos φ + λ·L·sin φ)·Ib",
        };
        r.Input("System", i.System == SupplySystem.ThreePhase ? 3 : 1, "phase(s)", 0).Input("Nominal voltage U", i.NominalVoltage, "V", 1)
            .Input("Design current Ib", i.CurrentA, "A").Input("Length L", i.LengthM, "m", 1).Input("Section S", i.SectionMm2, "mm²", 1)
            .Input("Parallel conductors", i.ParallelConductors, "", 0).Input("Resistivity ρ1", rho, "Ω·mm²/m", 4)
            .Input("Reactance λ", i.ReactancePerMetre * 1000, "mΩ/m", 3).Input("Power factor cos φ", i.PowerFactor, "", 3).Input("Limit", i.LimitPercent, "%", 1);
        r.Step("Reference voltage", b == 1 ? "U0 = U/√3" : "U0 = U", b == 1 ? $"{F(i.NominalVoltage, 1)}/√3" : F(i.NominalVoltage, 1), u0, "V", 2);
        r.Step("Resistive term", "ρ1·L/(n·S)·cos φ", $"{F(rho, 4)}·{F(i.LengthM, 1)}/{F(s, 1)}·{F(i.PowerFactor)}", rTerm, "Ω", 5);
        r.Step("Reactive term", "λ/n·L·sin φ", $"{F(lambda * 1000, 3)}e-3·{F(i.LengthM, 1)}·{F(sin)}", xTerm, "Ω", 5);
        r.Step("Voltage drop", "u = b·(R + X)·Ib", $"{b}·({F(rTerm, 5)} + {F(xTerm, 5)})·{F(i.CurrentA)}", u, "V", 3);
        r.Step("Relative voltage drop", "Δu = 100·u/U0", $"100·{F(u)}/{F(u0, 2)}", pct, "%", 2);
        r.Out("Voltage drop u", u, "V", 2).Out("Voltage drop Δu", pct, "%", 2);
        r.Check(pct <= i.LimitPercent, $"Δu = {F(pct, 2)} % ≤ {F(i.LimitPercent, 1)} % limit");
        r.Assumptions.Add("Conductor at normal service temperature (ρ1 = 1.25·ρ20).");
        r.Assumptions.Add("Balanced three-phase load (b = 1) / single-phase loop of phase and neutral (b = 2).");
        r.Assumptions.Add("Typical limits (IEC 60364-5-52 Table G.52.1): public LV supply 3 % lighting / 5 % other uses; private supply 6 % / 8 %. IEC 60204-1 machines: 5 %.");
        r.Summary = $"Δu = {F(pct, 2)} % ({F(u, 2)} V)";
        return r;
    }
}

public enum ProtectiveDevice { CircuitBreaker, FuseGG }

public sealed record CableSizingInput(
    SupplySystem System,
    double NominalVoltage,
    double DesignCurrentA,
    double LengthM,
    InstallationMethod Method = InstallationMethod.C,
    CableInsulation Insulation = CableInsulation.PVC,
    double AmbientTemperatureC = 30,
    int GroupedCircuits = 1,
    double PowerFactor = 0.85,
    double MaxVoltageDropPercent = 5,
    ProtectiveDevice Device = ProtectiveDevice.CircuitBreaker,
    double? DeviceRatingA = null,
    Conductor Material = Conductor.Copper);

public static class CableSizingCalculator
{
    /// <summary>
    /// Selects the smallest standard copper section satisfying overload coordination
    /// (IEC 60364-4-43: Ib ≤ In ≤ Iz, I2 ≤ 1.45·Iz) with temperature and grouping corrections,
    /// then increases the section until the voltage drop limit is met.
    /// </summary>
    public static CalculationResult Calculate(CableSizingInput i)
    {
        PowerCalculator.Validate(i.DesignCurrentA > 0, "Design current must be positive.");
        PowerCalculator.Validate(i.LengthM >= 0, "Length must not be negative.");
        var r = new CalculationResult
        {
            CalculationType = "cable-sizing",
            Title = "Cable sizing",
            StandardProfile = "IEC 60364-5-52 / 4-43 (" + CurrentCarryingCapacity.Source + ")",
            Method = $"Reference method {i.Method}, {i.Insulation}, {(i.System == SupplySystem.ThreePhase ? 3 : 2)} loaded conductors",
        };
        r.Input("Design current Ib", i.DesignCurrentA, "A").Input("Length", i.LengthM, "m", 1).Input("Nominal voltage", i.NominalVoltage, "V", 0)
            .Input("Ambient temperature θa", i.AmbientTemperatureC, "°C", 0).Input("Grouped circuits", i.GroupedCircuits, "", 0)
            .Input("Power factor", i.PowerFactor, "", 2).Input("Max. voltage drop", i.MaxVoltageDropPercent, "%", 1);

        if (i.Material != Conductor.Copper)
        {
            r.Warn("Current-carrying capacity data is only available for copper conductors. Aluminium sizing is not supported.");
            r.Status = ResultStatus.Fail;
            r.Summary = "Not calculated: aluminium tables not available.";
            return r;
        }

        var loaded = i.System == SupplySystem.ThreePhase ? 3 : 2;
        var k1 = CurrentCarryingCapacity.TemperatureFactor(i.Insulation, i.AmbientTemperatureC);
        var k2 = CurrentCarryingCapacity.GroupingFactor(i.GroupedCircuits);
        var tmax = CurrentCarryingCapacity.MaxConductorTemperature(i.Insulation);
        r.Step("Temperature factor", "k1 = √((θmax − θa)/(θmax − 30))", $"√(({F(tmax, 0)} − {F(i.AmbientTemperatureC, 0)})/({F(tmax, 0)} − 30))", k1, "", 3);
        r.Step("Grouping factor (Table B.52.17, arrangement 1)", "k2 = f(n)", $"n = {i.GroupedCircuits}", k2, "", 2);
        if (k1 <= 0)
        {
            r.Check(false, "Ambient temperature exceeds the insulation limit");
            r.Summary = "Ambient temperature too high for the insulation.";
            return r;
        }

        var inRating = i.DeviceRatingA ?? ConductorData.StandardRatings.FirstOrDefault(x => x >= i.DesignCurrentA);
        if (inRating <= 0) { r.Check(false, "No standard protective device rating ≥ Ib"); return r; }
        r.Step("Protective device rating", i.DeviceRatingA is null ? "In = smallest standard rating ≥ Ib" : "In (given)", $"Ib = {F(i.DesignCurrentA, 2)} A", inRating, "A", 0);
        var i2Factor = i.Device == ProtectiveDevice.CircuitBreaker ? 1.45 : (inRating >= 16 ? 1.6 : 1.9);
        // Required tabulated capacity: Iz0 ≥ In/(k1·k2), and for fuses also I2 ≤ 1.45·Iz ⇒ Iz ≥ I2/1.45.
        var requiredIz = Math.Max(inRating, i2Factor * inRating / 1.45);
        var requiredIz0 = requiredIz / (k1 * k2);
        r.Step("Required corrected capacity", i.Device == ProtectiveDevice.CircuitBreaker ? "Iz ≥ In" : "Iz ≥ max(In, I2/1.45), I2 = k·In", $"In = {F(inRating, 0)} A", requiredIz, "A", 2);
        r.Step("Required tabulated capacity", "Iz0 ≥ Iz / (k1·k2)", $"{F(requiredIz, 2)} / ({F(k1)}·{F(k2, 2)})", requiredIz0, "A", 2);

        double? chosen = null;
        foreach (var s in ConductorData.StandardSections)
        {
            var iz0 = CurrentCarryingCapacity.Lookup(i.Method, i.Insulation, loaded, s);
            if (iz0 is { } v && v >= requiredIz0) { chosen = s; break; }
        }
        if (chosen is null)
        {
            r.Check(false, "No single standard section up to 300 mm² satisfies the current requirement; consider parallel cables.");
            r.Summary = "No suitable section found.";
            return r;
        }
        var sectionForCurrent = chosen.Value;
        var iz0Chosen = CurrentCarryingCapacity.Lookup(i.Method, i.Insulation, loaded, sectionForCurrent)!.Value;
        r.Step("Section for current", "smallest S with Iz0(S) ≥ required", $"Iz0({F(sectionForCurrent, 1)} mm²) = {F(iz0Chosen, 1)} A", sectionForCurrent, "mm²", 1);

        // Voltage drop check; increase section if required.
        var section = sectionForCurrent;
        CalculationResult vd;
        while (true)
        {
            vd = VoltageDropCalculator.Calculate(new VoltageDropInput(i.System, i.NominalVoltage, i.DesignCurrentA, i.LengthM, section, i.Material, i.PowerFactor, LimitPercent: i.MaxVoltageDropPercent));
            if (vd.Output("Voltage drop Δu") <= i.MaxVoltageDropPercent) break;
            var next = ConductorData.StandardSections.FirstOrDefault(x => x > section);
            if (next == 0) break;
            section = next;
        }
        var dropPct = vd.Output("Voltage drop Δu");
        if (section > sectionForCurrent)
            r.Step("Section increased for voltage drop", "Δu(S) ≤ limit", $"Δu({F(section, 1)} mm²) = {F(dropPct, 2)} %", section, "mm²", 1);
        else
            r.Step("Voltage drop with selected section", "Δu = 100·u/U0", $"S = {F(section, 1)} mm²", dropPct, "%", 2);

        var iz = CurrentCarryingCapacity.Lookup(i.Method, i.Insulation, loaded, section)!.Value * k1 * k2;
        r.Step("Corrected capacity of selected section", "Iz = Iz0·k1·k2", $"{F(CurrentCarryingCapacity.Lookup(i.Method, i.Insulation, loaded, section)!.Value, 1)}·{F(k1)}·{F(k2, 2)}", iz, "A", 1);

        r.Out("Selected section", section, "mm²", 1).Out("Protective device rating In", inRating, "A", 0).Out("Corrected capacity Iz", iz, "A", 1).Out("Voltage drop", dropPct, "%", 2);
        r.Check(i.DesignCurrentA <= inRating && inRating <= iz, $"Ib ({F(i.DesignCurrentA, 1)} A) ≤ In ({F(inRating, 0)} A) ≤ Iz ({F(iz, 1)} A)");
        r.Check(i2Factor * inRating <= 1.45 * iz + 1e-9, $"I2 = {F(i2Factor, 2)}·In = {F(i2Factor * inRating, 1)} A ≤ 1.45·Iz = {F(1.45 * iz, 1)} A");
        r.Check(dropPct <= i.MaxVoltageDropPercent, $"Voltage drop {F(dropPct, 2)} % ≤ {F(i.MaxVoltageDropPercent, 1)} %");
        r.Assumptions.Add("Copper conductors, ambient air (not buried), continuous load.");
        r.Assumptions.Add(i.Device == ProtectiveDevice.CircuitBreaker
            ? "Circuit breaker conventional tripping current I2 = 1.45·In (IEC 60898 / IEC 60947-2 typical)."
            : "gG fuse conventional fusing current I2 = 1.6·In (In ≥ 16 A) or 1.9·In (In < 16 A) (IEC 60269).");
        r.Assumptions.Add("Harmonic currents in the neutral are not considered.");
        r.Assumptions.Add("Short-circuit thermal withstand (k²S² ≥ I²t) is not checked here; verify with the device let-through energy.");
        r.Warnings.Add("Current-carrying capacities are transcribed reference values; verify against the licensed edition of IEC 60364-5-52.");
        r.Summary = $"{F(section, 1)} mm² Cu, In = {F(inRating, 0)} A, Iz = {F(iz, 1)} A, Δu = {F(dropPct, 2)} %";
        return r;
    }
}

public sealed record ShortCircuitInput(
    double NetworkShortCircuitPowerMva,
    double TransformerRatingKva,
    double TransformerUkPercent,
    double TransformerLoadLossKw,
    double LowVoltage,
    double CableLengthM,
    double CableSectionMm2,
    Conductor Material = Conductor.Copper,
    int ParallelConductors = 1,
    double VoltageFactorCmax = 1.05);

public static class ShortCircuitCalculator
{
    /// <summary>
    /// Maximum three-phase short-circuit current (IEC 60909-0 equivalent voltage source method, simplified):
    /// network feeder (RQ = 0.1·XQ), transformer with correction factor KT, then a cable feeder.
    /// </summary>
    public static CalculationResult Calculate(ShortCircuitInput i)
    {
        PowerCalculator.Validate(i.NetworkShortCircuitPowerMva > 0 && i.TransformerRatingKva > 0 && i.TransformerUkPercent > 0 && i.LowVoltage > 0, "Network, transformer and voltage data must be positive.");
        PowerCalculator.Validate(i.TransformerLoadLossKw >= 0 && i.CableLengthM >= 0 && i.CableSectionMm2 > 0, "Invalid losses, length or section.");
        var c = i.VoltageFactorCmax;
        var un = i.LowVoltage;
        var r = new CalculationResult
        {
            CalculationType = "short-circuit",
            Title = "Three-phase short-circuit current (maximum)",
            StandardProfile = "IEC 60909-0 (simplified, radial LV network)",
            Method = "Equivalent voltage source c·Un/√3 at the fault location",
        };
        r.Input("Network S\"kQ", i.NetworkShortCircuitPowerMva, "MVA", 0).Input("Transformer SrT", i.TransformerRatingKva, "kVA", 0)
            .Input("Transformer ukr", i.TransformerUkPercent, "%", 2).Input("Transformer PkrT", i.TransformerLoadLossKw, "kW", 2)
            .Input("Low voltage Un", un, "V", 0).Input("Cable length", i.CableLengthM, "m", 1).Input("Cable section", i.CableSectionMm2, "mm²", 1)
            .Input("Parallel conductors", i.ParallelConductors, "", 0).Input("Voltage factor cmax", c, "", 2);

        // Network feeder referred to LV side (mΩ)
        var zq = c * un * un / (i.NetworkShortCircuitPowerMva * 1e6) * 1000;
        var xq = 0.995 * zq;
        var rq = 0.1 * xq;
        r.Step("Network impedance (LV side)", "ZQ = c·Un²/S\"kQ ; XQ = 0.995·ZQ ; RQ = 0.1·XQ", $"{F(c, 2)}·{F(un, 0)}²/{F(i.NetworkShortCircuitPowerMva, 0)} MVA", zq, "mΩ", 3);

        // Transformer
        var sr = i.TransformerRatingKva * 1000;
        var zt = i.TransformerUkPercent / 100 * un * un / sr * 1000;
        var rt = i.TransformerLoadLossKw * 1000 * un * un / (sr * sr) * 1000;
        var xt = Math.Sqrt(Math.Max(0, zt * zt - rt * rt));
        var xtPu = xt / (un * un / sr * 1000);
        var kt = 0.95 * c / (1 + 0.6 * xtPu);
        r.Step("Transformer impedance", "ZT = ukr/100·UrT²/SrT ; RT = PkrT·UrT²/SrT² ; XT = √(ZT² − RT²)", $"{F(i.TransformerUkPercent, 2)} %·{F(un, 0)}²/{F(i.TransformerRatingKva, 0)} kVA", zt, "mΩ", 3);
        r.Step("Transformer correction factor", "KT = 0.95·cmax/(1 + 0.6·xT)", $"0.95·{F(c, 2)}/(1 + 0.6·{F(xtPu, 4)})", kt, "", 4);
        rt *= kt; xt *= kt;

        var r1 = rq + rt; var x1 = xq + xt;
        var z1 = Math.Sqrt(r1 * r1 + x1 * x1);
        var ik1 = c * un / (Math.Sqrt(3) * z1 / 1000) / 1000;
        r.Step("Short-circuit current at transformer LV terminals", "I\"k3 = c·Un/(√3·|ZQ + KT·ZT|)", $"{F(c, 2)}·{F(un, 0)}/(√3·{F(z1, 3)} mΩ)", ik1, "kA", 2);

        // Cable (conductor at 20 °C for maximum current)
        var rl = ConductorData.Rho20(i.Material) * i.CableLengthM / (i.CableSectionMm2 * i.ParallelConductors) * 1000;
        var xl = ConductorData.ReactancePerMetre * i.CableLengthM / i.ParallelConductors * 1000;
        r.Step("Cable impedance (20 °C)", "RL = ρ20·L/(n·S) ; XL = 0.08 mΩ/m·L/n", $"{F(ConductorData.Rho20(i.Material), 6)}·{F(i.CableLengthM, 1)}/({i.ParallelConductors}·{F(i.CableSectionMm2, 1)})", rl, "mΩ", 3);
        var r2 = r1 + rl; var x2 = x1 + xl;
        var z2 = Math.Sqrt(r2 * r2 + x2 * x2);
        var ik2 = c * un / (Math.Sqrt(3) * z2 / 1000) / 1000;
        r.Step("Short-circuit current at cable end", "I\"k3 = c·Un/(√3·|Zk|)", $"{F(c, 2)}·{F(un, 0)}/(√3·{F(z2, 3)} mΩ)", ik2, "kA", 2);

        static double Kappa(double rr, double xx) => xx <= 0 ? 2 : 1.02 + 0.98 * Math.Exp(-3 * rr / xx);
        var k1 = Kappa(r1, x1); var k2 = Kappa(r2, x2);
        r.Step("Peak factor at transformer terminals", "κ = 1.02 + 0.98·e^(−3R/X)", $"R/X = {F(r1 / x1, 3)}", k1, "", 3);
        r.Out("I\"k3 at transformer LV terminals", ik1, "kA", 2).Out("ip at transformer LV terminals", k1 * Math.Sqrt(2) * ik1, "kA", 2)
            .Out("I\"k3 at cable end", ik2, "kA", 2).Out("ip at cable end", k2 * Math.Sqrt(2) * ik2, "kA", 2)
            .Out("Fault loop R/X at cable end", r2 / x2, "", 3);
        r.Status = ResultStatus.Informational;
        r.Assumptions.Add("Far-from-generator short circuit; motor contributions are neglected.");
        r.Assumptions.Add("Transformer rated LV voltage equals Un; tap changer at main position.");
        r.Assumptions.Add("Cable conductors at 20 °C (maximum current). Minimum currents and line-to-earth faults are not calculated.");
        r.Warnings.Add("Simplified estimation for preliminary design; a full IEC 60909 study is required for final equipment ratings.");
        r.Summary = $"I\"k3 = {F(ik1, 2)} kA at transformer, {F(ik2, 2)} kA at cable end";
        return r;
    }
}

public static class TransformerSizingCalculator
{
    public static CalculationResult Calculate(double connectedLoadKw, double demandFactor, double powerFactor, double reservePercent)
    {
        PowerCalculator.Validate(connectedLoadKw > 0, "Connected load must be positive.");
        PowerCalculator.Validate(demandFactor is > 0 and <= 1 && powerFactor is > 0 and <= 1 && reservePercent >= 0, "Invalid factors.");
        var pd = connectedLoadKw * demandFactor;
        var s = pd / powerFactor;
        var req = s * (1 + reservePercent / 100);
        var rating = ConductorData.StandardTransformerKva.FirstOrDefault(x => x >= req);
        var r = new CalculationResult { CalculationType = "transformer-sizing", Title = "Transformer sizing", StandardProfile = "Load-based sizing (IEC 60076 standard ratings)" };
        r.Input("Connected load", connectedLoadKw, "kW", 1).Input("Demand factor ks", demandFactor, "", 2).Input("Power factor", powerFactor, "", 2).Input("Reserve", reservePercent, "%", 0);
        r.Step("Maximum demand", "Pd = ΣP·ks", $"{F(connectedLoadKw, 1)}·{F(demandFactor, 2)}", pd, "kW", 1);
        r.Step("Apparent power", "S = Pd / cos φ", $"{F(pd, 1)}/{F(powerFactor, 2)}", s, "kVA", 1);
        r.Step("Required rating with reserve", "Sreq = S·(1 + reserve)", $"{F(s, 1)}·(1 + {F(reservePercent / 100, 2)})", req, "kVA", 1);
        if (rating == 0)
        {
            r.Check(false, "Required power exceeds the largest standard rating (3150 kVA); consider parallel transformers.");
            return r;
        }
        r.Out("Required apparent power", req, "kVA", 1).Out("Selected standard rating", rating, "kVA", 0).Out("Loading at maximum demand", s / rating * 100, "%", 1);
        r.Check(s / rating <= 1, $"Loading {F(s / rating * 100, 1)} % ≤ 100 %");
        r.Assumptions.Add("Ambient conditions within the transformer rating; no harmonic derating.");
        r.Summary = $"Select {F(rating, 0)} kVA (loading {F(s / rating * 100, 1)} %)";
        return r;
    }
}

public enum StartingMethod { DirectOnLine, StarDelta, SoftStarter, VariableFrequencyDrive }

public static class MotorStartingCalculator
{
    /// <summary>Rated and starting current for a three-phase motor and the starting method.</summary>
    public static CalculationResult Calculate(double powerKw, double voltage, double powerFactor, double efficiency, double lockedRotorRatio,
        StartingMethod method, double softStarterLimit = 3.5, double? transformerKva = null, double? transformerUk = null)
    {
        PowerCalculator.Validate(powerKw > 0 && voltage > 0, "Power and voltage must be positive.");
        PowerCalculator.Validate(powerFactor is > 0 and <= 1 && efficiency is > 0 and <= 1 && lockedRotorRatio > 1, "Invalid motor data.");
        var inA = powerKw * 1000 / (Math.Sqrt(3) * voltage * powerFactor * efficiency);
        var ratio = method switch
        {
            StartingMethod.DirectOnLine => lockedRotorRatio,
            StartingMethod.StarDelta => lockedRotorRatio / 3,
            StartingMethod.SoftStarter => Math.Min(softStarterLimit, lockedRotorRatio),
            _ => 1.5,
        };
        var ist = inA * ratio;
        var sStart = Math.Sqrt(3) * voltage * ist / 1000;
        var r = new CalculationResult { CalculationType = "motor-starting", Title = "Motor starting current", StandardProfile = "Electrical theory / IEC 60034 motor data" };
        r.Input("Rated power", powerKw, "kW", 2).Input("Voltage", voltage, "V", 0).Input("Power factor", powerFactor, "", 2).Input("Efficiency", efficiency, "", 3)
            .Input("Locked-rotor ratio Ist/In (DOL)", lockedRotorRatio, "", 1);
        r.Step("Rated current", "In = P/(√3·U·cos φ·η)", $"{F(powerKw * 1000, 0)}/(√3·{F(voltage, 0)}·{F(powerFactor, 2)}·{F(efficiency, 3)})", inA, "A", 2);
        var methodText = method switch
        {
            StartingMethod.DirectOnLine => "Ist = (Ist/In)·In",
            StartingMethod.StarDelta => "Ist ≈ (Ist/In)/3·In (star connection)",
            StartingMethod.SoftStarter => "Ist = current limit setting·In",
            _ => "Ist ≈ 1.5·In (VFD current limit)",
        };
        r.Step($"Starting current ({method})", methodText, $"{F(ratio, 2)}·{F(inA, 2)}", ist, "A", 1);
        r.Step("Starting apparent power", "Sst = √3·U·Ist", $"√3·{F(voltage, 0)}·{F(ist, 1)}", sStart, "kVA", 1);
        r.Out("Rated current In", inA, "A", 2).Out("Starting current Ist", ist, "A", 1).Out("Starting apparent power", sStart, "kVA", 1);
        if (transformerKva is { } st && transformerUk is { } uk && st > 0 && uk > 0)
        {
            var ssc = st / (uk / 100);
            var dip = sStart / (ssc + sStart) * 100;
            r.Step("Approximate voltage dip at transformer", "ΔU ≈ Sst/(Ssc + Sst), Ssc = SrT/uk", $"{F(sStart, 1)}/({F(ssc, 0)} + {F(sStart, 1)})", dip, "%", 1);
            r.Out("Voltage dip (approx.)", dip, "%", 1);
            r.Check(dip <= 15, $"Voltage dip {F(dip, 1)} % ≤ 15 % (typical acceptance limit)", failIsWarning: true);
        }
        else r.Status = ResultStatus.Informational;
        r.Assumptions.Add("Locked-rotor ratio from the motor datasheet (typical 6–8 for IE3 motors).");
        r.Assumptions.Add("Upstream impedance other than the transformer is neglected for the voltage dip estimate.");
        r.Summary = $"In = {F(inA, 1)} A, Ist = {F(ist, 0)} A ({method})";
        return r;
    }
}

public enum TripCurve { B, C, D }

public static class ProtectionCalculator
{
    /// <summary>Selects a circuit-breaker rating with Ib ≤ In ≤ Iz and checks breaking capacity.</summary>
    public static CalculationResult SelectBreaker(double designCurrentA, double cableCapacityIzA, double? prospectiveShortCircuitKa = null, double? breakingCapacityKa = null, bool highInrush = false)
    {
        PowerCalculator.Validate(designCurrentA > 0 && cableCapacityIzA > 0, "Currents must be positive.");
        var rating = ConductorData.StandardRatings.FirstOrDefault(x => x >= designCurrentA);
        var r = new CalculationResult { CalculationType = "protection", Title = "Protective device selection", StandardProfile = "IEC 60364-4-43 433.1 / IEC 60898-1" };
        r.Input("Design current Ib", designCurrentA, "A").Input("Cable capacity Iz", cableCapacityIzA, "A");
        r.Step("Rating", "In = smallest standard rating ≥ Ib", $"Ib = {F(designCurrentA, 2)} A", rating, "A", 0);
        r.Out("Selected rating In", rating, "A", 0);
        r.Check(designCurrentA <= rating && rating <= cableCapacityIzA, $"Ib ({F(designCurrentA, 1)}) ≤ In ({F(rating, 0)}) ≤ Iz ({F(cableCapacityIzA, 1)})");
        r.Check(1.45 * rating <= 1.45 * cableCapacityIzA, "I2 = 1.45·In ≤ 1.45·Iz");
        if (prospectiveShortCircuitKa is { } ik)
        {
            r.Input("Prospective I\"k", ik, "kA");
            if (breakingCapacityKa is { } icu)
            {
                r.Input("Breaking capacity Icu", icu, "kA");
                r.Check(icu >= ik, $"Icu ({F(icu, 1)} kA) ≥ I\"k ({F(ik, 2)} kA)");
            }
            else r.Warn("Breaking capacity not given: select a device with Icu ≥ the prospective short-circuit current.");
        }
        var curve = highInrush ? TripCurve.D : TripCurve.C;
        r.Assumptions.Add($"Suggested trip curve: {curve} ({(highInrush ? "high inrush loads: transformers, large motors" : "general loads with moderate inrush")}). Curve B suits resistive loads and long cables.");
        r.Summary = $"In = {F(rating, 0)} A, curve {curve}";
        return r;
    }

    /// <summary>
    /// TN system automatic disconnection: maximum circuit length by the conventional method,
    /// Lmax = 0.8·U0·Sph / (ρ·(1 + m)·Ia), m = Sph/SPE, ρ = 1.25·ρ20 (fault at operating temperature).
    /// </summary>
    public static CalculationResult TnMaximumLength(double u0, double phaseSectionMm2, double peSectionMm2, double magneticTripCurrentA, Conductor material = Conductor.Copper, double? actualLengthM = null)
    {
        PowerCalculator.Validate(u0 > 0 && phaseSectionMm2 > 0 && peSectionMm2 > 0 && magneticTripCurrentA > 0, "Inputs must be positive.");
        var rho = ConductorData.RhoService(material);
        var m = phaseSectionMm2 / peSectionMm2;
        var lmax = 0.8 * u0 * phaseSectionMm2 / (rho * (1 + m) * magneticTripCurrentA);
        var r = new CalculationResult { CalculationType = "tn-fault-loop", Title = "TN fault protection – maximum length", StandardProfile = "IEC 60364-4-41 411.4.4 (conventional method)" };
        r.Input("U0", u0, "V", 0).Input("Phase section Sph", phaseSectionMm2, "mm²", 1).Input("PE section SPE", peSectionMm2, "mm²", 1).Input("Magnetic trip current Ia", magneticTripCurrentA, "A", 0);
        r.Step("Section ratio", "m = Sph/SPE", $"{F(phaseSectionMm2, 1)}/{F(peSectionMm2, 1)}", m, "", 2);
        r.Step("Maximum length", "Lmax = 0.8·U0·Sph/(ρ·(1+m)·Ia)", $"0.8·{F(u0, 0)}·{F(phaseSectionMm2, 1)}/({F(rho, 4)}·{F(1 + m, 2)}·{F(magneticTripCurrentA, 0)})", lmax, "m", 1);
        r.Out("Maximum length Lmax", lmax, "m", 1);
        if (actualLengthM is { } len)
        {
            r.Input("Actual length", len, "m", 1);
            r.Check(len <= lmax, $"Actual length {F(len, 1)} m ≤ Lmax {F(lmax, 1)} m");
        }
        else r.Status = ResultStatus.Informational;
        r.Assumptions.Add("Voltage at the origin of the circuit during the fault ≥ 0.8·U0 (upstream impedance accounted for globally).");
        r.Assumptions.Add("Ia = instantaneous tripping current upper limit (e.g. 10·In for curve C, 20·In for curve D).");
        r.Summary = $"Lmax = {F(lmax, 1)} m";
        return r;
    }
}

public sealed record LoadItem(string Name, double PowerKw, double PowerFactor = 0.9, double DemandFactor = 1, string Phase = "3P", string Category = "Other");

public static class LoadCalculator
{
    /// <summary>Load schedule totals with demand factors and the resulting current.</summary>
    public static CalculationResult Summarize(IReadOnlyList<LoadItem> loads, double systemVoltage, double simultaneityFactor = 1)
    {
        PowerCalculator.Validate(systemVoltage > 0, "Voltage must be positive.");
        PowerCalculator.Validate(simultaneityFactor is > 0 and <= 1, "Simultaneity factor must be in (0, 1].");
        double p = 0, q = 0, installed = 0;
        foreach (var l in loads)
        {
            PowerCalculator.Validate(l.PowerKw >= 0 && l.PowerFactor is > 0 and <= 1 && l.DemandFactor is >= 0 and <= 1, $"Invalid data for load '{l.Name}'.");
            installed += l.PowerKw;
            var pl = l.PowerKw * l.DemandFactor;
            p += pl;
            q += pl * Math.Tan(Math.Acos(l.PowerFactor));
        }
        p *= simultaneityFactor; q *= simultaneityFactor;
        var s = Math.Sqrt(p * p + q * q);
        var i = s * 1000 / (Math.Sqrt(3) * systemVoltage);
        var r = new CalculationResult { CalculationType = "load-schedule", Title = "Load calculation", StandardProfile = "Demand factor method", Status = ResultStatus.Informational };
        r.Input("Number of loads", loads.Count, "", 0).Input("Installed power", installed, "kW", 2).Input("System voltage", systemVoltage, "V", 0).Input("Simultaneity factor ks", simultaneityFactor, "", 2);
        r.Step("Active demand", "P = ks·Σ(Pi·kui)", $"{F(simultaneityFactor, 2)}·Σ", p, "kW", 2);
        r.Step("Reactive demand", "Q = ks·Σ(Pi·kui·tan φi)", "", q, "kvar", 2);
        r.Step("Apparent demand", "S = √(P² + Q²)", $"√({F(p, 2)}² + {F(q, 2)}²)", s, "kVA", 2);
        r.Step("Current", "I = S/(√3·U)", $"{F(s * 1000, 0)}/(√3·{F(systemVoltage, 0)})", i, "A", 1);
        r.Out("Installed power", installed, "kW", 2).Out("Maximum demand P", p, "kW", 2).Out("Reactive power Q", q, "kvar", 2)
            .Out("Apparent power S", s, "kVA", 2).Out("Overall power factor", s > 0 ? p / s : 1, "", 3).Out("Design current", i, "A", 1);
        foreach (var g in loads.GroupBy(l => l.Category))
            r.Out($"Demand – {g.Key}", g.Sum(l => l.PowerKw * l.DemandFactor) * simultaneityFactor, "kW", 2);
        r.Assumptions.Add("Demand (utilisation) factors per load and a global simultaneity factor as entered.");
        r.Summary = $"P = {F(p, 1)} kW, S = {F(s, 1)} kVA, I = {F(i, 1)} A";
        return r;
    }
}

public sealed record PhaseAssignmentResult(string Load, string Phase, double PowerKw);

public static class LoadBalancingCalculator
{
    /// <summary>
    /// Assigns single-phase loads to L1/L2/L3 by the longest-processing-time heuristic (largest load to the least
    /// loaded phase). Fixed three-phase loads are spread equally.
    /// </summary>
    public static (CalculationResult result, IReadOnlyList<PhaseAssignmentResult> assignments) Balance(IReadOnlyList<LoadItem> loads, double phaseVoltage = 230)
    {
        var phase = new double[3];
        var assignments = new List<PhaseAssignmentResult>();
        foreach (var l in loads.Where(l => l.Phase == "3P"))
        {
            for (int k = 0; k < 3; k++) phase[k] += l.PowerKw * l.DemandFactor / 3;
            assignments.Add(new(l.Name, "L1-L2-L3", l.PowerKw));
        }
        foreach (var l in loads.Where(l => l.Phase != "3P").OrderByDescending(l => l.PowerKw * l.DemandFactor))
        {
            var k = Array.IndexOf(phase, phase.Min());
            phase[k] += l.PowerKw * l.DemandFactor;
            assignments.Add(new(l.Name, $"L{k + 1}", l.PowerKw));
        }
        var avg = phase.Average();
        var imbalance = avg > 0 ? (phase.Max() - avg) / avg * 100 : 0;
        var r = new CalculationResult { CalculationType = "load-balancing", Title = "Phase load balancing", StandardProfile = "Greedy balancing (LPT heuristic)" };
        r.Input("Loads", loads.Count, "", 0).Input("Phase voltage", phaseVoltage, "V", 0);
        for (int k = 0; k < 3; k++)
        {
            r.Out($"L{k + 1} load", phase[k], "kW", 2);
            r.Out($"L{k + 1} current (cos φ = 1)", phase[k] * 1000 / phaseVoltage, "A", 1);
        }
        r.Step("Imbalance", "(Pmax − Pavg)/Pavg", $"({F(phase.Max(), 2)} − {F(avg, 2)})/{F(avg, 2)}", imbalance, "%", 1);
        r.Out("Imbalance", imbalance, "%", 1);
        r.Check(imbalance <= 10, $"Imbalance {F(imbalance, 1)} % ≤ 10 % (design target)", failIsWarning: true);
        r.Assumptions.Add("Phase currents shown for unity power factor; the heuristic is near-optimal, not guaranteed optimal.");
        r.Summary = $"L1 {F(phase[0], 2)} kW / L2 {F(phase[1], 2)} kW / L3 {F(phase[2], 2)} kW, imbalance {F(imbalance, 1)} %";
        return (r, assignments);
    }
}
