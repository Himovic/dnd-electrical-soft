namespace DND.Electrical.Power;

public static class SampleNetworks
{
    /// <summary>
    /// IEEE 14-bus test system (public benchmark, 100 MVA base, per-unit data as distributed with the IEEE Common Data
    /// Format and MATPOWER case14). Used to validate the load flow; bus voltages are nominal placeholders (pu study).
    /// </summary>
    public static PowerNetwork Ieee14()
    {
        var n = new PowerNetwork { Name = "IEEE 14-bus test system (benchmark)", BaseMva = 100, FrequencyHz = 60 };
        double[,] bus =
        {
            // id Pd Qd Bs kV
            { 1, 0, 0, 0, 132 }, { 2, 21.7, 12.7, 0, 132 }, { 3, 94.2, 19.0, 0, 132 }, { 4, 47.8, -3.9, 0, 132 }, { 5, 7.6, 1.6, 0, 132 },
            { 6, 11.2, 7.5, 0, 33 }, { 7, 0, 0, 0, 1 }, { 8, 0, 0, 0, 18 }, { 9, 29.5, 16.6, 19, 33 }, { 10, 9.0, 5.8, 0, 33 },
            { 11, 3.5, 1.8, 0, 33 }, { 12, 6.1, 1.6, 0, 33 }, { 13, 13.5, 5.8, 0, 33 }, { 14, 14.9, 5.0, 0, 33 },
        };
        for (var i = 0; i < bus.GetLength(0); i++)
        {
            var id = ((int)bus[i, 0]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            n.Buses.Add(new Bus { Id = id, Name = $"Bus {id}", Kv = bus[i, 4], VminPu = 0.94, VmaxPu = 1.10 });
            if (bus[i, 1] != 0 || bus[i, 2] != 0) n.Loads.Add(new Load { Id = $"L{id}", Bus = id, PMw = bus[i, 1], QMvar = bus[i, 2] });
            if (bus[i, 3] != 0) n.Shunts.Add(new Shunt { Id = $"SH{id}", Bus = id, QMvar = bus[i, 3] });
        }
        n.Grids.Add(new ExternalGrid { Id = "G1 (slack)", Bus = "1", VoltagePu = 1.06, SkMaxMva = 5000 });
        void Gen(string bus, double p, double v, double qmin, double qmax) => n.Generators.Add(new Generator { Id = $"G{bus}", Bus = bus, PMw = p, VsetPu = v, QminMvar = qmin, QmaxMvar = qmax, SnMva = 100 });
        Gen("2", 40, 1.045, -40, 50); Gen("3", 0, 1.01, 0, 40); Gen("6", 0, 1.07, -6, 24); Gen("8", 0, 1.09, -6, 24);
        double[,] br =
        {
            { 1, 2, 0.01938, 0.05917, 0.0528, 0 }, { 1, 5, 0.05403, 0.22304, 0.0492, 0 }, { 2, 3, 0.04699, 0.19797, 0.0438, 0 },
            { 2, 4, 0.05811, 0.17632, 0.0340, 0 }, { 2, 5, 0.05695, 0.17388, 0.0346, 0 }, { 3, 4, 0.06701, 0.17103, 0.0128, 0 },
            { 4, 5, 0.01335, 0.04211, 0, 0 }, { 4, 7, 0, 0.20912, 0, 0.978 }, { 4, 9, 0, 0.55618, 0, 0.969 }, { 5, 6, 0, 0.25202, 0, 0.932 },
            { 6, 11, 0.09498, 0.19890, 0, 0 }, { 6, 12, 0.12291, 0.25581, 0, 0 }, { 6, 13, 0.06615, 0.13027, 0, 0 }, { 7, 8, 0, 0.17615, 0, 0 },
            { 7, 9, 0, 0.11001, 0, 0 }, { 9, 10, 0.03181, 0.08450, 0, 0 }, { 9, 14, 0.12711, 0.27038, 0, 0 }, { 10, 11, 0.08205, 0.19207, 0, 0 },
            { 12, 13, 0.22092, 0.19988, 0, 0 }, { 13, 14, 0.17093, 0.34802, 0, 0 },
        };
        for (var i = 0; i < br.GetLength(0); i++)
        {
            string f = ((int)br[i, 0]).ToString(System.Globalization.CultureInfo.InvariantCulture), t = ((int)br[i, 1]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            n.PuBranches.Add(new PuBranch { Id = $"{f}-{t}", From = f, To = t, R = br[i, 2], X = br[i, 3], B = br[i, 4], Tap = br[i, 5] });
        }
        return n;
    }

    /// <summary>Fictional industrial plant network for demonstrations (data are illustrative, not from a real site or manufacturer).</summary>
    public static PowerNetwork DemoIndustrial()
    {
        var n = new PowerNetwork { Name = "Demo industrial plant (fictional data)", BaseMva = 100, FrequencyHz = 50, CmaxLowVoltage = 1.05 };
        n.Buses.AddRange(new[]
        {
            new Bus { Id = "MV", Name = "20 kV incoming", Kv = 20 },
            new Bus { Id = "MDB1", Name = "Main LV board 1", Kv = 0.4 },
            new Bus { Id = "MDB2", Name = "Main LV board 2", Kv = 0.4 },
            new Bus { Id = "MCC1", Name = "Motor control centre", Kv = 0.4, VminPu = 0.95 },
            new Bus { Id = "DB-L", Name = "Lighting board", Kv = 0.4, VminPu = 0.95 },
        });
        n.Grids.Add(new ExternalGrid { Id = "Utility", Bus = "MV", SkMaxMva = 250, SkMinMva = 150, RxRatio = 0.1, Z0Z1Ratio = 1 });
        n.Transformers.Add(new Transformer { Id = "TR1", HvBus = "MV", LvBus = "MDB1", SnMva = 1.0, UkPercent = 6, PcuKw = 10.5, VectorGroup = "Dyn11" });
        n.Transformers.Add(new Transformer { Id = "TR2", HvBus = "MV", LvBus = "MDB2", SnMva = 0.63, UkPercent = 6, PcuKw = 6.5, VectorGroup = "Dyn11" });
        n.Lines.Add(new Line { Id = "C-MCC1", From = "MDB1", To = "MCC1", LengthKm = 0.08, ROhmPerKm = 0.0754, XOhmPerKm = 0.08, R0OhmPerKm = 0.30, X0OhmPerKm = 0.32, Parallel = 2, RatingA = 400 });
        n.Lines.Add(new Line { Id = "C-DBL", From = "MDB2", To = "DB-L", LengthKm = 0.12, ROhmPerKm = 0.727, XOhmPerKm = 0.08, R0OhmPerKm = 2.9, X0OhmPerKm = 0.32, RatingA = 100 });
        n.Loads.AddRange(new[]
        {
            new Load { Id = "MCC1 motors", Bus = "MCC1", PMw = 0.42, QMvar = 0.26 },
            new Load { Id = "MDB1 general", Bus = "MDB1", PMw = 0.18, QMvar = 0.09 },
            new Load { Id = "Lighting", Bus = "DB-L", PMw = 0.045, QMvar = 0.01 },
            new Load { Id = "MDB2 general", Bus = "MDB2", PMw = 0.30, QMvar = 0.15 },
        });
        n.Shunts.Add(new Shunt { Id = "PFC1", Bus = "MDB1", QMvar = 0.15 });
        n.Generators.Add(new Generator { Id = "Standby G1", Bus = "MDB2", InService = false, SnMva = 0.5, PMw = 0.3, XdSubtransientPu = 0.12, NeutralEarthed = true });
        n.Protection.AddRange(new[]
        {
            new ProtectionDevice { Id = "Q-INC1", Name = "MDB1 incomer", Bus = "MDB1", Curve = CurveType.IecStandardInverse, PickupA = 1500, Tms = 0.25, InstantaneousA = 15000, InstantaneousTimeS = 0.3 },
            new ProtectionDevice { Id = "Q-MCC1", Name = "MCC1 feeder", Bus = "MDB1", UpstreamId = "Q-INC1", Curve = CurveType.IecVeryInverse, PickupA = 630, Tms = 0.1, InstantaneousA = 6300, InstantaneousTimeS = 0.05 },
            new ProtectionDevice { Id = "F-M1", Name = "Motor outgoing (MCB C32)", Bus = "MCC1", UpstreamId = "Q-MCC1", Curve = CurveType.MiniatureBreakerC, PickupA = 32 },
        });
        n.HarmonicSources.Add(new HarmonicSource { Id = "VFD group", Bus = "MCC1", FundamentalA = 250, SpectrumPercent = HarmonicStudy.IdealSixPulse(25) });
        return n;
    }
}
