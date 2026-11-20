using DND.Electrical.Application.Services;
using DND.Electrical.Calculations;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.PLC;
using DND.Electrical.Power;
using DND.Electrical.Wiring;
using PowerLoad = DND.Electrical.Power.Load;

namespace DND.Electrical.Application.Demo;

/// <summary>
/// Industry demonstration projects (manufacturing, railway, data center, oil and gas, solar). All names, products,
/// prices, network data and certificate records are fictional and marked as demonstration data.
/// </summary>
public sealed partial class DemoProjectFactory
{
    private sealed record Feeder(string Name, double Kw, double Pf, double Ku, double LengthM, LoadCategory Category, bool Motor);

    /// <summary>Outgoing feeders with breaker, sized cable (stored calculation), motor or terminals, and load list entries.</summary>
    private static void FeederSheet(CircuitBuilder b, ElectricalProject p, string title, string function, string location, string board, string strip, IReadOnlyList<Feeder> feeders, string[]? phases = null)
    {
        phases ??= new[] { "L1", "L2", "L3" };
        var sheet = b.Sheet(title, SheetKind.Power, $"{board} outgoing circuits", function, location);
        for (var i = 0; i < feeders.Count; i++)
        {
            var l = feeders[i];
            var cx = CircuitBuilder.Col(1) + i * 65;
            var a = b.Supply(sheet, phases[0], cx, 270); var bb = b.Supply(sheet, phases[1], cx + 10, 270); var c = b.Supply(sheet, phases[2], cx + 20, 270);
            var q = b.Place(sheet, "IEC.CB.3P", cx, 230, function: l.Name);
            b.Wire(sheet, a, "1", q, "1"); b.Wire(sheet, bb, "1", q, "3"); b.Wire(sheet, c, "1", q, "5");
            var ib = l.Kw * 1000 / (Math.Sqrt(3) * 400 * l.Pf);
            var size = CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, Math.Round(ib, 1), l.LengthM, InstallationMethod.E, CableInsulation.XLPE, 35, 3, l.Pf, 4));
            var section = size.Outputs.Any(o => o.Name == "Selected section") ? size.Output("Selected section") : 0;
            var rating = size.Outputs.Any(o => o.Name == "Protective device rating In") ? size.Output("Protective device rating In") : 0;
            var dev = b.Spec(q, $"DEMO-MCCB-{rating:0}A", $"Moulded-case breaker 3P {rating:0} A 36 kA", 180m + (decimal)rating * 1.2m, 105, 160, 90, $"{rating:0} A");
            var cable = CableService.CreateCable(p, "N2XY", 4, section, l.LengthM, location, l.Name);
            cable.Insulation = InsulationType.XLPE;
            CalculationService.Store(p, size, $"Cable sizing {cable.Tag} – {l.Name}", cable.Id);
            if (l.Motor)
            {
                var m = b.Place(sheet, "IEC.MOTOR.3PH", cx, 150, function: l.Name);
                foreach (var (from, to) in new[] { ("2", "U1"), ("4", "V1"), ("6", "W1") })
                    CableService.AssignWire(cable, b.Wire(sheet, q, from, m, to));
                var pe = b.Supply(sheet, "PE", cx + 30, 270);
                CableService.AssignWire(cable, b.Wire(sheet, pe, "1", m, "PE"));
                b.Spec(m, $"DEMO-MOT-{l.Kw:0}KW", $"Three-phase motor {l.Kw:0.#} kW", 120m + (decimal)l.Kw * 55m, rating: $"{l.Kw:0.#} kW");
                p.Motors.Add(new Motor { DeviceId = m.DeviceId, Tag = b.Device(m).Tag, Description = l.Name, PowerKw = l.Kw, VoltageV = 400, PowerFactor = l.Pf, Efficiency = 0.93, RatedCurrentA = Math.Round(ib, 1), StartingMethod = l.Kw > 30 ? MotorStartingMethod.SoftStarter : MotorStartingMethod.DirectOnLine, ProtectionDeviceTag = dev.Tag });
            }
            else
            {
                for (var k = 0; k < 3; k++)
                {
                    var t = b.Terminal(sheet, strip, (i * 3 + k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), cx + k * 10, 180);
                    CableService.AssignWire(cable, b.Wire(sheet, q, (2 + 2 * k).ToString(System.Globalization.CultureInfo.InvariantCulture), t, "1"));
                }
                sheet.Drawing.Add(new TextEntity(new Point2(cx, 160), $"→ {l.Name}", 2.5) { Layer = StandardLayers.Annotations });
            }
            p.Loads.Add(new ElectricalLoad
            {
                Tag = $"{board}-F{i + 1}", Description = l.Name, Board = board, Circuit = $"F{i + 1}", Category = l.Category, Phase = PhaseAssignment.ThreePhase,
                PowerKw = l.Kw, PowerFactor = l.Pf, DemandFactor = l.Ku, VoltageV = 400, CableLengthM = l.LengthM, CableCrossSectionMm2 = section,
                ProtectionRatingA = rating, ProtectionDeviceTag = dev.Tag, CableId = cable.Id,
            });
        }
    }

    private static PlcStation Plc(ElectricalProject p, string tag, string name, string ip, params string[] modules)
    {
        var st = PlcService.CreateStation(p, tag, name, "generic-iec");
        st.IpAddress = ip;
        st.Description = "Generic PLC (vendor-neutral demo profile)";
        PlcService.AddModule(st, st.Racks[0], "GEN-CPU");
        foreach (var m in modules) PlcService.AddModule(st, st.Racks[0], m);
        return st;
    }

    /// <summary>Instrument wired to the next spare channel of the requested kind (demo data, consistent with the AUTO rules).</summary>
    private static Instrument AddInstrument(ElectricalProject p, PlcStation st, string tag, string description, MeasuredVariable v, InstrumentFunction f, InstrumentSignal s,
        double min, double max, string unit, string area, string zone = "", string ex = "", string jb = "JB-01")
    {
        var i = new Instrument { Tag = tag, Description = description, Variable = v, Function = f, Signal = s, RangeMin = min, RangeMax = max, Unit = unit, Area = area, HazardousZone = zone, ExMarking = ex, JunctionBox = jb, Manufacturer = "D&D Demo Instruments (fictional)", Model = "DEMO-" + tag.Split('-')[0], Status = InstrumentStatus.Specified };
        var output = f is InstrumentFunction.ControlValve or InstrumentFunction.OnOffValve or InstrumentFunction.Actuator;
        var kind = (i.IsAnalog, output) switch { (true, false) => PlcSignalKind.AnalogInput, (true, true) => PlcSignalKind.AnalogOutput, (false, false) => PlcSignalKind.DigitalInput, _ => PlcSignalKind.DigitalOutput };
        var ch = st.Channels.First(c => c.Kind == kind && c.IsSpare);
        i.PlcChannelId = ch.Id; ch.Tag = tag; ch.Description = description;
        ch.SignalType = i.IsAnalog ? "4–20 mA" : "24 V DC";
        p.Instruments.Add(i);
        return i;
    }

    private static ControlLoop Loop(ElectricalProject p, string tag, string description, LoopType type, Instrument measurement, Instrument? final, string controller, double? sp, double? kp = null, double? ti = null)
    {
        var l = new ControlLoop { Tag = tag, Description = description, Type = type, MeasurementId = measurement.Id, FinalElementId = final?.Id, Controller = controller, SetPoint = sp, Unit = measurement.Unit, Gain = kp, IntegralS = ti };
        measurement.LoopId = l.Id;
        if (final is not null) final.LoopId = l.Id;
        p.Loops.Add(l);
        return l;
    }

    private static void Alarms(ElectricalProject p, Instrument i, double? ll, double? lo, double? hi, double? hh)
    {
        void A(AlarmType t, string sfx, double? sp, AlarmPriority pr, string action)
        {
            if (sp is null) return;
            p.Alarms.Add(new AlarmDefinition { Tag = $"{i.Tag}-{sfx}", InstrumentId = i.Id, Type = t, SetPoint = sp, Unit = i.Unit, Priority = pr, Class = AlarmClass.Process, Message = $"{i.Description} {sfx}", OperatorAction = action, DelayS = 2, Deadband = Math.Round((i.RangeMax - i.RangeMin) * 0.01, 3) });
        }
        A(AlarmType.LowLow, "LL", ll, AlarmPriority.High, "Check trip and process conditions before restart.");
        A(AlarmType.Low, "L", lo, AlarmPriority.Medium, "Investigate the cause; prepare corrective action.");
        A(AlarmType.High, "H", hi, AlarmPriority.Medium, "Investigate the cause; prepare corrective action.");
        A(AlarmType.HighHigh, "HH", hh, AlarmPriority.High, "Check trip and process conditions before restart.");
    }

    private static ScadaNode Node(ElectricalProject p, string name, ScadaNodeKind kind, string ip, ScadaNode? parent, string protocol = "", string network = "Control LAN", string zone = "Control", bool redundant = false, PlcStation? plc = null)
    {
        var n = new ScadaNode { Name = name, Kind = kind, IpAddress = ip, ParentId = parent?.Id, Protocol = protocol, Network = network, Zone = zone, Redundant = redundant, PlcStationId = plc?.Id, Notes = "Demonstration data" };
        p.ScadaNodes.Add(n);
        return n;
    }

    private static void Lifecycle(ElectricalProject p, LifecyclePhase phase, params string[] tasks)
    {
        p.Info.Phase = phase;
        p.Members.Add(new ProjectMember { Name = "D&D Demo Engineer", Role = ProjectRole.LeadEngineer, Discipline = "Electrical" });
        p.Members.Add(new ProjectMember { Name = "Automation Engineer (demo)", Role = ProjectRole.Engineer, Discipline = "Automation" });
        p.Members.Add(new ProjectMember { Name = "Approver (demo)", Role = ProjectRole.Approver, Discipline = "Quality" });
        p.Members.Add(new ProjectMember { Name = "Project Manager (demo)", Role = ProjectRole.ProjectManager, Discipline = "Management" });
        var start = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        foreach (var (t, i) in tasks.Select((t, i) => (t, i)))
            p.Tasks.Add(new ProjectTask { Title = t, Assignee = i % 2 == 0 ? "D&D Demo Engineer" : "Automation Engineer (demo)", Phase = phase, DueDate = start.AddDays(7 * (i + 1)), Status = i == 0 ? WorkStatus.Done : i == 1 ? WorkStatus.InProgress : WorkStatus.Open });
        p.Milestones.Add(new Milestone { Name = "Issued for review", Phase = LifecyclePhase.DetailedEngineering, Due = start.AddDays(21) });
        p.Milestones.Add(new Milestone { Name = "Issued for construction", Phase = LifecyclePhase.Construction, Due = start.AddDays(56) });
        p.DocumentRegister.Add(new DocumentRegisterEntry { Number = $"{p.Info.Number}-E-001", Title = "Electrical schematics", Kind = "Drawing set", Revision = "A", Status = "For review" });
        p.DocumentRegister.Add(new DocumentRegisterEntry { Number = $"{p.Info.Number}-E-002", Title = "Calculation note", Kind = "Report", Revision = "A", Status = "Draft" });
    }

    private static void Network(ElectricalProject p, PowerNetwork n) =>
        p.PowerNetworks.Add(new PowerNetworkDocument { Name = n.Name, NetworkJson = n.ToJson() });

    // ===================================================================== DEMO-MFG-006
    private ElectricalProject Manufacturing()
    {
        var (p, b) = NewProject("DEMO-MFG-006", "Packaging Works Demo (fictional)", "Hall 4 – packaging line PL-2");
        p.Settings.StandardProfileId = "IEC60204";
        var power = b.Sheet("Line power and drives", SheetKind.Power, "Main switch and DOL motor feeders", "=PL2", "+CP4");
        MainSwitch(b, power, CircuitBuilder.Col(1), "IEC.CB.3P");
        var infeed = b.DolFeeder(power, CircuitBuilder.Col(3), "Infeed conveyor", "-X1", 1, phases: Main);
        var wrap = b.DolFeeder(power, CircuitBuilder.Col(5), "Wrapper drive", "-X1", 5, phases: Main);
        foreach (var f in new[] { infeed, wrap })
        {
            b.Spec(f.Motor, "DEMO-MOT-1K5-4P", "Three-phase motor 1.5 kW", 260m, rating: "1.5 kW");
            b.Spec(f.Breaker, "DEMO-MPCB-4A", "Motor protection breaker 2.5–4 A", 47.50m, 45, 90, 80, "2.5–4 A");
            b.Spec(f.Contactor, "DEMO-CONT-9A-24DC", "Contactor 9 A AC-3, coil 24 V DC", 29.50m, 45, 82, 90, "9 A");
            if (f.Overload is { } o) b.Spec(o, "DEMO-OLR-2.5-4A", "Thermal overload relay 2.5–4 A", 29.00m, 45, 75, 80, "2.5–4 A");
        }
        var ctrl = b.Sheet("Control supply", SheetKind.Control, "24 V DC supply", "=PL2", "+CP4");
        b.Spec(b.PowerSupply24V(ctrl, CircuitBuilder.Col(2), "1L1", "N"), "DEMO-PSU-24V-5A", "Power supply 24 V DC 5 A", 99m, 40, 125, 115, "24 V DC 5 A");
        var rungs = b.Sheet("Motor control", SheetKind.Control, "Start/stop circuits of the line drives", "=PL2", "+CP4");
        foreach (var (f, name, i) in new[] { (infeed, "infeed conveyor", 0), (wrap, "wrapper", 1) })
        {
            var (_, start, stop) = b.StartStopRung(rungs, CircuitBuilder.Col(1 + i * 3), b.Device(f.Contactor).Tag, f.Overload is { } o ? b.Device(o).Tag : null, $"Start {name}", $"Stop {name}", i == 0 ? "-S0" : null);
            b.Spec(start, "DEMO-PB-GN-1NO", "Push button green, 1NO", 12.40m, 30, 30, 45);
            b.Spec(stop, "DEMO-PB-RD-1NC", "Push button red, 1NC", 12.40m, 30, 30, 45);
        }
        var estop = rungs.Drawing.Entities.OfType<DND.Electrical.Schematic.Entities.SymbolInstanceEntity>().First(e => e.SymbolId == "IEC.ESTOP");
        b.Spec(estop, "DEMO-ESTOP-40", "Emergency stop mushroom 40 mm", 24.60m, 40, 40, 60);
        FeederSheet(b, p, "Hall 4 sub-distribution", "=PL2", "+SD4", "SD4", "-X20", new[]
        {
            new Feeder("Sealing jaw heaters", 18, 1.0, 0.9, 25, LoadCategory.Process, false),
            new Feeder("Compressed air dryer", 7.5, 0.85, 0.8, 40, LoadCategory.Process, false),
            new Feeder("Palletiser", 22, 0.86, 0.7, 35, LoadCategory.Motor, true),
            new Feeder("Hall lighting", 12, 0.95, 1.0, 60, LoadCategory.Lighting, false),
        });
        var st = Plc(p, "-A10", "Line PL-2 controller", "192.168.40.10", "GEN-DI16", "GEN-DO8", "GEN-AI4", "GEN-AO4");
        var tt = AddInstrument(p, st, "TT-201", "Sealing jaw temperature", MeasuredVariable.Temperature, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, 0, 250, "°C", "Wrapper");
        var tv = AddInstrument(p, st, "TY-201", "Heater power controller", MeasuredVariable.Temperature, InstrumentFunction.Actuator, InstrumentSignal.Analog4To20mA, 0, 100, "%", "Wrapper");
        var pt = AddInstrument(p, st, "PT-202", "Compressed air header pressure", MeasuredVariable.Pressure, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, 0, 10, "bar", "Utilities");
        var wt = AddInstrument(p, st, "WT-203", "Check-weigher load cell", MeasuredVariable.Weight, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, 0, 5, "kg", "Outfeed");
        AddInstrument(p, st, "XV-204", "Reject diverter valve", MeasuredVariable.Position, InstrumentFunction.OnOffValve, InstrumentSignal.Digital24VDC, 0, 1, "", "Outfeed");
        Loop(p, "TIC-201", "Sealing jaw temperature control", LoopType.Pid, tt, tv, "-A10", 165, 2.5, 40);
        Loop(p, "PI-202", "Air header pressure monitoring", LoopType.Monitoring, pt, null, "-A10", null);
        Loop(p, "WI-203", "Check-weigher monitoring", LoopType.Monitoring, wt, null, "-A10", null);
        Alarms(p, tt, null, 140, 185, 200);
        Alarms(p, pt, 4.5, 5.5, null, null);
        var fw = Node(p, "FW-01 Plant firewall", ScadaNodeKind.Firewall, "192.168.40.1", null, network: "Control LAN", zone: "DMZ");
        var sw = Node(p, "SW-01 Line switch", ScadaNodeKind.Switch, "192.168.40.2", fw);
        Node(p, "-A10 Line PL-2 controller", ScadaNodeKind.Plc, "192.168.40.10", sw, "PROFINET / OPC UA", plc: st);
        Node(p, "HMI-01 Operator panel", ScadaNodeKind.Hmi, "192.168.40.20", sw, "OPC UA");
        Node(p, "SCADA-01 Line supervisory server", ScadaNodeKind.ScadaServer, "192.168.40.30", sw, "OPC UA", redundant: true);
        Node(p, "HIS-01 Historian", ScadaNodeKind.Historian, "192.168.40.31", sw, "OPC UA");
        CalculationService.Store(p, LightingCalculator.Calculate(40, 24, 6.5, 300, 18000, 0.65, 0.8), "Hall 4 lighting (UF from demo photometric table)");
        CalculationService.Store(p, PowerFactorCorrectionCalculator.Calculate(85, 0.78, 0.95, 400, 12), "SD4 power factor correction");
        Lifecycle(p, LifecyclePhase.DetailedEngineering, "Freeze motor list", "Complete I/O list", "Loop diagrams TIC-201", "FAT procedure");
        var n = new PowerNetwork { Name = "Hall 4 network (demo data)" };
        n.Buses.AddRange(new[] { new Bus { Id = "MV", Kv = 20 }, new Bus { Id = "MDB", Kv = 0.4 }, new Bus { Id = "SD4", Kv = 0.4 } });
        n.Grids.Add(new ExternalGrid { Id = "Utility", Bus = "MV", SkMaxMva = 300, SkMinMva = 200 });
        n.Transformers.Add(new Transformer { Id = "T1", HvBus = "MV", LvBus = "MDB", SnMva = 0.63, UkPercent = 6, PcuKw = 6.5 });
        n.Lines.Add(new Line { Id = "C-SD4", From = "MDB", To = "SD4", LengthKm = 0.09, ROhmPerKm = 0.193, XOhmPerKm = 0.08, R0OhmPerKm = 0.77, X0OhmPerKm = 0.32, RatingA = 250 });
        n.Loads.Add(new PowerLoad { Id = "SD4 loads", Bus = "SD4", PMw = 0.05, QMvar = 0.022 });
        n.Loads.Add(new PowerLoad { Id = "Line PL-2", Bus = "MDB", PMw = 0.12, QMvar = 0.07 });
        Network(p, n);
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-RAIL-007
    private ElectricalProject Railway()
    {
        var (p, b) = NewProject("DEMO-RAIL-007", "Metro Line Demo Authority (fictional)", "Station S07 – auxiliary substation");
        FeederSheet(b, p, "Station LV board A", "=STN", "+LVA", "LVA", "-X30", new[]
        {
            new Feeder("Escalator ES-1", 30, 0.86, 0.8, 70, LoadCategory.Motor, true),
            new Feeder("Platform lighting PL-A", 18, 0.95, 1.0, 120, LoadCategory.Lighting, false),
            new Feeder("Ventilation fan VF-1", 37, 0.87, 0.9, 90, LoadCategory.Hvac, true),
            new Feeder("Signalling UPS A input", 40, 0.95, 1.0, 45, LoadCategory.Other, false),
        });
        FeederSheet(b, p, "Station LV board B", "=STN", "+LVB", "LVB", "-X31", new[]
        {
            new Feeder("Escalator ES-2", 30, 0.86, 0.8, 75, LoadCategory.Motor, true),
            new Feeder("Platform lighting PL-B", 18, 0.95, 1.0, 125, LoadCategory.Lighting, false),
            new Feeder("Lift L-1", 15, 0.85, 0.5, 60, LoadCategory.Motor, true),
            new Feeder("Signalling UPS B input", 40, 0.95, 1.0, 45, LoadCategory.Other, false),
        }, new[] { "B-L1", "B-L2", "B-L3" });
        b.Sheet("SCADA and telecom interfaces", SheetKind.Plc, "RTU, protection IEDs and control centre link (see Automation → Architecture)", "=SCADA", "+SER");
        var rtuSt = Plc(p, "-A50", "Station RTU", "10.7.7.50", "GEN-DI16", "GEN-DO8");
        var oc = Node(p, "OCC gateway (control centre link)", ScadaNodeKind.Gateway, "10.7.0.1", null, "IEC 60870-5-104", network: "SCADA WAN", zone: "WAN");
        var sw = Node(p, "SW-S07 Station switch", ScadaNodeKind.Switch, "10.7.7.2", oc, network: "Station LAN");
        Node(p, "RTU-S07", ScadaNodeKind.Rtu, "10.7.7.50", sw, "IEC 60870-5-104", network: "Station LAN", plc: rtuSt, redundant: true);
        Node(p, "IED Incomer A", ScadaNodeKind.Ied, "10.7.7.61", sw, "IEC 61850", network: "Station LAN");
        Node(p, "IED Incomer B", ScadaNodeKind.Ied, "10.7.7.62", sw, "IEC 61850", network: "Station LAN");
        Node(p, "UPS A monitoring", ScadaNodeKind.Gateway, "10.7.7.71", sw, "Modbus TCP", network: "Station LAN");
        Node(p, "UPS B monitoring", ScadaNodeKind.Gateway, "10.7.7.72", sw, "Modbus TCP", network: "Station LAN");
        CalculationService.Store(p, LightingCalculator.Calculate(120, 6, 4, 200, 6500, 0.55, 0.75), "Platform lighting (UF from demo photometric table)");
        CalculationService.Store(p, DataCenterCalculator.Autonomy(30, 60, 0.8, 0.94, 0.8, 60), "Signalling UPS A autonomy");
        CalculationService.Store(p, GeneratorSizingCalculator.Calculate(180, 0.85, 80, 37, 6, 0.14, 20), "Standby generator (preliminary)");
        CalculationService.Store(p, TransformerSizingCalculator.Calculate(p.Loads.Sum(l => l.PowerKw), 0.8, 0.9, 20), "Auxiliary transformer sizing");
        Lifecycle(p, LifecyclePhase.PreliminaryEngineering, "Load list per board", "Short-circuit study", "Discrimination study", "Interface schedule with signalling");
        var n = new PowerNetwork { Name = "Station S07 auxiliary network (demo data)", GradingMarginS = 0.3 };
        n.Buses.AddRange(new[] { new Bus { Id = "HV", Kv = 11 }, new Bus { Id = "LVA", Kv = 0.4 }, new Bus { Id = "LVB", Kv = 0.4 } });
        n.Grids.Add(new ExternalGrid { Id = "11 kV ring", Bus = "HV", SkMaxMva = 250, SkMinMva = 150 });
        n.Transformers.Add(new Transformer { Id = "TA", HvBus = "HV", LvBus = "LVA", SnMva = 0.5, UkPercent = 4.5, PcuKw = 5.5 });
        n.Transformers.Add(new Transformer { Id = "TB", HvBus = "HV", LvBus = "LVB", SnMva = 0.5, UkPercent = 4.5, PcuKw = 5.5 });
        n.Loads.Add(new PowerLoad { Id = "Board A", Bus = "LVA", PMw = 0.11, QMvar = 0.05 });
        n.Loads.Add(new PowerLoad { Id = "Board B", Bus = "LVB", PMw = 0.09, QMvar = 0.045 });
        n.Generators.Add(new Generator { Id = "Standby G1", Bus = "LVA", InService = false, SnMva = 0.25, PMw = 0.18, XdSubtransientPu = 0.14, NeutralEarthed = true });
        n.Protection.Add(new ProtectionDevice { Id = "QA-INC", Name = "Board A incomer", Bus = "LVA", Curve = CurveType.IecStandardInverse, PickupA = 800, Tms = 0.3 });
        n.Protection.Add(new ProtectionDevice { Id = "QA-ES1", Name = "Escalator ES-1", Bus = "LVA", UpstreamId = "QA-INC", Curve = CurveType.IecVeryInverse, PickupA = 80, Tms = 0.1 });
        Network(p, n);
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-DC-008
    private ElectricalProject DataCenter()
    {
        var (p, b) = NewProject("DEMO-DC-008", "Colocation Demo Campus (fictional)", "Building DC1 – data hall 1");
        FeederSheet(b, p, "UPS A input and mechanical board A", "=DC", "+MSB-A", "MSB-A", "-X40", new[]
        {
            new Feeder("UPS A module 1 input", 105, 0.99, 1.0, 30, LoadCategory.Other, false),
            new Feeder("UPS A module 2 input", 105, 0.99, 1.0, 30, LoadCategory.Other, false),
            new Feeder("CRAH units A", 45, 0.9, 0.9, 60, LoadCategory.Hvac, true),
            new Feeder("Chilled water pump A", 30, 0.88, 1.0, 50, LoadCategory.Hvac, true),
        });
        FeederSheet(b, p, "UPS B input and mechanical board B", "=DC", "+MSB-B", "MSB-B", "-X41", new[]
        {
            new Feeder("UPS B module 1 input", 105, 0.99, 1.0, 30, LoadCategory.Other, false),
            new Feeder("UPS B module 2 input", 105, 0.99, 1.0, 30, LoadCategory.Other, false),
            new Feeder("CRAH units B", 45, 0.9, 0.9, 60, LoadCategory.Hvac, true),
            new Feeder("Chilled water pump B", 30, 0.88, 1.0, 50, LoadCategory.Hvac, true),
        }, new[] { "B-L1", "B-L2", "B-L3" });
        b.Sheet("Single-line diagram", SheetKind.Single_Line, "2N topology: utility A/B, generators, UPS A/B, PDUs (see PowerStudio network)", "=DC", "+DC1");
        CalculationService.Store(p, DataCenterCalculator.Redundancy(360, 200, RedundancyScheme.TwoN, 4), "UPS redundancy 2N (2 × 2 × 200 kW)");
        CalculationService.Store(p, DataCenterCalculator.Autonomy(360, 180, 0.8, 0.96, 0.8, 10), "UPS A battery autonomy at full IT load");
        CalculationService.Store(p, DataCenterCalculator.Pue(5_150_000, 3_150_000), "Design PUE estimate (annual energy model, demo values)");
        CalculationService.Store(p, GeneratorSizingCalculator.Calculate(560, 0.9, 80, 45, 6, 0.15, 15), "Generator set per side (preliminary)");
        var epms = Plc(p, "-A70", "EPMS controller", "10.20.0.70", "GEN-DI16", "GEN-AI4");
        var sw = Node(p, "SW-EPMS core switch", ScadaNodeKind.Switch, "10.20.0.2", null, network: "EPMS LAN", zone: "OT", redundant: true);
        Node(p, "EPMS server", ScadaNodeKind.ScadaServer, "10.20.0.10", sw, "Modbus TCP / SNMP", "EPMS LAN", "OT", true);
        Node(p, "-A70 EPMS controller", ScadaNodeKind.Plc, "10.20.0.70", sw, "Modbus TCP", "EPMS LAN", "OT", plc: epms);
        Node(p, "Power meter MSB-A", ScadaNodeKind.Ied, "10.20.0.101", sw, "Modbus TCP", "EPMS LAN", "OT");
        Node(p, "Power meter MSB-B", ScadaNodeKind.Ied, "10.20.0.102", sw, "Modbus TCP", "EPMS LAN", "OT");
        Node(p, "UPS A gateway", ScadaNodeKind.Gateway, "10.20.0.111", sw, "SNMP", "EPMS LAN", "OT");
        Node(p, "UPS B gateway", ScadaNodeKind.Gateway, "10.20.0.112", sw, "SNMP", "EPMS LAN", "OT");
        var tt = AddInstrument(p, epms, "TT-501", "Data hall cold aisle temperature", MeasuredVariable.Temperature, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, 0, 50, "°C", "Data hall 1");
        Alarms(p, tt, null, 15, 27, 32);
        Loop(p, "TI-501", "Cold aisle temperature monitoring", LoopType.Monitoring, tt, null, "-A70", null);
        Lifecycle(p, LifecyclePhase.DetailedEngineering, "Confirm IT load per hall", "Battery discharge tables from supplier", "Selectivity study 2N paths", "EPMS points list");
        var n = new PowerNetwork { Name = "DC1 2N network (demo data)" };
        n.Buses.AddRange(new[] { new Bus { Id = "MV-A", Kv = 20 }, new Bus { Id = "MV-B", Kv = 20 }, new Bus { Id = "MSB-A", Kv = 0.4 }, new Bus { Id = "MSB-B", Kv = 0.4 } });
        n.Grids.Add(new ExternalGrid { Id = "Utility A", Bus = "MV-A", SkMaxMva = 350 });
        n.Grids.Add(new ExternalGrid { Id = "Utility B", Bus = "MV-B", SkMaxMva = 350 });
        n.Transformers.Add(new Transformer { Id = "TA", HvBus = "MV-A", LvBus = "MSB-A", SnMva = 1.0, UkPercent = 6, PcuKw = 10.5 });
        n.Transformers.Add(new Transformer { Id = "TB", HvBus = "MV-B", LvBus = "MSB-B", SnMva = 1.0, UkPercent = 6, PcuKw = 10.5 });
        n.Loads.Add(new PowerLoad { Id = "UPS A + mech A", Bus = "MSB-A", PMw = 0.285, QMvar = 0.06 });
        n.Loads.Add(new PowerLoad { Id = "UPS B + mech B", Bus = "MSB-B", PMw = 0.285, QMvar = 0.06 });
        n.Generators.Add(new Generator { Id = "G-A", Bus = "MSB-A", InService = false, SnMva = 0.8, PMw = 0.3, XdSubtransientPu = 0.15, NeutralEarthed = true });
        n.Generators.Add(new Generator { Id = "G-B", Bus = "MSB-B", InService = false, SnMva = 0.8, PMw = 0.3, XdSubtransientPu = 0.15, NeutralEarthed = true });
        Network(p, n);
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-OG-009
    private ElectricalProject OilAndGas()
    {
        var (p, b) = NewProject("DEMO-OG-009", "Gas Midstream Demo Co. (fictional)", "Compressor station CS-3 – skid K-301");
        FeederSheet(b, p, "MCC-301 compressor feeders", "=MCC", "+MCC301", "MCC301", "-X50", new[]
        {
            new Feeder("Compressor K-301 lube oil pump", 5.5, 0.85, 1.0, 80, LoadCategory.Motor, true),
            new Feeder("Gas cooler fan E-301A", 22, 0.86, 1.0, 95, LoadCategory.Motor, true),
            new Feeder("Gas cooler fan E-301B", 22, 0.86, 1.0, 100, LoadCategory.Motor, true),
            new Feeder("Skid heat tracing", 12, 1.0, 0.6, 110, LoadCategory.Process, false),
        });
        var esd = b.Sheet("ESD interface", SheetKind.Control, "ESD relay outputs to MCC (logic in the cause and effect matrix)", "=ESD", "+MCC301");
        b.Spec(b.PowerSupply24V(esd, CircuitBuilder.Col(2), "L1", "N"), "DEMO-PSU-24V-10A", "Power supply 24 V DC 10 A", 129m, 50, 125, 125, "24 V DC 10 A");
        foreach (var m in p.Motors)
            if (p.FindDevice(m.DeviceId!.Value) is { } d)
            {
                d.Properties[DeviceProperties.HazardousZone] = "Zone 2";
                d.Properties[DeviceProperties.ExMarking] = "Ex ec IIA T3 Gc (demo record)";
            }
        var st = Plc(p, "-A30", "K-301 unit control panel", "172.16.30.10", "GEN-DI16", "GEN-DO8", "GEN-AI4", "GEN-AI4", "GEN-AO4");
        const string exT = "Ex db IIB T4 Gb (demo record)", exI = "Ex ia IIC T4 Ga (demo record)";
        var pt = AddInstrument(p, st, "PT-301", "Compressor suction pressure", MeasuredVariable.Pressure, InstrumentFunction.Transmitter, InstrumentSignal.Hart, 0, 40, "barg", "K-301", "Zone 1", exI, "JB-301");
        var pv = AddInstrument(p, st, "PV-301", "Anti-surge recycle valve", MeasuredVariable.Position, InstrumentFunction.ControlValve, InstrumentSignal.Hart, 0, 100, "%", "K-301", "Zone 1", exI, "JB-301");
        var lt = AddInstrument(p, st, "LT-302", "Suction scrubber level", MeasuredVariable.Level, InstrumentFunction.Transmitter, InstrumentSignal.Hart, 0, 1200, "mm", "V-302", "Zone 1", exI, "JB-302");
        var lv = AddInstrument(p, st, "LV-302", "Scrubber drain valve", MeasuredVariable.Position, InstrumentFunction.ControlValve, InstrumentSignal.Hart, 0, 100, "%", "V-302", "Zone 1", exI, "JB-302");
        var tt = AddInstrument(p, st, "TT-303", "Discharge temperature", MeasuredVariable.Temperature, InstrumentFunction.Transmitter, InstrumentSignal.Hart, 0, 200, "°C", "K-301", "Zone 1", exT, "JB-301");
        var gd = AddInstrument(p, st, "GD-310", "Skid gas detector (% LEL)", MeasuredVariable.Gas, InstrumentFunction.Detector, InstrumentSignal.Analog4To20mA, 0, 100, "%LEL", "K-301", "Zone 1", exT, "JB-303");
        var esdPb = AddInstrument(p, st, "HS-320", "Local ESD push button", MeasuredVariable.Other, InstrumentFunction.Switch, InstrumentSignal.Digital24VDC, 0, 1, "", "K-301", "Zone 1", exT, "JB-303");
        var xv = AddInstrument(p, st, "XV-305", "Suction isolation valve", MeasuredVariable.Position, InstrumentFunction.OnOffValve, InstrumentSignal.Digital24VDC, 0, 1, "", "K-301", "Zone 1", exT, "JB-301");
        var bdv = AddInstrument(p, st, "XV-306", "Blowdown valve", MeasuredVariable.Position, InstrumentFunction.OnOffValve, InstrumentSignal.Digital24VDC, 0, 1, "", "K-301", "Zone 1", exT, "JB-301");
        Loop(p, "PIC-301", "Suction pressure control (recycle)", LoopType.Pid, pt, pv, "-A30", 18, 0.8, 12);
        Loop(p, "LIC-302", "Scrubber level control", LoopType.Pid, lt, lv, "-A30", 400, 1.5, 60);
        Loop(p, "TI-303", "Discharge temperature monitoring", LoopType.Monitoring, tt, null, "-A30", null);
        Loop(p, "GI-310", "Gas detection", LoopType.Monitoring, gd, null, "-A30", null);
        Alarms(p, pt, 8, 12, 30, 34);
        Alarms(p, lt, null, 150, 800, 1000);
        Alarms(p, tt, null, null, 150, 165);
        Alarms(p, gd, null, null, 20, 40);
        var fan = p.Motors.First(m => m.Description.Contains("E-301A"));
        var lube = p.Motors.First(m => m.Description.Contains("lube"));
        var ce = new CauseEffectMatrix { Name = "K-301 ESD cause and effect (demo)", Revision = "A" };
        CauseEffectCause C(string tag, string desc, string cond, Instrument? i = null) { var c = new CauseEffectCause { Tag = tag, Description = desc, Condition = cond, InstrumentId = i?.Id }; ce.Causes.Add(c); return c; }
        CauseEffectEffect E(string tag, string desc, string action, Guid? device = null, Instrument? i = null) { var e = new CauseEffectEffect { Tag = tag, Description = desc, Action = action, DeviceId = device, InstrumentId = i?.Id }; ce.Effects.Add(e); return e; }
        var c1 = C("PT-301 LL", "Suction pressure low-low", "< 8 barg", pt);
        var c2 = C("LT-302 HH", "Scrubber level high-high", "> 1000 mm", lt);
        var c3 = C("TT-303 HH", "Discharge temperature high-high", "> 165 °C", tt);
        var c4 = C("GD-310 HH", "Gas detected", "> 40 %LEL", gd);
        var c5 = C("HS-320", "Local ESD push button", "pressed", esdPb);
        var e1 = E("K-301", "Compressor lube oil pump stop", "Stop", lube.DeviceId);
        var e2 = E("E-301A", "Cooler fan stop", "Stop", fan.DeviceId);
        var e3 = E("XV-305", "Suction isolation", "Close", i: xv);
        var e4 = E("XV-306", "Blowdown", "Open", i: bdv);
        void X(CauseEffectCause c, params CauseEffectEffect[] es) { foreach (var e in es) ce.Links.Add(new CauseEffectLink { CauseId = c.Id, EffectId = e.Id }); }
        X(c1, e1, e3); X(c2, e1, e3); X(c3, e1, e2, e3); X(c4, e1, e2, e3, e4); X(c5, e1, e2, e3, e4);
        p.CauseEffects.Add(ce);
        var fw = Node(p, "FW-CS3 Zone firewall", ScadaNodeKind.Firewall, "172.16.0.1", null, network: "Process control network", zone: "DMZ");
        var bpcs = Node(p, "SW-PCN switch", ScadaNodeKind.Switch, "172.16.30.2", fw, network: "Process control network", zone: "BPCS", redundant: true);
        Node(p, "-A30 K-301 unit control panel", ScadaNodeKind.Plc, "172.16.30.10", bpcs, "Modbus TCP / OPC UA", "Process control network", "BPCS", plc: st);
        Node(p, "Operator workstation OWS-1", ScadaNodeKind.Hmi, "172.16.30.21", bpcs, "OPC UA", "Process control network", "BPCS");
        Node(p, "Engineering workstation", ScadaNodeKind.EngineeringStation, "172.16.30.25", bpcs, "", "Process control network", "BPCS");
        var sisSw = Node(p, "SW-SIS switch", ScadaNodeKind.Switch, "172.16.40.2", fw, network: "Safety network", zone: "SIS");
        Node(p, "SIS logic solver (separate system)", ScadaNodeKind.Plc, "172.16.40.10", sisSw, "Vendor safety protocol", "Safety network", "SIS", true);
        CalculationService.Store(p, EarthingCalculator.Grid(150, 60 * 40, 900, 0.6), "Skid earthing grid (demo soil data)");
        CalculationService.Store(p, EarthingCalculator.TolerableVoltages(150, 3000, 0.1, 0.5, 70), "Tolerable touch/step voltages (demo)");
        Lifecycle(p, LifecyclePhase.DetailedEngineering, "Hazardous area classification review", "Ex equipment register", "C&E review with process", "Loop check procedures");
        p.Issues.Add(new EngineeringIssue { Number = 1, Title = "Confirm gas group for cooler fans", Description = "Area classification drawing rev B pending.", Severity = IssueSeverity.High, RaisedBy = "D&D Demo Engineer" });
        var n = new PowerNetwork { Name = "CS-3 MCC network (demo data)" };
        n.Buses.AddRange(new[] { new Bus { Id = "MV", Kv = 6.6 }, new Bus { Id = "MCC301", Kv = 0.4 } });
        n.Grids.Add(new ExternalGrid { Id = "Station 6.6 kV board", Bus = "MV", SkMaxMva = 150, SkMinMva = 100 });
        n.Transformers.Add(new Transformer { Id = "T-301", HvBus = "MV", LvBus = "MCC301", SnMva = 0.4, UkPercent = 4.5, PcuKw = 4.6 });
        n.Loads.Add(new PowerLoad { Id = "MCC-301", Bus = "MCC301", PMw = 0.06, QMvar = 0.035 });
        Network(p, n);
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-PV-010
    private ElectricalProject SolarPlant()
    {
        var (p, b) = NewProject("DEMO-PV-010", "Sunfield Energy Demo (fictional)", "PV plant PV-10 – 2.5 MWp");
        FeederSheet(b, p, "Inverter AC collection board", "=GRID", "+ACB", "ACB", "-X60", new[]
        {
            new Feeder("Inverter INV-01 (AC output)", 110, 1.0, 1.0, 120, LoadCategory.Other, false),
            new Feeder("Inverter INV-02 (AC output)", 110, 1.0, 1.0, 140, LoadCategory.Other, false),
            new Feeder("Inverter INV-03 (AC output)", 110, 1.0, 1.0, 160, LoadCategory.Other, false),
            new Feeder("Auxiliary services", 15, 0.9, 0.6, 30, LoadCategory.Other, false),
        });
        b.Sheet("PV array and strings", SheetKind.Power, "Strings per MPPT (see string sizing calculation)", "=PV", "+ARRAY");
        CalculationService.Store(p, PvStringCalculator.Calculate(49.5, 41.6, 13.9, -0.25, -0.30, -10, 70, 1100, 200, 1000, 30, 20, 2), "String sizing (illustrative module and inverter data)");
        CalculationService.Store(p, TransformerSizingCalculator.Calculate(2300, 1.0, 1.0, 10), "Step-up transformer sizing");
        var plc = Plc(p, "-A90", "Power plant controller I/O", "10.90.0.10", "GEN-DI16", "GEN-AI4", "GEN-AO4");
        var irr = AddInstrument(p, plc, "RT-901", "Plane-of-array irradiance (pyranometer)", MeasuredVariable.Other, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, 0, 1600, "W/m²", "Weather station");
        var mt = AddInstrument(p, plc, "TT-902", "Module back-surface temperature", MeasuredVariable.Temperature, InstrumentFunction.Transmitter, InstrumentSignal.Analog4To20mA, -40, 100, "°C", "Weather station");
        Loop(p, "RI-901", "Irradiance monitoring", LoopType.Monitoring, irr, null, "PPC", null);
        Loop(p, "TI-902", "Module temperature monitoring", LoopType.Monitoring, mt, null, "PPC", null);
        Alarms(p, mt, null, null, 75, 85);
        var rtu = Node(p, "RTU – grid operator link", ScadaNodeKind.Rtu, "10.90.0.2", null, "IEC 60870-5-104", "Plant LAN", "Grid interface");
        var sw = Node(p, "SW-PV core switch", ScadaNodeKind.Switch, "10.90.0.3", rtu, network: "Plant LAN", zone: "Plant", redundant: true);
        Node(p, "PPC power plant controller", ScadaNodeKind.Plc, "10.90.0.10", sw, "Modbus TCP", "Plant LAN", "Plant", plc: plc);
        Node(p, "Plant SCADA server", ScadaNodeKind.ScadaServer, "10.90.0.20", sw, "Modbus TCP / OPC UA", "Plant LAN", "Plant");
        for (var i = 1; i <= 3; i++) Node(p, $"INV-0{i} inverter", ScadaNodeKind.Drive, $"10.90.1.{i}", sw, "Modbus TCP", "Plant LAN", "Plant");
        Node(p, "Revenue meter", ScadaNodeKind.Ied, "10.90.0.30", sw, "Modbus TCP", "Plant LAN", "Plant");
        Node(p, "Weather station logger", ScadaNodeKind.Sensor, "10.90.0.40", sw, "Modbus TCP", "Plant LAN", "Plant");
        Lifecycle(p, LifecyclePhase.Construction, "String layout per inverter", "Grid code compliance documents", "Commissioning test sheets", "SCADA point list to grid operator");
        var n = new PowerNetwork { Name = "PV-10 grid connection (demo data)" };
        n.Buses.AddRange(new[] { new Bus { Id = "POC", Name = "Point of connection 20 kV", Kv = 20 }, new Bus { Id = "ACB", Name = "Inverter AC board", Kv = 0.8, VmaxPu = 1.1 } });
        n.Grids.Add(new ExternalGrid { Id = "Distribution grid", Bus = "POC", SkMaxMva = 120, SkMinMva = 60, RxRatio = 0.3 });
        n.Transformers.Add(new Transformer { Id = "TX-01", HvBus = "POC", LvBus = "ACB", SnMva = 2.5, UkPercent = 6, PcuKw = 22, VectorGroup = "Dyn11" });
        n.Loads.Add(new PowerLoad { Id = "PV inverters (export, unity pf)", Bus = "ACB", PMw = -2.2, QMvar = 0 });
        n.Loads.Add(new PowerLoad { Id = "Auxiliaries", Bus = "ACB", PMw = 0.009, QMvar = 0.004 });
        Network(p, n);
        Finish(p);
        return p;
    }
}
