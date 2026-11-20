using DND.Electrical.Calculations;

namespace DND.Electrical.UnitTests.Calculations;

public class PowerStudioCalculatorTests
{
    [Fact]
    public void Power_factor_correction_matches_power_triangle()
    {
        // 100 kW from 0.7 to 0.95: tan φ1 = 1.0202, tan φ2 = 0.3287 → Qc = 69.15 kvar
        var r = PowerFactorCorrectionCalculator.Calculate(100, 0.7, 0.95, 400);
        Assert.Equal(100 * (Math.Tan(Math.Acos(0.7)) - Math.Tan(Math.Acos(0.95))), r.Output("Required capacitor power Qc"), 9);
        Assert.Equal(69.15, r.Output("Required capacitor power Qc"), 2);
        Assert.Equal(r.Output("Required capacitor power Qc") * 1000 / (Math.Sqrt(3) * 400), r.Output("Capacitor current"), 9);
        Assert.Throws<ArgumentException>(() => PowerFactorCorrectionCalculator.Calculate(100, 0.95, 0.9, 400));
    }

    [Fact]
    public void Power_factor_correction_flags_resonance_near_characteristic_harmonics()
    {
        // Sk = 25 MVA, Qc = 1000 kvar → h = 5.0 exactly
        var p = 1000 / (Math.Tan(Math.Acos(0.6)) - Math.Tan(Math.Acos(0.95)));
        var r = PowerFactorCorrectionCalculator.Calculate(p, 0.6, 0.95, 400, 25);
        Assert.Equal(5, r.Output("Resonance harmonic order"), 6);
        Assert.Equal(ResultStatus.Warning, r.Status);
        Assert.Contains(r.Checks, c => c.Contains("harmonic 5"));
    }

    [Fact]
    public void Generator_sizing_uses_the_governing_criterion()
    {
        // Running 400 kW at 0.8, 80 % loading → 625 kVA. Motor 75 kW × 6 kVA/kW = 450 kVA start; xd'' 0.15; 15 % dip → 0.15·450·(1/0.15 − 1) = 382.5 kVA
        var r = GeneratorSizingCalculator.Calculate(400, 0.8, 80, 75, 6, 0.15, 15);
        Assert.Equal(625, r.Output("Minimum generator rating"), 6);
        Assert.Equal(0, r.Output("Governing criterion"));
        // Large motor: 200 kW → 1200 kVA start → 0.15·1200·(5.6667) = 1020 kVA governs; dip at that rating is exactly 15 %
        var m = GeneratorSizingCalculator.Calculate(400, 0.8, 80, 200, 6, 0.15, 15);
        Assert.Equal(1020, m.Output("Minimum generator rating"), 6);
        Assert.Equal(15, m.Output("Estimated dip at that rating"), 6);
    }

    [Fact]
    public void Rod_resistance_matches_dwight_formula()
    {
        var r = EarthingCalculator.Rod(100, 3, 16);
        Assert.Equal(100 / (2 * Math.PI * 3) * (Math.Log(4 * 3 / 0.016) - 1), r.Output("Single rod resistance"), 9);
        Assert.Equal(29.82, r.Output("Single rod resistance"), 2);
    }

    [Fact]
    public void Grid_resistance_reproduces_ieee80_annex_b_example()
    {
        // IEEE Std 80 Annex B, square grid without rods: ρ = 400 Ω·m, 70 m × 70 m, LT = 1540 m, h = 0.5 m → Rg ≈ 2.78 Ω
        var r = EarthingCalculator.Grid(400, 70 * 70, 1540, 0.5);
        Assert.Equal(2.78, r.Output("Grid resistance Rg"), 2);
    }

    [Fact]
    public void Tolerable_voltages_reproduce_ieee80_annex_b_example()
    {
        // ρ = 400 Ω·m, crushed rock ρs = 2500 Ω·m, hs = 0.102 m, ts = 0.5 s, 70 kg → Cs ≈ 0.74, Etouch ≈ 838 V, Estep ≈ 2687 V
        var r = EarthingCalculator.TolerableVoltages(400, 2500, 0.102, 0.5, 70);
        var cs = r.Output("Surface layer factor Cs");
        Assert.Equal(0.74, cs, 2);
        Assert.Equal((1000 + 1.5 * cs * 2500) * 0.157 / Math.Sqrt(0.5), r.Output("Tolerable touch voltage"), 9);
        Assert.Equal((1000 + 6 * cs * 2500) * 0.157 / Math.Sqrt(0.5), r.Output("Tolerable step voltage"), 9);
        // The published example rounds Cs to 0.74 before evaluating: 838.2 V and 2686.6 V.
        Assert.Equal(838.2, (1000 + 1.5 * 0.74 * 2500) * 0.157 / Math.Sqrt(0.5), 1);
        Assert.Equal(2686.6, (1000 + 6 * 0.74 * 2500) * 0.157 / Math.Sqrt(0.5), 1);
        Assert.InRange(r.Output("Tolerable touch voltage") / 838.2, 0.995, 1.005);
        Assert.InRange(r.Output("Tolerable step voltage") / 2686.6, 0.995, 1.005);
        var gpr = EarthingCalculator.TolerableVoltages(400, 2500, 0.102, 0.5, 70, gridPotentialRiseV: 5000);
        Assert.Equal(ResultStatus.Warning, gpr.Status);
    }

    [Fact]
    public void Lumen_method_rounds_up_and_reports_achieved_illuminance()
    {
        // 20 m × 10 m, 500 lx, 4000 lm, UF 0.6, MF 0.8 → N = 500·200/(4000·0.48) = 52.08 → 53
        var r = LightingCalculator.Calculate(20, 10, 2.2, 500, 4000, 0.6, 0.8);
        Assert.Equal(53, r.Output("Luminaires required"));
        Assert.Equal(200.0 / (2.2 * 30), r.Output("Room index K"), 9);
        Assert.Equal(53 * 4000 * 0.48 / 200, r.Output("Achieved maintained illuminance"), 9);
        Assert.Equal(ResultStatus.Pass, r.Status);
    }
}
