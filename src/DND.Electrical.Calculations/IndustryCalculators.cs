using static DND.Electrical.Calculations.CalculationResult;

namespace DND.Electrical.Calculations;

public enum RedundancyScheme { N, NPlus1, NPlus2, TwoN, TwoNPlus1 }

/// <summary>Data centre power: UPS module redundancy, battery autonomy (energy method) and PUE.</summary>
public static class DataCenterCalculator
{
    public static int ModulesRequired(double loadKw, double moduleKw, RedundancyScheme scheme)
    {
        var n = (int)Math.Ceiling(loadKw / moduleKw - 1e-9);
        return scheme switch { RedundancyScheme.N => n, RedundancyScheme.NPlus1 => n + 1, RedundancyScheme.NPlus2 => n + 2, RedundancyScheme.TwoN => 2 * n, _ => 2 * n + 1 };
    }

    public static CalculationResult Redundancy(double criticalLoadKw, double moduleKw, RedundancyScheme scheme, int installedModules)
    {
        PowerCalculator.Validate(criticalLoadKw > 0 && moduleKw > 0 && installedModules >= 0, "Load and module rating must be positive.");
        var r = new CalculationResult { CalculationType = "dc-redundancy", Title = "UPS redundancy", StandardProfile = "Redundancy schemes N, N+1, N+2, 2N, 2N+1", Method = "N = ⌈P/Pmodule⌉" };
        r.Input("Critical IT load", criticalLoadKw, "kW", 1).Input("Module rating", moduleKw, "kW", 1).Input("Installed modules", installedModules, "", 0);
        var n = (int)Math.Ceiling(criticalLoadKw / moduleKw - 1e-9);
        var required = ModulesRequired(criticalLoadKw, moduleKw, scheme);
        r.Step("Modules needed to carry the load", "N = ⌈P/Pmodule⌉", $"⌈{F(criticalLoadKw, 1)}/{F(moduleKw, 1)}⌉", n, "", 0);
        r.Step($"Modules for {Label(scheme)}", Formula(scheme), $"N = {n}", required, "", 0);
        var perModule = installedModules > 0 ? criticalLoadKw / (installedModules * moduleKw) * 100 : 0;
        var afterOne = installedModules > 1 ? criticalLoadKw / ((installedModules - 1) * moduleKw) * 100 : double.PositiveInfinity;
        r.Out("Modules for capacity N", n, "", 0).Out("Modules required", required, "", 0).Out("Loading with all modules", perModule, "%", 1);
        if (!double.IsInfinity(afterOne)) r.Out("Loading after loss of one module", afterOne, "%", 1);
        r.Check(installedModules >= required, $"{installedModules} installed modules {(installedModules >= required ? "meet" : "do not meet")} {Label(scheme)} ({required} required)");
        r.Assumptions.Add("Modules share the load equally; 2N counts two independent systems each able to carry the full load.");
        r.Summary = $"{Label(scheme)}: {required} × {F(moduleKw, 0)} kW modules required, {installedModules} installed";
        return r;
    }

    /// <summary>Battery autonomy from usable stored energy: t = E·DoD·ηinv·ηageing / P.</summary>
    public static CalculationResult Autonomy(double loadKw, double batteryKwh, double depthOfDischarge, double inverterEfficiency, double ageingFactor, double requiredMinutes)
    {
        PowerCalculator.Validate(loadKw > 0 && batteryKwh > 0, "Load and battery energy must be positive.");
        PowerCalculator.Validate(depthOfDischarge is > 0 and <= 1 && inverterEfficiency is > 0 and <= 1 && ageingFactor is > 0 and <= 1, "Factors must be between 0 and 1.");
        var r = new CalculationResult { CalculationType = "dc-autonomy", Title = "UPS battery autonomy (energy method)", StandardProfile = "Energy balance (preliminary)", Method = "t = E·DoD·η·k_ageing/P" };
        r.Input("Load on UPS", loadKw, "kW", 1).Input("Nominal battery energy", batteryKwh, "kWh", 1).Input("Usable depth of discharge", depthOfDischarge, "", 2)
            .Input("Inverter efficiency", inverterEfficiency, "", 3).Input("Ageing / end-of-life factor", ageingFactor, "", 2).Input("Required autonomy", requiredMinutes, "min", 0);
        var usable = batteryKwh * depthOfDischarge * inverterEfficiency * ageingFactor;
        var minutes = usable / loadKw * 60;
        r.Step("Usable energy", "Eu = E·DoD·η·k", $"{F(batteryKwh, 1)}·{F(depthOfDischarge, 2)}·{F(inverterEfficiency, 3)}·{F(ageingFactor, 2)}", usable, "kWh", 2);
        r.Step("Autonomy", "t = Eu/P·60", $"{F(usable, 2)}/{F(loadKw, 1)}·60", minutes, "min", 1);
        var required = requiredMinutes * loadKw / 60 / (depthOfDischarge * inverterEfficiency * ageingFactor);
        r.Out("Autonomy", minutes, "min", 1).Out("Battery energy for required autonomy", required, "kWh", 1);
        r.Check(minutes >= requiredMinutes, $"Autonomy {F(minutes, 1)} min {(minutes >= requiredMinutes ? "≥" : "<")} required {F(requiredMinutes, 0)} min");
        r.Assumptions.Add("Energy method ignores the discharge-rate dependence of battery capacity (Peukert effect, high rates in short autonomies); final sizing must use the battery manufacturer's constant-power discharge tables.");
        r.Summary = $"Autonomy ≈ {F(minutes, 1)} min at {F(loadKw, 0)} kW";
        return r;
    }

    public static CalculationResult Pue(double totalFacilityKwh, double itEquipmentKwh)
    {
        PowerCalculator.Validate(totalFacilityKwh > 0 && itEquipmentKwh > 0 && totalFacilityKwh >= itEquipmentKwh, "Facility energy must be ≥ IT energy > 0.");
        var r = new CalculationResult { CalculationType = "dc-pue", Title = "Power usage effectiveness", StandardProfile = "PUE = total facility energy / IT equipment energy", Method = "PUE = Efacility/EIT" };
        r.Input("Total facility energy", totalFacilityKwh, "kWh", 0).Input("IT equipment energy", itEquipmentKwh, "kWh", 0);
        var pue = totalFacilityKwh / itEquipmentKwh;
        r.Step("PUE", "PUE = Efacility/EIT", $"{F(totalFacilityKwh, 0)}/{F(itEquipmentKwh, 0)}", pue, "", 3);
        r.Out("PUE", pue, "", 3).Out("DCiE", 100 / pue, "%", 1).Out("Non-IT energy", totalFacilityKwh - itEquipmentKwh, "kWh", 0);
        r.Assumptions.Add("Energies over the same period and measured at the defined measurement points.");
        r.Summary = $"PUE = {F(pue, 2)}";
        r.Status = ResultStatus.Informational;
        return r;
    }

    private static string Label(RedundancyScheme s) => s switch { RedundancyScheme.N => "N", RedundancyScheme.NPlus1 => "N+1", RedundancyScheme.NPlus2 => "N+2", RedundancyScheme.TwoN => "2N", _ => "2N+1" };
    private static string Formula(RedundancyScheme s) => s switch { RedundancyScheme.N => "N", RedundancyScheme.NPlus1 => "N + 1", RedundancyScheme.NPlus2 => "N + 2", RedundancyScheme.TwoN => "2·N", _ => "2·N + 1" };
}

/// <summary>PV string sizing against inverter MPPT/voltage/current limits using module datasheet temperature coefficients.</summary>
public static class PvStringCalculator
{
    public static CalculationResult Calculate(double vocStc, double vmpStc, double iscStc, double betaVocPercentPerK, double betaVmpPercentPerK,
        double tMinC, double tMaxCellC, double inverterVmax, double mpptVmin, double mpptVmax, double inverterImaxPerMppt, int modulesPerString, int stringsPerMppt)
    {
        PowerCalculator.Validate(vocStc > 0 && vmpStc > 0 && iscStc > 0 && inverterVmax > 0 && mpptVmin > 0 && mpptVmax > mpptVmin && inverterImaxPerMppt > 0, "Module and inverter data must be positive.");
        PowerCalculator.Validate(modulesPerString >= 1 && stringsPerMppt >= 1, "At least one module per string and one string.");
        var r = new CalculationResult { CalculationType = "pv-string", Title = "PV string sizing", StandardProfile = "Datasheet temperature coefficients (STC 25 °C)", Method = "V(T) = V_STC·(1 + β·(T − 25))" };
        r.Input("Voc (STC)", vocStc, "V", 2).Input("Vmp (STC)", vmpStc, "V", 2).Input("Isc (STC)", iscStc, "A", 2).Input("β Voc", betaVocPercentPerK, "%/K", 3).Input("β Vmp", betaVmpPercentPerK, "%/K", 3)
            .Input("Minimum ambient/cell temperature", tMinC, "°C", 0).Input("Maximum cell temperature", tMaxCellC, "°C", 0).Input("Inverter max DC voltage", inverterVmax, "V", 0)
            .Input("MPPT range min", mpptVmin, "V", 0).Input("MPPT range max", mpptVmax, "V", 0).Input("Max input current per MPPT", inverterImaxPerMppt, "A", 1)
            .Input("Modules per string", modulesPerString, "", 0).Input("Strings per MPPT", stringsPerMppt, "", 0);
        var vocMax = vocStc * (1 + betaVocPercentPerK / 100 * (tMinC - 25));
        var vmpMin = vmpStc * (1 + betaVmpPercentPerK / 100 * (tMaxCellC - 25));
        var vmpCold = vmpStc * (1 + betaVmpPercentPerK / 100 * (tMinC - 25));
        r.Step("Module Voc at minimum temperature", "Voc(Tmin) = Voc·(1 + βVoc·(Tmin − 25))", $"{F(vocStc, 2)}·(1 + {F(betaVocPercentPerK / 100, 5)}·({F(tMinC, 0)} − 25))", vocMax, "V", 2);
        r.Step("Module Vmp at maximum cell temperature", "Vmp(Tmax) = Vmp·(1 + βVmp·(Tmax − 25))", $"{F(vmpStc, 2)}·(1 + {F(betaVmpPercentPerK / 100, 5)}·({F(tMaxCellC, 0)} − 25))", vmpMin, "V", 2);
        var nMax = (int)Math.Floor(Math.Min(inverterVmax / vocMax, mpptVmax / vmpCold) + 1e-9);
        var nMin = (int)Math.Ceiling(mpptVmin / vmpMin - 1e-9);
        r.Step("Maximum modules per string", "Nmax = ⌊min(Vmax,inv/Voc(Tmin), VMPPT,max/Vmp(Tmin))⌋", "", nMax, "", 0);
        r.Step("Minimum modules per string", "Nmin = ⌈VMPPT,min/Vmp(Tmax)⌉", $"⌈{F(mpptVmin, 0)}/{F(vmpMin, 2)}⌉", nMin, "", 0);
        var stringVoc = vocMax * modulesPerString;
        var current = iscStc * stringsPerMppt;
        r.Out("Max modules per string", nMax, "", 0).Out("Min modules per string", nMin, "", 0).Out("String Voc at Tmin", stringVoc, "V", 1)
            .Out("String Vmp at Tmax", vmpMin * modulesPerString, "V", 1).Out("MPPT input current (Isc)", current, "A", 2);
        r.Check(stringVoc <= inverterVmax, $"String Voc {F(stringVoc, 1)} V ≤ inverter maximum {F(inverterVmax, 0)} V");
        r.Check(vmpMin * modulesPerString >= mpptVmin, $"String Vmp at high temperature {F(vmpMin * modulesPerString, 1)} V ≥ MPPT minimum {F(mpptVmin, 0)} V");
        r.Check(vmpCold * modulesPerString <= mpptVmax, $"String Vmp at low temperature {F(vmpCold * modulesPerString, 1)} V ≤ MPPT maximum {F(mpptVmax, 0)} V", failIsWarning: true);
        r.Check(current <= inverterImaxPerMppt, $"Isc of {stringsPerMppt} string(s) {F(current, 2)} A ≤ {F(inverterImaxPerMppt, 1)} A per MPPT");
        r.Assumptions.Add("Isc check uses STC values; irradiance above 1000 W/m² and the Isc temperature coefficient increase the current – apply the safety factors of the applicable installation standard.");
        r.Summary = $"{modulesPerString} modules/string allowed range {nMin}…{nMax}; string Voc {F(stringVoc, 0)} V";
        return r;
    }
}
