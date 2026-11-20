using System.Numerics;
using DND.Electrical.Calculations;
using DND.Electrical.Power;

namespace DND.Electrical.UnitTests.Power;

public class LoadFlowTests
{
    // Published IEEE 14-bus solution (MATPOWER runpf(case14)): |V| pu and angle (deg)
    public static IEnumerable<object[]> Ieee14Solution => new[]
    {
        new object[] { "1", 1.060, 0.000 }, new object[] { "2", 1.045, -4.983 }, new object[] { "3", 1.010, -12.725 }, new object[] { "4", 1.018, -10.313 },
        new object[] { "5", 1.020, -8.774 }, new object[] { "6", 1.070, -14.221 }, new object[] { "7", 1.062, -13.360 }, new object[] { "8", 1.090, -13.360 },
        new object[] { "9", 1.056, -14.939 }, new object[] { "10", 1.051, -15.097 }, new object[] { "11", 1.057, -14.791 }, new object[] { "12", 1.055, -15.076 },
        new object[] { "13", 1.050, -15.156 }, new object[] { "14", 1.036, -16.034 },
    };

    [Theory]
    [MemberData(nameof(Ieee14Solution))]
    public void Ieee14_bus_voltages_match_the_published_solution(string bus, double vm, double va)
    {
        var r = LoadFlowSolver.Solve(SampleNetworks.Ieee14());
        Assert.True(r.Converged);
        var b = r.Bus(bus);
        Assert.Equal(vm, b.VPu, 3);
        Assert.InRange(b.AngleDeg - va, -0.01, 0.01);
    }

    [Fact]
    public void Ieee14_slack_generation_losses_and_reactive_outputs_match()
    {
        var r = LoadFlowSolver.Solve(SampleNetworks.Ieee14());
        Assert.True(r.Converged);
        Assert.InRange(r.Iterations, 2, 6);
        static void Near(double expected, double actual, double tol) => Assert.InRange(actual, expected - tol, expected + tol);
        Near(232.39, r.Bus("1").PGenMw, 0.01);
        Near(-16.55, r.Bus("1").QGenMvar, 0.01);
        Near(13.393, r.LossesMw, 0.001);
        Near(43.56, r.Bus("2").QGenMvar, 0.01);
        Near(25.08, r.Bus("3").QGenMvar, 0.01);
        Near(12.73, r.Bus("6").QGenMvar, 0.01);
        Near(17.62, r.Bus("8").QGenMvar, 0.01);
        Assert.DoesNotContain(r.Messages, m => m.Contains("PV → PQ"));
        // Power balance: generation = load + losses
        Assert.Equal(r.Buses.Sum(b => b.PGenMw), r.Buses.Sum(b => b.PLoadMw) + r.LossesMw, 6);
    }

    [Fact]
    public void Two_bus_solution_satisfies_the_network_equation()
    {
        var n = new PowerNetwork { BaseMva = 10 };
        n.Buses.Add(new Bus { Id = "A", Kv = 20 }); n.Buses.Add(new Bus { Id = "B", Kv = 20 });
        n.Grids.Add(new ExternalGrid { Bus = "A", VoltagePu = 1.0 });
        n.Lines.Add(new Line { Id = "L", From = "A", To = "B", LengthKm = 10, ROhmPerKm = 0.4, XOhmPerKm = 0.4 });
        n.Loads.Add(new Load { Id = "LD", Bus = "B", PMw = 3, QMvar = 1 });
        var r = LoadFlowSolver.Solve(n);
        Assert.True(r.Converged);
        var zpu = new Complex(4, 4) / (20.0 * 20 / 10);
        var vb = Complex.FromPolarCoordinates(r.Bus("B").VPu, r.Bus("B").AngleDeg * Math.PI / 180);
        var s = new Complex(0.3, 0.1);
        var residual = (Complex.One - zpu * Complex.Conjugate(s / vb)) - vb;
        Assert.True(residual.Magnitude < 1e-7);
        Assert.Equal(3 + r.LossesMw, r.Bus("A").PGenMw, 6);
        var current = (s / vb).Magnitude * 10 / (Math.Sqrt(3) * 20) * 1000;
        Assert.Equal(current, r.Branches.Single().CurrentA, 3);
    }

    [Fact]
    public void Reactive_limits_switch_pv_buses_to_pq()
    {
        var n = SampleNetworks.Ieee14();
        n.Generators.First(g => g.Bus == "3").QmaxMvar = 10;
        var r = LoadFlowSolver.Solve(n);
        Assert.True(r.Converged);
        Assert.Equal(10, r.Bus("3").QGenMvar, 4);
        Assert.Equal(BusType.PQ, r.Bus("3").Type);
        Assert.True(r.Bus("3").VPu < 1.01);
        Assert.Contains(r.Messages, m => m.Contains("PV → PQ"));
    }

    [Fact]
    public void Data_errors_and_islands_are_reported_before_solving()
    {
        var n = new PowerNetwork();
        n.Buses.Add(new Bus { Id = "A", Kv = 20 }); n.Buses.Add(new Bus { Id = "B", Kv = 20 });
        n.Grids.Add(new ExternalGrid { Bus = "A" });
        n.Loads.Add(new Load { Id = "x", Bus = "Z" });
        var errors = n.Validate();
        Assert.Contains(errors, e => e.Contains("unknown bus 'Z'"));
        n.Loads.Clear();
        Assert.Contains(n.Validate(), e => e.Contains("Bus B is not connected"));
        Assert.Throws<InvalidOperationException>(() => LoadFlowSolver.Solve(n));
    }

    [Fact]
    public void Network_json_roundtrip()
    {
        var n = SampleNetworks.DemoIndustrial();
        var back = PowerNetwork.FromJson(n.ToJson());
        Assert.Equal(n.Buses.Count, back.Buses.Count);
        Assert.Equal(CurveType.MiniatureBreakerC, back.Protection.Single(p => p.Id == "F-M1").Curve);
        Assert.Equal(n.HarmonicSources[0].SpectrumPercent[5], back.HarmonicSources[0].SpectrumPercent[5]);
        Assert.Equal(LoadFlowSolver.Solve(n).LossesMw, LoadFlowSolver.Solve(back).LossesMw, 9);
    }

    [Fact]
    public void Two_hundred_bus_meshed_network_solves_quickly()
    {
        var n = new PowerNetwork { BaseMva = 100 };
        for (var i = 0; i < 200; i++) n.Buses.Add(new Bus { Id = $"B{i}", Kv = 110, VminPu = 0.8 });
        n.Grids.Add(new ExternalGrid { Bus = "B0", VoltagePu = 1.02 });
        for (var i = 1; i < 200; i++)
        {
            n.Lines.Add(new Line { Id = $"L{i}", From = $"B{i - 1}", To = $"B{i}", LengthKm = 5, ROhmPerKm = 0.05, XOhmPerKm = 0.4, BMicroSiemensPerKm = 2.8 });
            n.Loads.Add(new Load { Id = $"D{i}", Bus = $"B{i}", PMw = 0.3, QMvar = 0.1 });
            if (i % 10 == 0) n.Lines.Add(new Line { Id = $"M{i}", From = $"B{i - 10}", To = $"B{i}", LengthKm = 20, ROhmPerKm = 0.05, XOhmPerKm = 0.4 });
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = LoadFlowSolver.Solve(n);
        sw.Stop();
        Assert.True(r.Converged);
        Assert.True(sw.ElapsedMilliseconds < 10000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Equal(199 * 0.3 + r.LossesMw, r.Bus("B0").PGenMw, 5);
    }
}

public class ShortCircuitTests
{
    private static PowerNetwork Radial(double? gridC = null, double cableKm = 0.03)
    {
        var n = new PowerNetwork { BaseMva = 100, CmaxLowVoltage = 1.05 };
        n.Buses.Add(new Bus { Id = "MV", Kv = 20 }); n.Buses.Add(new Bus { Id = "LV", Kv = 0.4 }); n.Buses.Add(new Bus { Id = "END", Kv = 0.4 });
        n.Grids.Add(new ExternalGrid { Bus = "MV", SkMaxMva = 500, RxRatio = 0.1, CFactor = gridC });
        n.Transformers.Add(new Transformer { Id = "T", HvBus = "MV", LvBus = "LV", SnMva = 0.63, UkPercent = 6, PcuKw = 6.5, VectorGroup = "Dyn11" });
        n.Lines.Add(new Line { Id = "C", From = "LV", To = "END", LengthKm = cableKm, ROhmPerKm = 0.017241 / 95 * 1000, XOhmPerKm = 0.08, R0OhmPerKm = 4 * 0.017241 / 95 * 1000, X0OhmPerKm = 0.32 });
        return n;
    }

    [Fact]
    public void Radial_network_matches_the_simplified_radial_calculator()
    {
        // Same assumptions as the existing calculator (feeder impedance with the LV voltage factor 1.05).
        var r = ShortCircuitSolver.Solve(Radial(gridC: 1.05));
        var c = ShortCircuitCalculator.Calculate(new ShortCircuitInput(500, 630, 6, 6.5, 400, 30, 95, Conductor.Copper, 1));
        // The simplified calculator rounds XQ = 0.995·ZQ (exact: ZQ/√1.01) → agreement within 1e-5 relative.
        Assert.InRange(r.At("LV").Ik3MaxKa / c.Output("I\"k3 at transformer LV terminals"), 1 - 1e-5, 1 + 1e-5);
        Assert.InRange(r.At("END").Ik3MaxKa / c.Output("I\"k3 at cable end"), 1 - 1e-5, 1 + 1e-5);
    }

    [Fact]
    public void Radial_network_matches_hand_calculation_with_iec_feeder_factor()
    {
        var r = ShortCircuitSolver.Solve(Radial());
        // Hand calculation referred to 0.4 kV (ohms)
        double un = 400, cq = 1.10, c = 1.05;
        var zq = cq * un * un / 500e6; var xq = zq / Math.Sqrt(1.01); var rq = 0.1 * xq;
        var zt = 0.06 * un * un / 630e3; var rt = 6500 * un * un / (630e3 * 630e3); var xt = Math.Sqrt(zt * zt - rt * rt);
        var kt = 0.95 * c / (1 + 0.6 * (xt / (un * un / 630e3)));
        var z = new Complex(rq + kt * rt, xq + kt * xt);
        var ik = c * un / (Math.Sqrt(3) * z.Magnitude) / 1000;
        Assert.Equal(ik, r.At("LV").Ik3MaxKa, 6);
        // MV busbar: I"k = c·Sk/(√3·Un·cQ)… with c = cQ = 1.1 → Sk/(√3·Un)
        Assert.Equal(500 / (Math.Sqrt(3) * 20), r.At("MV").Ik3MaxKa, 6);
        Assert.Equal(500, r.At("MV").Sk3Mva, 6);
        // Two-phase fault: Ik2 = √3/2·Ik3
        Assert.Equal(Math.Sqrt(3) / 2 * r.At("LV").Ik3MaxKa, r.At("LV").Ik2Ka, 9);
        // Minimum current lower than maximum
        Assert.True(r.At("END").Ik3MinKa < r.At("END").Ik3MaxKa);
    }

    [Fact]
    public void Earth_faults_follow_transformer_vector_groups()
    {
        var n = Radial();
        var r = ShortCircuitSolver.Solve(n);
        var m = new NetworkModel(n);
        // LV of a Dyn transformer: Z0 = KT·ZT (feeder does not contribute), Z1 = Z2 = ZQ + KT·ZT
        var lv = r.At("LV");
        var zt = m.TransformerZ(n.Transformers[0]) * m.TransformerKt(n.Transformers[0]);
        var zq = m.GridZ(n.Grids[0], false);
        var expected = 3 * 1.05 / (2 * (zq + zt) + zt).Magnitude * m.Ibase(1);
        Assert.Equal(expected, lv.Ik1Ka, 6);
        Assert.True(lv.EarthFaultPath);
        // MV busbar: feeder Z0/Z1 = 1 → Ik1 = Ik3
        Assert.Equal(r.At("MV").Ik3MaxKa, r.At("MV").Ik1Ka, 6);
        // Feeder without zero-sequence path and a delta HV winding: no earth-fault current on MV
        n.Grids[0].Z0Z1Ratio = 0;
        var iso = ShortCircuitSolver.Solve(n);
        Assert.False(iso.At("MV").EarthFaultPath);
        Assert.Equal(0, iso.At("MV").Ik1Ka);
        Assert.True(iso.At("LV").EarthFaultPath);
        // Yy transformer: no earth path on LV either
        n.Transformers[0].VectorGroup = "Yy0";
        Assert.False(ShortCircuitSolver.Solve(n).At("LV").EarthFaultPath);
    }

    [Fact]
    public void Parallel_transformers_are_handled_by_the_bus_impedance_matrix()
    {
        var n = Radial();
        n.Transformers.Add(new Transformer { Id = "T2", HvBus = "MV", LvBus = "LV", SnMva = 0.63, UkPercent = 6, PcuKw = 6.5 });
        var m = new NetworkModel(n);
        var r = ShortCircuitSolver.Solve(n);
        var z = m.GridZ(n.Grids[0], false) + m.TransformerZ(n.Transformers[0]) * m.TransformerKt(n.Transformers[0]) / 2;
        Assert.Equal(1.05 / z.Magnitude * m.Ibase(1), r.At("LV").Ik3MaxKa, 6);
    }

    [Fact]
    public void Generator_contribution_uses_kg_correction()
    {
        var n = new PowerNetwork { BaseMva = 10 };
        n.Buses.Add(new Bus { Id = "G", Kv = 0.4 });
        n.Generators.Add(new Generator { Id = "G1", Bus = "G", SnMva = 1, XdSubtransientPu = 0.12, RxRatio = 0.15, RatedPowerFactor = 0.8 });
        var r = ShortCircuitSolver.Solve(n);
        var kg = 1.05 / (1 + 0.12 * 0.6);
        var z = new Complex(0.15 * 0.12, 0.12) * 10 * kg;
        Assert.Equal(1.05 / z.Magnitude * 10 / (Math.Sqrt(3) * 0.4), r.At("G").Ik3MaxKa, 6);
    }
}

public class ProtectionTests
{
    [Theory]
    [InlineData(CurveType.IecStandardInverse, 2.971)]
    [InlineData(CurveType.IecVeryInverse, 1.5)]
    [InlineData(CurveType.IecExtremelyInverse, 0.808)]
    [InlineData(CurveType.IecLongTimeInverse, 13.333)]
    public void Iec_curves_at_ten_times_pickup_and_tms_one(CurveType curve, double expected)
    {
        var d = new ProtectionDevice { Curve = curve, PickupA = 100, Tms = 1 };
        Assert.Equal(expected, ProtectionStudy.MaxTime(d, 1000), 3);
        Assert.True(double.IsPositiveInfinity(ProtectionStudy.MaxTime(d, 99)));
    }

    [Fact]
    public void Instantaneous_element_and_mcb_band_limits()
    {
        var relay = new ProtectionDevice { Curve = CurveType.IecStandardInverse, PickupA = 100, Tms = 1, InstantaneousA = 2000, InstantaneousTimeS = 0.05 };
        Assert.Equal(0.05, ProtectionStudy.MaxTime(relay, 2500));
        var mcb = new ProtectionDevice { Curve = CurveType.MiniatureBreakerC, PickupA = 16 };
        Assert.True(double.IsPositiveInfinity(ProtectionStudy.MinTime(mcb, 1.12 * 16)));
        Assert.True(double.IsPositiveInfinity(ProtectionStudy.MaxTime(mcb, 1.4 * 16)));
        Assert.Equal(3600, ProtectionStudy.MaxTime(mcb, 1.45 * 16), 6);
        Assert.Equal(60, ProtectionStudy.MaxTime(mcb, 2.55 * 16), 6);
        Assert.Equal(1, ProtectionStudy.MinTime(mcb, 2.55 * 16), 6);
        Assert.True(ProtectionStudy.MinTime(mcb, 4.99 * 16) >= 0.1);      // no instantaneous trip below 5 In
        Assert.Equal(0.1, ProtectionStudy.MaxTime(mcb, 10 * 16));          // instantaneous at 10 In
    }

    [Fact]
    public void Grading_margin_is_evaluated_over_the_fault_current_range()
    {
        var n = new PowerNetwork { GradingMarginS = 0.3 };
        n.Buses.Add(new Bus { Id = "B", Kv = 11 });
        n.Grids.Add(new ExternalGrid { Bus = "B", SkMaxMva = 100 });
        n.Protection.Add(new ProtectionDevice { Id = "UP", Bus = "B", Curve = CurveType.IecStandardInverse, PickupA = 400, Tms = 0.3 });
        n.Protection.Add(new ProtectionDevice { Id = "DOWN", Bus = "B", UpstreamId = "UP", Curve = CurveType.IecStandardInverse, PickupA = 400, Tms = 0.1 });
        var sc = ShortCircuitSolver.Solve(n);
        var f = Assert.Single(ProtectionStudy.Coordinate(n, sc));
        // Same pickup, SI curves: margin = 0.2·k/(M^0.02 − 1) is smallest at the maximum fault current.
        var m = sc.At("B").Ik3MaxKa * 1000 / 400;
        var expected = 0.2 * 0.14 / (Math.Pow(m, 0.02) - 1);
        Assert.Equal(expected, f.MinMarginS, 3);
        Assert.Equal(expected >= 0.3, f.Selective);
        n.Protection[0].Tms = 0.6;
        Assert.True(ProtectionStudy.Coordinate(n, sc).Single().Selective);
        Assert.Contains("<polyline", ProtectionStudy.TccSvg(n, sc));
    }

    [Fact]
    public void Instantaneous_overlap_is_not_reported_as_selective()
    {
        var n = SampleNetworks.DemoIndustrial();
        var sc = ShortCircuitSolver.Solve(n);
        var findings = ProtectionStudy.Coordinate(n, sc);
        var mcb = findings.Single(f => f.Downstream == "F-M1");
        Assert.False(mcb.Selective);
        Assert.Contains("selectivity tables", mcb.Message);
    }
}

public class HarmonicTests
{
    [Fact]
    public void Capacitor_resonance_occurs_at_sqrt_sk_over_qc()
    {
        var n = new PowerNetwork { BaseMva = 10 };
        n.Buses.Add(new Bus { Id = "B", Kv = 0.4 });
        n.Grids.Add(new ExternalGrid { Bus = "B", SkMaxMva = 50, RxRatio = 0.001, CFactor = 1.0 });
        n.Shunts.Add(new Shunt { Id = "C", Bus = "B", QMvar = 2 });
        var r = HarmonicStudy.Run(n, 20, 0.05);
        var res = Assert.Single(r.Resonances);
        Assert.InRange(res.Order, 4.95, 5.05);
    }

    [Fact]
    public void Harmonic_voltage_equals_impedance_times_injected_current()
    {
        var n = new PowerNetwork { BaseMva = 10 };
        n.Buses.Add(new Bus { Id = "B", Kv = 0.4 });
        n.Grids.Add(new ExternalGrid { Bus = "B", SkMaxMva = 20, RxRatio = 0.1, CFactor = 1.0 });
        n.HarmonicSources.Add(new HarmonicSource { Bus = "B", FundamentalA = 500, SpectrumPercent = new() { [5] = 20, [7] = 14 } });
        var r = HarmonicStudy.Run(n, 10);
        var zq = 0.4 * 0.4 / 20; var xq = zq / Math.Sqrt(1.01); var rq = 0.1 * xq;
        double V(int h, double ia) => new Complex(rq, xq * h).Magnitude * ia / (400 / Math.Sqrt(3)) * 100;
        var d = r.Distortion.Single();
        Assert.Equal(V(5, 100), d.VoltagePercent[5], 6);
        Assert.Equal(V(7, 70), d.VoltagePercent[7], 6);
        Assert.Equal(Math.Sqrt(V(5, 100) * V(5, 100) + V(7, 70) * V(7, 70)), d.ThdPercent, 6);
    }

    [Fact]
    public void Ideal_six_pulse_spectrum_is_one_over_h()
    {
        var s = HarmonicStudy.IdealSixPulse(13);
        Assert.Equal(new[] { 5, 7, 11, 13 }, s.Keys.OrderBy(k => k));
        Assert.Equal(20, s[5], 9);
    }
}
