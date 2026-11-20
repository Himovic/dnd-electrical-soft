using DND.Electrical.Calculations;

namespace DND.Electrical.UnitTests.Calculations;

public class IndustryCalculatorTests
{
    [Theory]
    [InlineData(RedundancyScheme.N, 4)]
    [InlineData(RedundancyScheme.NPlus1, 5)]
    [InlineData(RedundancyScheme.NPlus2, 6)]
    [InlineData(RedundancyScheme.TwoN, 8)]
    [InlineData(RedundancyScheme.TwoNPlus1, 9)]
    public void Redundancy_schemes_count_modules(RedundancyScheme scheme, int expected) =>
        Assert.Equal(expected, DataCenterCalculator.ModulesRequired(700, 200, scheme));

    [Fact]
    public void Redundancy_check_reports_loading_after_module_loss()
    {
        var r = DataCenterCalculator.Redundancy(700, 200, RedundancyScheme.NPlus1, 5);
        Assert.Equal(ResultStatus.Pass, r.Status);
        Assert.Equal(70, r.Output("Loading with all modules"), 9);
        Assert.Equal(87.5, r.Output("Loading after loss of one module"), 9);
        Assert.Equal(ResultStatus.Fail, DataCenterCalculator.Redundancy(700, 200, RedundancyScheme.TwoN, 6).Status);
    }

    [Fact]
    public void Autonomy_energy_balance()
    {
        // 200 kWh · 0.8 · 0.95 · 0.8 = 121.6 kWh usable at 400 kW → 18.24 min
        var r = DataCenterCalculator.Autonomy(400, 200, 0.8, 0.95, 0.8, 10);
        Assert.Equal(18.24, r.Output("Autonomy"), 6);
        Assert.Equal(ResultStatus.Pass, r.Status);
        Assert.Equal(10 * 400 / 60.0 / (0.8 * 0.95 * 0.8), r.Output("Battery energy for required autonomy"), 6);
        Assert.Equal(ResultStatus.Fail, DataCenterCalculator.Autonomy(400, 200, 0.8, 0.95, 0.8, 20).Status);
    }

    [Fact]
    public void Pue_and_dcie()
    {
        var r = DataCenterCalculator.Pue(1_500_000, 1_000_000);
        Assert.Equal(1.5, r.Output("PUE"), 9);
        Assert.Equal(100 / 1.5, r.Output("DCiE"), 9);
        Assert.Throws<ArgumentException>(() => DataCenterCalculator.Pue(1, 2));
    }

    [Fact]
    public void Pv_string_limits_follow_temperature_coefficients()
    {
        // Illustrative module: Voc 49.5 V, Vmp 41.6 V, Isc 13.9 A, βVoc −0.25 %/K, βVmp −0.30 %/K; −10 °C … 70 °C cell; inverter 1100 V, MPPT 200–1000 V, 30 A
        var r = PvStringCalculator.Calculate(49.5, 41.6, 13.9, -0.25, -0.30, -10, 70, 1100, 200, 1000, 30, 20, 2);
        var vocMax = 49.5 * (1 - 0.0025 * -35);   // 53.83 V
        var vmpMin = 41.6 * (1 - 0.0030 * 45);    // 35.98 V
        var vmpCold = 41.6 * (1 - 0.0030 * -35);  // 45.97 V
        Assert.Equal(Math.Floor(Math.Min(1100 / vocMax, 1000 / vmpCold)), r.Output("Max modules per string"));
        Assert.Equal(Math.Ceiling(200 / vmpMin), r.Output("Min modules per string"));
        Assert.Equal(vocMax * 20, r.Output("String Voc at Tmin"), 6);
        Assert.Equal(ResultStatus.Pass, r.Status);
        Assert.Equal(ResultStatus.Fail, PvStringCalculator.Calculate(49.5, 41.6, 13.9, -0.25, -0.30, -10, 70, 1100, 200, 1000, 30, 22, 2).Status);
        Assert.Equal(ResultStatus.Fail, PvStringCalculator.Calculate(49.5, 41.6, 13.9, -0.25, -0.30, -10, 70, 1100, 200, 1000, 20, 20, 2).Status);
    }
}
