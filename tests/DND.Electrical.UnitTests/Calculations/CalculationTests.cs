using DND.Electrical.Calculations;

namespace DND.Electrical.UnitTests.Calculations;

/// <summary>Reference values computed by hand from the documented formulas.</summary>
public class CalculationTests
{
    [Fact]
    public void Three_phase_power_from_current()
    {
        var r = PowerCalculator.PowerFromCurrent(SupplySystem.ThreePhase, 400, 10, 0.8);
        Assert.Equal(6.928, r.Output("Apparent power S"), 3);
        Assert.Equal(5.543, r.Output("Active power P"), 3);
        Assert.Equal(4.157, r.Output("Reactive power Q"), 3);
        Assert.NotEmpty(r.Steps);
    }

    [Fact]
    public void Single_phase_power()
    {
        var r = PowerCalculator.PowerFromCurrent(SupplySystem.SinglePhase, 230, 10, 1);
        Assert.Equal(2.3, r.Output("Active power P"), 6);
        Assert.Equal(0, r.Output("Reactive power Q"), 6);
    }

    [Fact]
    public void Current_from_power_three_phase_motor()
    {
        var r = PowerCalculator.CurrentFromPower(SupplySystem.ThreePhase, 11, 400, 0.85, 0.9);
        Assert.Equal(20.754, r.Output("Current I"), 2);
    }

    [Fact]
    public void Power_triangle() =>
        Assert.Equal(0.8, PowerCalculator.PowerTriangle(8, 6).Output("Power factor cos φ"), 6);

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => PowerCalculator.CurrentFromPower(SupplySystem.ThreePhase, 11, 0, 0.85));
        Assert.Throws<ArgumentException>(() => PowerCalculator.PowerFromCurrent(SupplySystem.ThreePhase, 400, 10, 1.2));
    }

    [Fact]
    public void Voltage_drop_iec_60364_annex_g()
    {
        var r = VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.ThreePhase, 400, 20, 50, 4, PowerFactor: 0.85));
        Assert.Equal(4.823, r.Output("Voltage drop u"), 2);
        Assert.Equal(2.089, r.Output("Voltage drop Δu"), 2);
        Assert.Equal(ResultStatus.Pass, r.Status);
        Assert.Contains("Preliminary engineering calculation", r.ToReportText());
    }

    [Fact]
    public void Voltage_drop_single_phase_uses_b_equals_2()
    {
        var r = VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.SinglePhase, 230, 10, 30, 2.5, PowerFactor: 1, LimitPercent: 3));
        // u = 2·0.0225·30/2.5·10 = 5.4 V → 2.348 %
        Assert.Equal(5.4, r.Output("Voltage drop u"), 3);
        Assert.Equal(2.348, r.Output("Voltage drop Δu"), 2);
    }

    [Fact]
    public void Voltage_drop_limit_failure_is_reported()
    {
        var r = VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.SinglePhase, 230, 16, 120, 1.5, LimitPercent: 3));
        Assert.Equal(ResultStatus.Fail, r.Status);
    }

    [Theory]
    [InlineData(CableInsulation.PVC, 40, 0.866)]
    [InlineData(CableInsulation.PVC, 25, 1.061)]
    [InlineData(CableInsulation.XLPE, 40, 0.913)]
    [InlineData(CableInsulation.XLPE, 60, 0.707)]
    public void Temperature_correction_matches_table_b52_14(CableInsulation ins, double ambient, double expected) =>
        Assert.Equal(expected, CurrentCarryingCapacity.TemperatureFactor(ins, ambient), 3);

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(3, 0.70)]
    [InlineData(10, 0.45)] // conservative: next tabulated row (12)
    public void Grouping_factor(int n, double expected) =>
        Assert.Equal(expected, CurrentCarryingCapacity.GroupingFactor(n), 3);

    [Fact]
    public void Ampacity_lookup()
    {
        Assert.Equal(32, CurrentCarryingCapacity.Lookup(InstallationMethod.C, CableInsulation.PVC, 3, 4));
        Assert.Equal(27, CurrentCarryingCapacity.Lookup(InstallationMethod.C, CableInsulation.PVC, 2, 2.5));
        Assert.Null(CurrentCarryingCapacity.Lookup(InstallationMethod.C, CableInsulation.PVC, 3, 3));
    }

    [Fact]
    public void Cable_sizing_reference_case()
    {
        var r = CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, 30, 20));
        Assert.Equal(4, r.Output("Selected section"));
        Assert.Equal(32, r.Output("Protective device rating In"));
        Assert.Equal(ResultStatus.Pass, r.Status);
    }

    [Fact]
    public void Cable_sizing_applies_temperature_and_grouping()
    {
        Assert.Equal(6, CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, 30, 20, AmbientTemperatureC: 40)).Output("Selected section"));
        Assert.Equal(10, CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, 30, 20, GroupedCircuits: 3)).Output("Selected section"));
    }

    [Fact]
    public void Cable_sizing_increases_section_for_voltage_drop()
    {
        var r = CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, 30, 250, MaxVoltageDropPercent: 3));
        Assert.True(r.Output("Selected section") > 4);
        Assert.True(r.Output("Voltage drop") <= 3);
    }

    [Fact]
    public void Cable_sizing_refuses_aluminium()
    {
        var r = CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, 30, 20, Material: Conductor.Aluminium));
        Assert.Equal(ResultStatus.Fail, r.Status);
    }

    [Fact]
    public void Short_circuit_630kva_transformer()
    {
        var r = ShortCircuitCalculator.Calculate(new ShortCircuitInput(500, 630, 6, 6.5, 400, 50, 95));
        Assert.Equal(16.15, r.Output("I\"k3 at transformer LV terminals"), 1);
        Assert.True(r.Output("I\"k3 at cable end") < r.Output("I\"k3 at transformer LV terminals"));
        Assert.True(r.Output("ip at transformer LV terminals") > Math.Sqrt(2) * r.Output("I\"k3 at transformer LV terminals"));
    }

    [Fact]
    public void Transformer_sizing_selects_next_standard_rating()
    {
        var r = TransformerSizingCalculator.Calculate(500, 0.8, 0.9, 20);
        Assert.Equal(630, r.Output("Selected standard rating"));
        Assert.Equal(70.5, r.Output("Loading at maximum demand"), 1);
    }

    [Fact]
    public void Motor_starting_currents()
    {
        var dol = MotorStartingCalculator.Calculate(11, 400, 0.85, 0.9, 7, StartingMethod.DirectOnLine);
        var yd = MotorStartingCalculator.Calculate(11, 400, 0.85, 0.9, 7, StartingMethod.StarDelta);
        Assert.Equal(145.3, dol.Output("Starting current Ist"), 1);
        Assert.Equal(dol.Output("Starting current Ist") / 3, yd.Output("Starting current Ist"), 6);
    }

    [Fact]
    public void Breaker_selection_and_breaking_capacity()
    {
        var r = ProtectionCalculator.SelectBreaker(14, 24, 8, 6);
        Assert.Equal(16, r.Output("Selected rating In"));
        Assert.Equal(ResultStatus.Fail, r.Status); // Icu 6 kA < 8 kA
    }

    [Fact]
    public void Tn_maximum_length_conventional_method()
    {
        var r = ProtectionCalculator.TnMaximumLength(230, 2.5, 2.5, 160, actualLengthM: 40);
        Assert.Equal(63.89, r.Output("Maximum length Lmax"), 2);
        Assert.Equal(ResultStatus.Pass, r.Status);
    }

    [Fact]
    public void Load_summary_with_demand_factors()
    {
        var r = LoadCalculator.Summarize(new[]
        {
            new LoadItem("Pump", 10, 0.8, 1, Category: "Motor"),
            new LoadItem("Lighting", 5, 1, 0.8, Category: "Lighting"),
        }, 400);
        Assert.Equal(14, r.Output("Maximum demand P"), 6);
        Assert.Equal(7.5, r.Output("Reactive power Q"), 6); // 10·tan(acos 0.8)
        Assert.Equal(15.882, r.Output("Apparent power S"), 3);
    }

    [Fact]
    public void Load_balancing_distributes_single_phase_loads()
    {
        var loads = new[] { 3.0, 2, 2, 1, 1, 1 }.Select((p, i) => new LoadItem($"C{i}", p, Phase: "1P")).ToList();
        var (r, a) = LoadBalancingCalculator.Balance(loads);
        Assert.Equal(6, a.Count);
        var total = r.Output("L1 load") + r.Output("L2 load") + r.Output("L3 load");
        Assert.Equal(10, total, 6);
        Assert.True(r.Output("Imbalance") <= 20.0001);
    }

    [Fact]
    public void Standard_profiles_document_implementation_status()
    {
        Assert.Equal(5, StandardProfile.All.Count);
        Assert.Contains(StandardProfile.Get("IEC61439").Clauses, c => c.Status == ImplementationStatus.NotImplemented);
    }
}
