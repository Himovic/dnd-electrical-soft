using static DND.Electrical.Calculations.CalculationResult;

namespace DND.Electrical.Calculations;

/// <summary>Power factor correction: required capacitor power and parallel-resonance check.</summary>
public static class PowerFactorCorrectionCalculator
{
    public static CalculationResult Calculate(double activeKw, double pfActual, double pfTarget, double voltage, double? shortCircuitMva = null)
    {
        PowerCalculator.Validate(activeKw > 0 && voltage > 0, "Active power and voltage must be positive.");
        PowerCalculator.Validate(pfActual is > 0 and <= 1 && pfTarget is > 0 and <= 1, "Power factors must be between 0 and 1.");
        PowerCalculator.Validate(pfTarget > pfActual, "The target power factor must be higher than the actual power factor.");
        var r = new CalculationResult { CalculationType = "pfc", Title = "Power factor correction", StandardProfile = "Power triangle; resonance estimate h = √(Sk/Qc)", Method = "Qc = P·(tan φ1 − tan φ2)" };
        r.Input("Active power P", activeKw, "kW", 1).Input("Actual cos φ1", pfActual, "", 3).Input("Target cos φ2", pfTarget, "", 3).Input("Voltage", voltage, "V", 0);
        double t1 = Math.Tan(Math.Acos(pfActual)), t2 = Math.Tan(Math.Acos(pfTarget));
        var qc = activeKw * (t1 - t2);
        r.Step("Reactive power to compensate", "Qc = P·(tan φ1 − tan φ2)", $"{F(activeKw, 1)}·({F(t1, 4)} − {F(t2, 4)})", qc, "kvar", 1);
        var ic = qc * 1000 / (Math.Sqrt(3) * voltage);
        r.Step("Capacitor bank current (three-phase)", "Ic = Qc/(√3·U)", $"{F(qc * 1000, 0)}/(√3·{F(voltage, 0)})", ic, "A", 1);
        double s1 = activeKw / pfActual, s2 = activeKw / pfTarget;
        r.Step("Apparent power before / after", "S = P/cos φ", $"{F(s1, 1)} kVA → {F(s2, 1)} kVA", s2, "kVA", 1);
        r.Out("Required capacitor power Qc", qc, "kvar", 1).Out("Capacitor current", ic, "A", 1).Out("Apparent power after correction", s2, "kVA", 1)
            .Out("Current reduction", (1 - s2 / s1) * 100, "%", 1);
        if (shortCircuitMva is { } sk and > 0)
        {
            r.Input("Short-circuit power at the bank", sk, "MVA", 1);
            var h = Math.Sqrt(sk * 1000 / qc);
            r.Step("Parallel resonance order with the network", "h = √(Sk/Qc)", $"√({F(sk * 1000, 0)}/{F(qc, 1)})", h, "", 2);
            r.Out("Resonance harmonic order", h, "", 2);
            var near = new[] { 3, 5, 7, 11, 13 }.Where(n => Math.Abs(h - n) / n < 0.1).ToList();
            r.Check(near.Count == 0, near.Count == 0 ? $"Resonance order {F(h, 2)} is not within ±10 % of a characteristic harmonic" : $"Resonance order {F(h, 2)} is close to harmonic {string.Join("/", near)} – a detuned (reactor) bank should be studied", failIsWarning: true);
            r.Assumptions.Add("Resonance estimate neglects loads and network damping; a harmonic study is required where non-linear loads are significant.");
        }
        r.Assumptions.Add("Balanced three-phase load; capacitor rated at the operating voltage.");
        r.Summary = $"Qc = {F(qc, 1)} kvar to raise cos φ from {F(pfActual, 2)} to {F(pfTarget, 2)}";
        if (r.Status == ResultStatus.Informational) r.Status = ResultStatus.Pass;
        return r;
    }
}

/// <summary>Preliminary generator sizing from running load and the largest motor start (transient voltage dip).</summary>
public static class GeneratorSizingCalculator
{
    public static CalculationResult Calculate(double runningKw, double runningPf, double maxLoadingPercent, double largestMotorKw, double motorStartKvaPerKw, double subtransientReactancePu, double allowedDipPercent)
    {
        PowerCalculator.Validate(runningKw > 0 && runningPf is > 0 and <= 1 && maxLoadingPercent is > 0 and <= 100, "Invalid running load data.");
        PowerCalculator.Validate(largestMotorKw >= 0 && motorStartKvaPerKw >= 0 && subtransientReactancePu > 0 && allowedDipPercent is > 0 and < 100, "Invalid motor starting data.");
        var r = new CalculationResult { CalculationType = "gen", Title = "Generator sizing (preliminary)", StandardProfile = "Running load and transient voltage dip estimate", Method = "ΔU = xd''·Sst/(Sg + xd''·Sst)" };
        r.Input("Running load", runningKw, "kW", 1).Input("Running cos φ", runningPf, "", 2).Input("Maximum continuous loading", maxLoadingPercent, "%", 0)
            .Input("Largest motor", largestMotorKw, "kW", 1).Input("Motor starting kVA per kW", motorStartKvaPerKw, "kVA/kW", 2)
            .Input("Generator xd'' (manufacturer)", subtransientReactancePu, "pu", 3).Input("Allowed transient dip", allowedDipPercent, "%", 0);
        var sRun = runningKw / runningPf;
        var sByLoad = sRun / (maxLoadingPercent / 100);
        r.Step("Running apparent power", "S = P/cos φ", $"{F(runningKw, 1)}/{F(runningPf, 2)}", sRun, "kVA", 1);
        r.Step("Rating for continuous loading", "Sg ≥ S/loading", $"{F(sRun, 1)}/{F(maxLoadingPercent / 100, 2)}", sByLoad, "kVA", 1);
        var sst = largestMotorKw * motorStartKvaPerKw;
        var d = allowedDipPercent / 100;
        var sByDip = sst <= 0 ? 0 : subtransientReactancePu * sst * (1 / d - 1);
        r.Step("Largest motor starting kVA", "Sst = P·(kVA/kW)", $"{F(largestMotorKw, 1)}·{F(motorStartKvaPerKw, 2)}", sst, "kVA", 1);
        r.Step("Rating for the allowed voltage dip", "Sg ≥ xd''·Sst·(1/ΔU − 1)", $"{F(subtransientReactancePu, 3)}·{F(sst, 1)}·(1/{F(d, 3)} − 1)", sByDip, "kVA", 1);
        var sg = Math.Max(sByLoad, sByDip);
        var dip = sst <= 0 ? 0 : subtransientReactancePu * sst / (sg + subtransientReactancePu * sst) * 100;
        r.Out("Minimum generator rating", sg, "kVA", 0).Out("Governing criterion", sByDip > sByLoad ? 1 : 0, "1 = motor start", 0).Out("Estimated dip at that rating", dip, "%", 1);
        r.Assumptions.Add("Motor started with the running load already connected; dip estimate uses xd'' only (no AVR response, no prime-mover frequency dip).");
        r.Assumptions.Add("Starting kVA per kW depends on the motor and starting method (manufacturer data).");
        r.Warnings.Add("Preliminary estimate: confirm with the generator manufacturer's transient performance data (ISO 8528 performance class) and load step acceptance.");
        r.Summary = $"Generator ≥ {F(sg, 0)} kVA ({(sByDip > sByLoad ? "motor starting" : "running load")} governs)";
        r.Status = ResultStatus.Informational;
        return r;
    }
}

/// <summary>Earthing: rod resistance (Dwight), grid resistance (Sverak, IEEE Std 80) and tolerable touch/step voltages.</summary>
public static class EarthingCalculator
{
    public static CalculationResult Rod(double soilResistivity, double lengthM, double diameterMm, int rods = 1, double spacingM = 0)
    {
        PowerCalculator.Validate(soilResistivity > 0 && lengthM > 0 && diameterMm > 0 && rods >= 1, "Resistivity, length, diameter and number of rods must be positive.");
        var r = new CalculationResult { CalculationType = "earth-rod", Title = "Earth electrode resistance (vertical rod)", StandardProfile = "Dwight formula", Method = "R = ρ/(2πL)·(ln(4L/d) − 1)" };
        r.Input("Soil resistivity ρ", soilResistivity, "Ω·m", 0).Input("Rod length L", lengthM, "m", 2).Input("Rod diameter d", diameterMm, "mm", 1).Input("Number of rods", rods, "", 0);
        var d = diameterMm / 1000;
        var r1 = soilResistivity / (2 * Math.PI * lengthM) * (Math.Log(4 * lengthM / d) - 1);
        r.Step("Single rod resistance", "R1 = ρ/(2πL)·(ln(4L/d) − 1)", $"{F(soilResistivity, 0)}/(2π·{F(lengthM, 2)})·(ln(4·{F(lengthM, 2)}/{F(d, 4)}) − 1)", r1, "Ω", 2);
        r.Out("Single rod resistance", r1, "Ω", 2);
        if (rods > 1)
        {
            r.Input("Spacing", spacingM, "m", 2);
            if (spacingM >= 2 * lengthM)
            {
                // Rods far apart behave approximately in parallel; mutual resistance ρ/(2πs) per pair (hemispherical approximation).
                var rm = soilResistivity / (2 * Math.PI * spacingM);
                var rn = (r1 + (rods - 1) * rm) / rods;
                r.Step("Rods in parallel with mutual resistance", "Rn ≈ (R1 + (n−1)·ρ/(2πs))/n", $"({F(r1, 2)} + {rods - 1}·{F(rm, 2)})/{rods}", rn, "Ω", 2);
                r.Out("Combined resistance", rn, "Ω", 2);
                r.Assumptions.Add("Combined resistance uses an average mutual resistance (rods in line, spacing ≥ 2L); interaction is underestimated for compact arrangements.");
            }
            else r.Warn("Spacing below 2·L: rods interact strongly; use the grid method or a numerical study.");
        }
        r.Assumptions.Add("Homogeneous soil; resistivity from measurement (Wenner method) at the site.");
        r.Summary = $"R = {F(r1, 2)} Ω per rod";
        r.Status = ResultStatus.Informational;
        return r;
    }

    public static CalculationResult Grid(double soilResistivity, double areaM2, double totalBuriedLengthM, double depthM)
    {
        PowerCalculator.Validate(soilResistivity > 0 && areaM2 > 0 && totalBuriedLengthM > 0 && depthM >= 0, "Invalid grid data.");
        var r = new CalculationResult { CalculationType = "earth-grid", Title = "Earthing grid resistance", StandardProfile = "IEEE Std 80 – Sverak equation", Method = "Rg = ρ·[1/LT + 1/√(20A)·(1 + 1/(1 + h·√(20/A)))]" };
        r.Input("Soil resistivity ρ", soilResistivity, "Ω·m", 0).Input("Grid area A", areaM2, "m²", 0).Input("Total buried length LT", totalBuriedLengthM, "m", 0).Input("Burial depth h", depthM, "m", 2);
        var t1 = 1 / totalBuriedLengthM;
        var t2 = 1 / Math.Sqrt(20 * areaM2) * (1 + 1 / (1 + depthM * Math.Sqrt(20 / areaM2)));
        var rg = soilResistivity * (t1 + t2);
        r.Step("Grid resistance", "Rg = ρ·[1/LT + 1/√(20A)·(1 + 1/(1 + h√(20/A)))]", $"{F(soilResistivity, 0)}·[{F(t1, 6)} + {F(t2, 6)}]", rg, "Ω", 3);
        r.Out("Grid resistance Rg", rg, "Ω", 3);
        r.Assumptions.Add("Homogeneous soil; the equation applies to grids with or without rods at depths of about 0.25–2.5 m.");
        r.Summary = $"Rg = {F(rg, 2)} Ω";
        r.Status = ResultStatus.Informational;
        return r;
    }

    /// <summary>Tolerable touch and step voltages (IEEE Std 80, body weight 50 or 70 kg) with surface layer derating Cs.</summary>
    public static CalculationResult TolerableVoltages(double soilResistivity, double surfaceResistivity, double surfaceLayerM, double faultDurationS, double bodyKg = 70, double? gridPotentialRiseV = null)
    {
        PowerCalculator.Validate(soilResistivity > 0 && surfaceResistivity > 0 && surfaceLayerM >= 0 && faultDurationS > 0, "Invalid data.");
        PowerCalculator.Validate(bodyKg is 50 or 70, "Body weight must be 50 or 70 kg.");
        var r = new CalculationResult { CalculationType = "touch-step", Title = "Tolerable touch and step voltages", StandardProfile = $"IEEE Std 80 – {bodyKg:0} kg body weight", Method = "Etouch = (1000 + 1.5·Cs·ρs)·k/√ts ; Estep = (1000 + 6·Cs·ρs)·k/√ts" };
        r.Input("Soil resistivity ρ", soilResistivity, "Ω·m", 0).Input("Surface layer resistivity ρs", surfaceResistivity, "Ω·m", 0).Input("Surface layer thickness hs", surfaceLayerM, "m", 3)
            .Input("Fault duration ts", faultDurationS, "s", 2).Input("Body weight", bodyKg, "kg", 0);
        var cs = surfaceLayerM <= 0 ? 1 : 1 - 0.09 * (1 - soilResistivity / surfaceResistivity) / (2 * surfaceLayerM + 0.09);
        var ps = surfaceLayerM <= 0 ? soilResistivity : surfaceResistivity;
        r.Step("Surface layer derating factor", "Cs = 1 − 0.09·(1 − ρ/ρs)/(2hs + 0.09)", $"1 − 0.09·(1 − {F(soilResistivity, 0)}/{F(surfaceResistivity, 0)})/(2·{F(surfaceLayerM, 3)} + 0.09)", cs, "", 4);
        var k = bodyKg == 50 ? 0.116 : 0.157;
        var et = (1000 + 1.5 * cs * ps) * k / Math.Sqrt(faultDurationS);
        var es = (1000 + 6 * cs * ps) * k / Math.Sqrt(faultDurationS);
        r.Step("Tolerable touch voltage", $"Etouch = (1000 + 1.5·Cs·ρs)·{k}/√ts", $"(1000 + 1.5·{F(cs, 4)}·{F(ps, 0)})·{k}/√{F(faultDurationS, 2)}", et, "V", 1);
        r.Step("Tolerable step voltage", $"Estep = (1000 + 6·Cs·ρs)·{k}/√ts", $"(1000 + 6·{F(cs, 4)}·{F(ps, 0)})·{k}/√{F(faultDurationS, 2)}", es, "V", 1);
        r.Out("Surface layer factor Cs", cs, "", 3).Out("Tolerable touch voltage", et, "V", 1).Out("Tolerable step voltage", es, "V", 1);
        if (gridPotentialRiseV is { } gpr)
        {
            r.Input("Ground potential rise GPR", gpr, "V", 0);
            r.Check(gpr <= et, gpr <= et ? "GPR is below the tolerable touch voltage – no further touch voltage analysis needed" : "GPR exceeds the tolerable touch voltage – mesh and step voltages must be calculated (detailed grid design)", failIsWarning: true);
        }
        r.Summary = $"Etouch = {F(et, 0)} V, Estep = {F(es, 0)} V ({bodyKg:0} kg, {F(faultDurationS, 2)} s)";
        if (r.Status == ResultStatus.Informational) r.Status = ResultStatus.Pass;
        return r;
    }
}

/// <summary>Average illuminance by the lumen method (utilisation factor from the luminaire photometric data).</summary>
public static class LightingCalculator
{
    public static CalculationResult Calculate(double lengthM, double widthM, double mountingHeightM, double targetLux, double lumensPerLuminaire, double utilisationFactor, double maintenanceFactor)
    {
        PowerCalculator.Validate(lengthM > 0 && widthM > 0 && mountingHeightM > 0 && targetLux > 0 && lumensPerLuminaire > 0, "Room dimensions, target and luminous flux must be positive.");
        PowerCalculator.Validate(utilisationFactor is > 0 and <= 1 && maintenanceFactor is > 0 and <= 1, "UF and MF must be between 0 and 1.");
        var r = new CalculationResult { CalculationType = "lighting", Title = "Lighting – lumen method", StandardProfile = "Lumen method (average maintained illuminance)", Method = "N = E·A/(Φ·UF·MF)" };
        r.Input("Room length", lengthM, "m", 2).Input("Room width", widthM, "m", 2).Input("Mounting height above work plane Hm", mountingHeightM, "m", 2)
            .Input("Target maintained illuminance", targetLux, "lx", 0).Input("Luminous flux per luminaire", lumensPerLuminaire, "lm", 0)
            .Input("Utilisation factor UF (photometric data)", utilisationFactor, "", 2).Input("Maintenance factor MF", maintenanceFactor, "", 2);
        var a = lengthM * widthM;
        var k = a / (mountingHeightM * (lengthM + widthM));
        r.Step("Room index", "K = L·W/(Hm·(L + W))", $"{F(lengthM, 2)}·{F(widthM, 2)}/({F(mountingHeightM, 2)}·({F(lengthM, 2)} + {F(widthM, 2)}))", k, "", 2);
        var n = targetLux * a / (lumensPerLuminaire * utilisationFactor * maintenanceFactor);
        var nInt = (int)Math.Ceiling(n - 1e-9);
        r.Step("Number of luminaires", "N = E·A/(Φ·UF·MF)", $"{F(targetLux, 0)}·{F(a, 1)}/({F(lumensPerLuminaire, 0)}·{F(utilisationFactor, 2)}·{F(maintenanceFactor, 2)})", n, "", 2);
        var eAchieved = nInt * lumensPerLuminaire * utilisationFactor * maintenanceFactor / a;
        r.Out("Room index K", k, "", 2).Out("Luminaires required", nInt, "", 0).Out("Achieved maintained illuminance", eAchieved, "lx", 0);
        r.Assumptions.Add("UF must be taken from the luminaire manufacturer's utilisation table for room index K and the room reflectances.");
        r.Assumptions.Add("Average illuminance only; uniformity, glare (UGR) and emergency lighting require a point-by-point study.");
        r.Summary = $"{nInt} luminaires → {F(eAchieved, 0)} lx average maintained";
        r.Check(eAchieved >= targetLux, $"Achieved {F(eAchieved, 0)} lx ≥ target {F(targetLux, 0)} lx");
        return r;
    }
}
