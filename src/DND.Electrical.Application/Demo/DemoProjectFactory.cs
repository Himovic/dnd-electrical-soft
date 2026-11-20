using DND.Electrical.Application.Services;
using DND.Electrical.Application.Templates;
using DND.Electrical.Calculations;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.PLC;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;
using DND.Electrical.Wiring;

namespace DND.Electrical.Application.Demo;

public sealed record DemoProjectInfo(string Number, string Name, string Description, ProjectIndustry Industry);

/// <summary>
/// Generates the bundled demonstration projects. All data (customers, products, prices) is fictional and
/// flagged as such; projects are marked <see cref="ProjectInfo.IsDemo"/> and title blocks show a banner.
/// </summary>
public sealed class DemoProjectFactory(SchematicService schematic)
{
    public static readonly IReadOnlyList<DemoProjectInfo> Catalogue = new[]
    {
        new DemoProjectInfo("DEMO-IND-001", "Industrial Motor Control", "Three-phase motor control installation (DOL starter with E-stop, signalling and cabinet).", ProjectIndustry.Industrial),
        new DemoProjectInfo("DEMO-AUT-002", "Industrial Automation", "PLC-controlled conveyor and pump skid with sensors, valves, VFD and analog instrumentation.", ProjectIndustry.Automation),
        new DemoProjectInfo("DEMO-PWR-003", "Electrical Distribution", "1000 kVA transformer, main distribution board and outgoing feeders with sizing calculations.", ProjectIndustry.PowerDistribution),
        new DemoProjectInfo("DEMO-CAB-004", "Control Cabinet", "Motor control cabinet with three starters, PLC and complete mounting-plate layout.", ProjectIndustry.Manufacturing),
        new DemoProjectInfo("DEMO-BLD-005", "Building Electrical Installation", "Residential/commercial distribution board with lighting and socket circuits.", ProjectIndustry.Building),
    };

    public ElectricalProject Create(string number) => number switch
    {
        "DEMO-IND-001" => IndustrialMotorControl(),
        "DEMO-AUT-002" => IndustrialAutomation(),
        "DEMO-PWR-003" => ElectricalDistribution(),
        "DEMO-CAB-004" => ControlCabinet(),
        "DEMO-BLD-005" => BuildingInstallation(),
        _ => throw new ArgumentException($"Unknown demo project '{number}'."),
    };

    public IEnumerable<ElectricalProject> CreateAll() => Catalogue.Select(c => Create(c.Number));

    private (ElectricalProject p, CircuitBuilder b) NewProject(string number, string customer, string site)
    {
        var meta = Catalogue.First(c => c.Number == number);
        var p = new ElectricalProject
        {
            Info = new ProjectInfo
            {
                Number = number, Name = meta.Name, Description = meta.Description, Industry = meta.Industry, IsDemo = true,
                CustomerName = customer, CustomerAddress = "1 Example Road, Demo City (fictional)", CustomerContact = "demo contact (fictional)",
                CompanyName = "D&D | Data&Development", CompanyAddress = "Demonstration data", Engineer = "D&D Demo Engineer",
                CheckedBy = "Reviewer (demo)", ApprovedBy = "Approver (demo)", Site = site, Reference = $"REF-{number[5..]}",
                Status = ProjectStatus.InProgress, CurrentRevision = "A", TemplateId = "demo",
            },
        };
        p.Revisions.Add(new ProjectRevision { Code = "A", Description = "Demonstration issue", Author = "D&D Demo Engineer", Status = RevisionStatus.Issued });
        var b = new CircuitBuilder(p, schematic);
        ProjectTemplate.CoverSheet(b);
        return (p, b);
    }

    private void Finish(ElectricalProject p)
    {
        schematic.Binder.BindProject(p);
        WireNumberingService.Apply(p, ConnectivityGraph.Build(p));
        CableService.SyncTerminalStrips(p);
        schematic.Binder.BindProject(p);
        foreach (var c in p.Cabinets) CabinetLayoutService.SyncNames(p, c);
        p.Audit("Demo", "Demonstration project generated (fictional data).", "system");
    }

    private static readonly string[] Main = { "1L1", "1L2", "1L3" };

    /// <summary>Main switch: mains L1/L2/L3 → -Q0 → named outgoing potentials 1L1/1L2/1L3.</summary>
    private static SymbolInstanceEntity MainSwitch(CircuitBuilder b, Sheet sheet, double x, string symbol = "IEC.DISC.3P")
    {
        var l1 = b.Supply(sheet, "L1", x, 270); var l2 = b.Supply(sheet, "L2", x + 10, 270); var l3 = b.Supply(sheet, "L3", x + 20, 270);
        var q0 = b.Place(sheet, symbol, x, 230, "-Q0", "Main switch");
        b.Wire(sheet, l1, "1", q0, "1"); b.Wire(sheet, l2, "1", q0, "3"); b.Wire(sheet, l3, "1", q0, "5");
        for (int i = 0; i < 3; i++)
        {
            var r = b.Return(sheet, Main[i], x + i * 10, 190);
            b.Wire(sheet, q0, (2 + 2 * i).ToString(), r, "1");
        }
        return q0;
    }

    // ===================================================================== DEMO-IND-001

    private ElectricalProject IndustrialMotorControl()
    {
        var (p, b) = NewProject("DEMO-IND-001", "Industrial Manufacturing Demo", "Plant A – pump house (fictional)");
        var power = b.Sheet("Power circuit", SheetKind.Power, "Main switch and DOL motor feeder", "=PUMP", "+CAB1");
        var q0 = MainSwitch(b, power, CircuitBuilder.Col(1));
        b.Spec(q0, "DEMO-DISC-3P-40A", "Switch-disconnector 3P 40 A, door handle", 68.50m, 90, 90, 80, "40 A");
        var feeder = b.DolFeeder(power, CircuitBuilder.Col(4), "Process pump", "-X1", 1, phases: Main);
        b.Spec(feeder.Breaker, "DEMO-MPCB-16A", "Motor protection breaker 10–16 A", 54.90m, 45, 90, 80, "10–16 A, set 14.5 A");
        b.Spec(feeder.Contactor, "DEMO-CONT-18A-24DC", "Contactor 18 A AC-3, coil 24 V DC, 1NO aux", 38.20m, 45, 82, 90, "18 A AC-3");
        b.Spec(feeder.Overload!, "DEMO-OLR-12-18A", "Thermal overload relay 12–18 A, 1NO+1NC", 31.40m, 45, 75, 80, "12–18 A");
        b.Spec(feeder.Motor, "DEMO-MOT-7K5-4P", "Three-phase motor 7.5 kW 4-pole IE3", 690.00m, 0, 0, 0, "7.5 kW");
        p.FindTerminalStrip("-X1");
        b.Device(feeder.Terminals[0]).Function = "Field terminals";

        var supply = b.Sheet("Control supply", SheetKind.Control, "230 V AC / 24 V DC power supply", "=PUMP", "+CAB1");
        var g1 = b.PowerSupply24V(supply, CircuitBuilder.Col(2), "1L1", "N");
        b.Spec(g1, "DEMO-PSU-24V-5A", "Power supply 230 V AC / 24 V DC 5 A", 89.00m, 40, 90, 110, "24 V DC 5 A");
        var f2 = supply.Drawing.Entities.OfType<SymbolInstanceEntity>().First(i => i.SymbolId == "IEC.CB.1P");
        b.Spec(f2, "DEMO-MCB-1P-C6", "Miniature circuit breaker 1P C6", 9.80m, 18, 90, 70, "C6");
        var nSupply = b.Supply(power, "N", CircuitBuilder.Col(1) + 30, 270);
        var nReturn = b.Return(power, "N", CircuitBuilder.Col(1) + 30, 190);
        b.Wire(power, nSupply, "1", nReturn, "1");

        var control = b.Sheet("Control circuit", SheetKind.Control, "Start/stop with emergency stop and signalling", "=PUMP", "+CAB1");
        var kTag = b.Device(feeder.Contactor).Tag;
        var fTag = b.Device(feeder.Overload!).Tag;
        var (coil, start, stop) = b.StartStopRung(control, CircuitBuilder.Col(2), kTag, fTag, "Start pump", "Stop pump", "-S0");
        b.Device(coil).Function = "Pump contactor";
        b.Spec(start, "DEMO-PB-GN-1NO", "Push button green, 1NO", 12.40m, 30, 30, 45);
        b.Spec(stop, "DEMO-PB-RD-1NC", "Push button red, 1NC", 12.40m, 30, 30, 45);
        var s0 = control.Drawing.Entities.OfType<SymbolInstanceEntity>().First(i => i.SymbolId == "IEC.ESTOP");
        b.Spec(s0, "DEMO-ESTOP-40", "Emergency stop mushroom 40 mm, 1NC positive opening", 24.60m, 40, 40, 60);
        var h1 = b.LampRung(control, CircuitBuilder.Col(5), kTag, true, "23", "24", "Pump running");
        var h2 = b.LampRung(control, CircuitBuilder.Col(6), fTag, true, "97", "98", "Overload tripped");
        b.Spec(h1, "DEMO-LAMP-GN-24", "Indicator lamp green 24 V LED", 9.90m, 30, 30, 40);
        b.Spec(h2, "DEMO-LAMP-RD-24", "Indicator lamp red 24 V LED", 9.90m, 30, 30, 40);

        var motor = new Motor
        {
            DeviceId = feeder.Motor.DeviceId, Tag = b.Device(feeder.Motor).Tag, Description = "Process pump motor", PowerKw = 7.5, VoltageV = 400,
            PowerFactor = 0.84, Efficiency = 0.905, SpeedRpm = 1460, StartingMethod = MotorStartingMethod.DirectOnLine, StartingCurrentRatio = 7.2,
            Manufacturer = CircuitBuilder.DemoManufacturer, Model = "DM-132M-4 (fictional)", PartNumber = "DEMO-MOT-7K5-4P",
            ProtectionDeviceTag = b.Device(feeder.Breaker).Tag, CabinetTag = "+CAB1",
        };
        motor.RatedCurrentA = Math.Round(PowerCalculator.CurrentFromPower(SupplySystem.ThreePhase, 7.5, 400, 0.84, 0.905).Output("Current I"), 2);
        p.Motors.Add(motor);

        var w1 = CableService.CreateCable(p, "H07RN-F", 4, 2.5, 25, "+CAB1 -X1", "-M1 pump");
        w1.Route = "Cable tray T1";
        w1.PartNumber = "DEMO-CBL-4G2.5";
        b.Product("DEMO-CBL-4G2.5", "Rubber cable H07RN-F 4G2.5 (per m)", 2.85m, category: "Cables");
        foreach (var w in feeder.MotorWires) CableService.AssignWire(w1, w);
        foreach (var t in feeder.Terminals) b.Device(t).PartNumber = "DEMO-TB-2.5";
        b.Product("DEMO-TB-2.5", "Feed-through terminal 2.5 mm²", 0.95m, 5.2, 45, 40, "Terminals");
        p.TerminalStrips.Add(new TerminalStrip { Tag = "-X1", Location = "+CAB1", Description = "Field terminals pump", TerminalPartNumber = "DEMO-TB-2.5" });

        // Calculations
        CalculationService.Store(p, PowerCalculator.CurrentFromPower(SupplySystem.ThreePhase, 7.5, 400, 0.84, 0.905), "Pump motor current");
        CalculationService.Store(p, CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, motor.RatedCurrentA, 25, InstallationMethod.E, CableInsulation.PVC, 35, 2, 0.84, 5, DeviceRatingA: 16)), "Cable -W1 sizing", w1.Id);
        CalculationService.Store(p, VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.ThreePhase, 400, motor.RatedCurrentA, 25, 2.5, PowerFactor: 0.84)), "Voltage drop -W1");
        CalculationService.Store(p, MotorStartingCalculator.Calculate(7.5, 400, 0.84, 0.905, 7.2, StartingMethod.DirectOnLine, transformerKva: 400, transformerUk: 4), "Motor starting -M1");

        // Cabinet
        var cab = new Cabinet { Tag = "+CAB1", Name = "Pump control panel", Width = 600, Height = 800, Depth = 250, PlateWidth = 500, PlateHeight = 700, EnclosurePartNumber = "DEMO-ENC-600-800", ClearanceMm = 0 };
        b.Product("DEMO-ENC-600-800", "Steel enclosure 600×800×250 IP55 with mounting plate", 245.00m, 600, 800, 250, "Enclosures");
        p.Cabinets.Add(cab);
        CabinetLayoutService.AddDuct(cab, 10, 620, 480, 60);
        CabinetLayoutService.AddDuct(cab, 10, 410, 480, 60);
        CabinetLayoutService.AddDuct(cab, 10, 190, 480, 60);
        var rail1 = CabinetLayoutService.AddRail(cab, 20, 505, 460);
        var rail2 = CabinetLayoutService.AddRail(cab, 20, 290, 460);
        var rail3 = CabinetLayoutService.AddRail(cab, 20, 80, 460);
        PlaceDevice(p, cab, rail1, feeder.Breaker, 5);
        PlaceDevice(p, cab, rail1, q0, 5);
        PlaceDevice(p, cab, rail1, f2, 5);
        PlaceDevice(p, cab, rail2, feeder.Contactor, 5);
        PlaceDevice(p, cab, rail2, feeder.Overload!, 5);
        PlaceDevice(p, cab, rail2, g1, 5);
        CabinetLayoutService.PlaceOnRail(cab, rail3, new CabinetItem { Kind = CabinetItemKind.TerminalStrip, Name = "-X1", Width = 4 * 5.2, Height = 45, Depth = 40, PartNumber = "DEMO-TB-2.5" });
        // Door-mounted pilot devices in one row, 60 mm pitch (22 mm units with 30 mm bezels; fictional demo dimensions).
        var doorX = 120.0;
        foreach (var lamp in new[] { h1, h2, start, stop, s0 })
        {
            cab.Items.Add(new CabinetItem { Kind = CabinetItemKind.Device, Face = CabinetFace.Door, DeviceId = lamp.DeviceId, Width = 30, Height = 30, Depth = 45, X = doorX, Y = 600 });
            doorX += 60;
        }

        Finish(p);
        return p;
    }

    private static void PlaceDevice(ElectricalProject p, Cabinet cab, CabinetItem rail, SymbolInstanceEntity inst, double gap = 0)
    {
        var dev = p.FindDevice(inst.DeviceId!.Value)!;
        var prod = p.FindProduct(dev.Manufacturer, dev.PartNumber);
        var item = new CabinetItem
        {
            Kind = CabinetItemKind.Device, DeviceId = dev.Id, Name = dev.Tag, PartNumber = dev.PartNumber,
            Width = prod?.WidthMm is > 0 ? prod.WidthMm : 45, Height = prod?.HeightMm is > 0 ? prod.HeightMm : 85, Depth = prod?.DepthMm is > 0 ? prod.DepthMm : 80,
        };
        if (!CabinetLayoutService.PlaceOnRail(cab, rail, item, gap)) throw new InvalidOperationException($"Demo layout: {dev.Tag} does not fit on the rail.");
    }

    // ===================================================================== DEMO-AUT-002

    private ElectricalProject IndustrialAutomation()
    {
        var (p, b) = NewProject("DEMO-AUT-002", "Automated Process Demo Ltd (fictional)", "Line 2 – conveyor & dosing skid");
        p.Settings.WireNumberFormat = "{SHEET}{COL}{SEQ:00}";
        var st = PlcService.CreateStation(p, "-A1", "Line 2 controller", "siemens-style");
        st.Description = "Generic PLC (vendor-neutral demo profile)";
        var rack = st.Racks[0];
        PlcService.AddModule(st, rack, "GEN-CPU");
        var di = PlcService.AddModule(st, rack, "GEN-DI16");
        var dq = PlcService.AddModule(st, rack, "GEN-DO8");
        var ai = PlcService.AddModule(st, rack, "GEN-AI4");
        var ao = PlcService.AddModule(st, rack, "GEN-AO4");

        var power = b.Sheet("Power circuits", SheetKind.Power, "Main breaker, conveyor DOL and pump VFD", "=LINE2", "+CP2");
        MainSwitch(b, power, CircuitBuilder.Col(1), "IEC.CB.3P");
        var conv = b.DolFeeder(power, CircuitBuilder.Col(3), "Conveyor drive", "-X1", 1, phases: Main);
        b.Spec(conv.Motor, "DEMO-MOT-2K2-4P", "Three-phase motor 2.2 kW", 310.00m, rating: "2.2 kW");
        b.Spec(conv.Breaker, "DEMO-MPCB-6A", "Motor protection breaker 4–6.3 A", 49.90m, 45, 90, 80, "4–6.3 A");
        b.Spec(conv.Contactor, "DEMO-CONT-9A-24DC", "Contactor 9 A AC-3, coil 24 V DC", 29.50m, 45, 82, 90, "9 A");
        b.Spec(conv.Overload!, "DEMO-OLR-4-6A", "Thermal overload relay 4–6 A", 29.00m, 45, 75, 80, "4–6 A");

        // Pump on VFD
        var x = CircuitBuilder.Col(6);
        var s1 = b.Supply(power, Main[0], x, 270); var s2 = b.Supply(power, Main[1], x + 10, 270); var s3 = b.Supply(power, Main[2], x + 20, 270);
        var q2 = b.Place(power, "IEC.CB.3P", x, 240, function: "VFD protection");
        b.Wire(power, s1, "1", q2, "1"); b.Wire(power, s2, "1", q2, "3"); b.Wire(power, s3, "1", q2, "5");
        var vfd = b.Place(power, "IEC.VFD", x, 195, function: "Dosing pump drive");
        b.Wire(power, q2, "2", vfd, "L1"); b.Wire(power, q2, "4", vfd, "L2"); b.Wire(power, q2, "6", vfd, "L3");
        var m2 = b.Place(power, "IEC.MOTOR.3PH", x, 120, function: "Dosing pump");
        b.Wire(power, vfd, "U", m2, "U1"); b.Wire(power, vfd, "V", m2, "V1"); b.Wire(power, vfd, "W", m2, "W1");
        var pe = b.Supply(power, "PE", x + 40, 270);
        b.Wire(power, pe, "1", m2, "PE", new Point2(x + 40, 140), new Point2(x + 30, 140));
        var irRun = b.Place(power, "IEC.INTERRUPT", x + 55, 200, rotation: 270, values: new Dictionary<string, string> { [SymbolAttributes.Name] = "VFD_RUN" });
        var irRef = b.Place(power, "IEC.INTERRUPT", x + 55, 185, rotation: 270, values: new Dictionary<string, string> { [SymbolAttributes.Name] = "SPEED_REF" });
        b.Wire(power, vfd, "DI1", irRun, "1", new Point2(x + 35, 200));
        b.Wire(power, vfd, "AI1", irRef, "1", new Point2(x + 40, 195), new Point2(x + 40, 185));
        var com = b.Place(power, "IEC.SUPPLY", x + 60, 170, rotation: 270, values: new Dictionary<string, string> { [SymbolAttributes.Potential] = "0V" });
        b.Wire(power, vfd, "COM", com, "1", new Point2(x + 35, 190), new Point2(x + 35, 170));
        b.Spec(vfd, "DEMO-VFD-4K0", "Frequency converter 4 kW 400 V", 520.00m, 90, 230, 180, "4 kW");
        b.Spec(m2, "DEMO-MOT-3K0-4P", "Three-phase motor 3 kW", 385.00m, rating: "3 kW");
        b.Spec(q2, "DEMO-MCB-3P-C16", "Miniature circuit breaker 3P C16", 34.00m, 54, 90, 70, "C16");

        var supply = b.Sheet("Control supply", SheetKind.Control, "24 V DC supply", "=LINE2", "+CP2");
        var g1 = b.PowerSupply24V(supply, CircuitBuilder.Col(2), "1L1", "N");
        b.Spec(g1, "DEMO-PSU-24V-10A", "Power supply 24 V DC 10 A", 129.00m, 50, 125, 125, "24 V DC 10 A");
        var nS = b.Supply(power, "N", CircuitBuilder.Col(1) + 30, 270); var nR = b.Return(power, "N", CircuitBuilder.Col(1) + 30, 190);
        b.Wire(power, nS, "1", nR, "1");

        // Digital inputs
        var inputs = b.Sheet("PLC digital inputs", SheetKind.Plc, "Field sensors and operator devices → DI module slot 1", "=LINE2", "+CP2");
        var diDefs = new (string symbol, string function, string plcTag, bool threeWire)[]
        {
            ("IEC.PB.NO", "Start line", "Start_PB", false),
            ("IEC.PB.NC", "Stop line", "Stop_PB", false),
            ("IEC.PROX", "Conveyor part present", "Part_Present", true),
            ("IEC.PHOTO", "Conveyor end position", "End_Position", true),
            ("IEC.LEVEL", "Dosing tank low level", "Tank_Low", false),
            ("IEC.ESTOP", "Emergency stop monitoring", "EStop_OK", false),
        };
        for (int i = 0; i < diDefs.Length; i++)
        {
            var (sym, function, plcTag, threeWire) = diDefs[i];
            var cx = CircuitBuilder.Col(i + 1) + (threeWire ? 5 : 0);
            var dev = b.Place(inputs, sym, cx, 230, function: function);
            if (threeWire)
            {
                var plus = b.Supply(inputs, "+24V", cx - 5, 270); var zero = b.Supply(inputs, "0V", cx + 5, 270);
                b.Wire(inputs, plus, "1", dev, "BN"); b.Wire(inputs, zero, "1", dev, "BU");
            }
            else
            {
                var plus = b.Supply(inputs, "+24V", cx, 270);
                b.Wire(inputs, plus, "1", dev, dev.Definition!.Pins[0].Id);
            }
            var outPin = threeWire ? "BK" : dev.Definition!.Pins[1].Id;
            var term = b.Terminal(inputs, "-X2", (i + 1).ToString(), cx, 180);
            var ch = b.Place(inputs, "IEC.PLC.DI", cx, 140, "-A1");
            b.Wire(inputs, dev, outPin, term, "1");
            b.Wire(inputs, term, "2", ch, "IN");
            var channel = di.Channels[i];
            channel.Tag = plcTag; channel.Description = function; channel.LinkedDeviceId = dev.DeviceId; channel.TerminalReference = $"-X2:{i + 1}";
            PlcService.LinkSymbol(p, channel, ch);
            b.Spec(dev, $"DEMO-{sym[4..].Replace('.', '-')}", $"{dev.Definition!.Name} (demo)", threeWire ? 64.00m : 18.00m, 30, 60, 50);
        }
        b.Device(inputs.Drawing.Entities.OfType<SymbolInstanceEntity>().First(e => e.SymbolId == "IEC.PLC.DI")).PartNumber = "GEN-DI16";

        // Digital outputs
        var outputs = b.Sheet("PLC digital outputs", SheetKind.Plc, "DO module slot 2 → actuators", "=LINE2", "+CP2");
        var convTag = b.Device(conv.Contactor).Tag;
        var doDefs = new (string? symbol, string? tag, string function, string plcTag, string? interruption)[]
        {
            ("IEC.COIL", convTag, "Conveyor contactor", "Conveyor_Run", null),
            ("IEC.SOL.COIL", null, "Dosing valve", "Dosing_Valve", null),
            ("IEC.LAMP", null, "Line running lamp", "Lamp_Running", null),
            (null, null, "Pump drive run command", "Pump_Run", "VFD_RUN"),
        };
        for (int i = 0; i < doDefs.Length; i++)
        {
            var (sym, tag, function, plcTag, ir) = doDefs[i];
            var cx = CircuitBuilder.Col(i + 1);
            var ch = b.Place(outputs, "IEC.PLC.DO", cx, 235, "-A1");
            var term = b.Terminal(outputs, "-X3", (i + 1).ToString(), cx, 195);
            b.Wire(outputs, ch, "OUT", term, "1");
            var channel = dq.Channels[i];
            channel.Tag = plcTag; channel.Description = function; channel.TerminalReference = $"-X3:{i + 1}";
            PlcService.LinkSymbol(p, channel, ch);
            if (ir is not null)
            {
                var irp = b.Interruption(outputs, ir, cx, 160, pointingDown: true);
                b.Wire(outputs, term, "2", irp, "1");
                continue;
            }
            var load = b.Place(outputs, sym!, cx, 150, tag, function);
            b.Wire(outputs, term, "2", load, "A1".Equals(load.Definition!.Pins[0].Id) ? "A1" : load.Definition.Pins[0].Id);
            var zero = b.Return(outputs, "0V", cx, 110);
            b.Wire(outputs, load, load.Definition.Pins[1].Id, zero, "1");
            channel.LinkedDeviceId = load.DeviceId;
            if (sym == "IEC.SOL.COIL") b.Spec(load, "DEMO-SOV-24DC", "Solenoid valve coil 24 V DC 4 W", 42.00m, 0, 0, 0);
            if (sym == "IEC.LAMP") b.Spec(load, "DEMO-LAMP-WH-24", "Indicator lamp white 24 V LED", 9.90m, 30, 30, 40);
        }

        // Analog
        var analog = b.Sheet("PLC analog I/O", SheetKind.Plc, "4-20 mA instrumentation and speed reference", "=LINE2", "+CP2");
        var aiDefs = new (string symbol, string function, string plcTag)[]
        {
            ("IEC.PRESS", "Dosing line pressure 0–10 bar", "PT201_Pressure"),
            ("IEC.TEMP", "Tank temperature 0–100 °C", "TT202_Temperature"),
        };
        for (int i = 0; i < aiDefs.Length; i++)
        {
            var (sym, function, plcTag) = aiDefs[i];
            var cx = CircuitBuilder.Col(i + 1);
            var plus = b.Supply(analog, "+24V", cx, 270);
            var tx = b.Place(analog, sym, cx, 235, function: function);
            var term = b.Terminal(analog, "-X4", (i + 1).ToString(), cx, 195);
            var ch = b.Place(analog, "IEC.PLC.AI", cx, 155, "-A1");
            b.Wire(analog, plus, "1", tx, "+");
            b.Wire(analog, tx, "-", term, "1");
            b.Wire(analog, term, "2", ch, "IN");
            var channel = ai.Channels[i];
            channel.Tag = plcTag; channel.Description = function; channel.LinkedDeviceId = tx.DeviceId; channel.TerminalReference = $"-X4:{i + 1}";
            PlcService.LinkSymbol(p, channel, ch);
            b.Spec(tx, $"DEMO-{sym[4..]}-TX", $"{function} transmitter 4-20 mA (demo)", 185.00m);
        }
        var aoCh = b.Place(analog, "IEC.PLC.AO", CircuitBuilder.Col(4), 235, "-A1");
        var aoIr = b.Interruption(analog, "SPEED_REF", CircuitBuilder.Col(4), 200, pointingDown: true);
        b.Wire(analog, aoCh, "OUT", aoIr, "1");
        ao.Channels[0].Tag = "Pump_Speed_SP"; ao.Channels[0].Description = "Dosing pump speed reference 4-20 mA"; ao.Channels[0].SignalType = "4-20 mA";
        ao.Channels[0].LinkedDeviceId = vfd.DeviceId;
        PlcService.LinkSymbol(p, ao.Channels[0], aoCh);
        b.Device(aoCh).Function = "Line 2 PLC";
        b.Device(aoCh).PartNumber = "GEN-CPU";
        b.Device(aoCh).Manufacturer = "Generic (profile)";

        p.Motors.Add(new Motor { DeviceId = conv.Motor.DeviceId, Tag = b.Device(conv.Motor).Tag, Description = "Conveyor drive", PowerKw = 2.2, VoltageV = 400, PowerFactor = 0.8, Efficiency = 0.86, RatedCurrentA = 4.6, SpeedRpm = 1430, StartingMethod = MotorStartingMethod.DirectOnLine, StartingCurrentRatio = 6.5, ProtectionDeviceTag = b.Device(conv.Breaker).Tag });
        p.Motors.Add(new Motor { DeviceId = m2.DeviceId, Tag = b.Device(m2).Tag, Description = "Dosing pump", PowerKw = 3.0, VoltageV = 400, PowerFactor = 0.82, Efficiency = 0.875, RatedCurrentA = 6.0, SpeedRpm = 1440, StartingMethod = MotorStartingMethod.VariableFrequencyDrive, StartingCurrentRatio = 7, ProtectionDeviceTag = b.Device(q2).Tag });
        var cm = CableService.CreateCable(p, "H07RN-F", 4, 1.5, 18, "+CP2 -X1", "Conveyor motor");
        foreach (var w in conv.MotorWires) CableService.AssignWire(cm, w);
        var cs = CableService.CreateCable(p, "LIYCY", 7, 0.75, 30, "+CP2 -X2", "Field junction box JB1");
        foreach (var w in inputs.Drawing.Entities.OfType<WireEntity>().Where(w => w.End.InstanceId is { } id && inputs.Drawing.Find(id) is SymbolInstanceEntity si && si.SymbolId == "IEC.TERMINAL").Take(6))
            CableService.AssignWire(cs, w);
        cs.Shielded = true;
        CalculationService.Store(p, LoadCalculator.Summarize(p.Motors.Select(m => new LoadItem(m.Tag, m.PowerKw / m.Efficiency, m.PowerFactor, 1, Category: "Motor")).Append(new LoadItem("Control supply", 0.3, 0.95)).ToList(), 400), "Line 2 load summary");
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-PWR-003

    private ElectricalProject ElectricalDistribution()
    {
        var (p, b) = NewProject("DEMO-PWR-003", "Regional Logistics Centre (fictional)", "Warehouse 3 – main substation");
        var mdb = b.Sheet("Transformer and main breaker", SheetKind.Power, "20 kV / 400 V transformer and incoming breaker", "=MDB", "+MDB");
        var x = CircuitBuilder.Col(2);
        var hv1 = b.Supply(mdb, "HV-L1", x, 280); var hv2 = b.Supply(mdb, "HV-L2", x + 10, 285); var hv3 = b.Supply(mdb, "HV-L3", x + 20, 280);
        var t1 = b.Place(mdb, "IEC.TR.3PH", x, 255, "-T1", "Distribution transformer 1000 kVA Dyn11");
        b.Wire(mdb, hv1, "1", t1, "1U"); b.Wire(mdb, hv2, "1", t1, "1V"); b.Wire(mdb, hv3, "1", t1, "1W");
        var q0 = b.Place(mdb, "IEC.CB.3P", x, 205, "-Q0", "Incoming main breaker 1600 A");
        b.Wire(mdb, t1, "2U", q0, "1"); b.Wire(mdb, t1, "2V", q0, "3"); b.Wire(mdb, t1, "2W", q0, "5");
        for (int i = 0; i < 3; i++) b.Wire(mdb, q0, (2 + 2 * i).ToString(), b.Return(mdb, new[] { "L1", "L2", "L3" }[i], x + i * 10, 165), "1");
        b.Spec(t1, "DEMO-TR-1000KVA", "Oil transformer 1000 kVA 20/0.4 kV Dyn11 uk 6 %", 18500.00m, rating: "1000 kVA");
        b.Spec(q0, "DEMO-ACB-1600", "Air circuit breaker 1600 A 3P 65 kA", 6200.00m, 400, 400, 300, "1600 A");
        b.Device(t1).Properties["uk"] = "6 %";
        b.Device(t1).Properties["Pk"] = "10.5 kW";

        var feeders = b.Sheet("Outgoing feeders", SheetKind.Power, "Main distribution board outgoing circuits", "=MDB", "+MDB");
        var loads = new (string name, double kw, double pf, double ku, double length, LoadCategory cat, bool motor)[]
        {
            ("Sprinkler pump", 45, 0.86, 1.0, 60, LoadCategory.Motor, true),
            ("Chiller compressor", 110, 0.88, 0.9, 85, LoadCategory.Hvac, true),
            ("Dock levellers board", 30, 0.8, 0.6, 140, LoadCategory.Process, false),
            ("Lighting board LB1", 48, 0.95, 0.9, 120, LoadCategory.Lighting, false),
            ("Office board OB1", 60, 0.9, 0.7, 95, LoadCategory.Sockets, false),
            ("Charging stations", 90, 0.98, 0.8, 70, LoadCategory.Other, false),
        };
        for (int i = 0; i < loads.Length; i++)
        {
            var l = loads[i];
            var cx = CircuitBuilder.Col(1) + i * 65;
            var a = b.Supply(feeders, "L1", cx, 270); var bb = b.Supply(feeders, "L2", cx + 10, 270); var c = b.Supply(feeders, "L3", cx + 20, 270);
            var q = b.Place(feeders, "IEC.CB.3P", cx, 230, function: l.name);
            b.Wire(feeders, a, "1", q, "1"); b.Wire(feeders, bb, "1", q, "3"); b.Wire(feeders, c, "1", q, "5");
            var ib = l.kw * 1000 / (Math.Sqrt(3) * 400 * l.pf);
            var size = CableSizingCalculator.Calculate(new CableSizingInput(SupplySystem.ThreePhase, 400, Math.Round(ib, 1), l.length, InstallationMethod.E, CableInsulation.XLPE, 35, 3, l.pf, 4));
            var section = size.Outputs.Any(o => o.Name == "Selected section") ? size.Output("Selected section") : 0;
            var rating = size.Outputs.Any(o => o.Name == "Protective device rating In") ? size.Output("Protective device rating In") : 0;
            var dev = b.Spec(q, $"DEMO-MCCB-{rating:0}A", $"Moulded-case breaker 3P {rating:0} A 36 kA", 180m + (decimal)rating * 1.2m, 105, 160, 90, $"{rating:0} A");
            var cable = CableService.CreateCable(p, "N2XY", 4, section, l.length, "+MDB", l.name);
            cable.Insulation = InsulationType.XLPE;
            CalculationService.Store(p, size, $"Cable sizing {cable.Tag} – {l.name}", cable.Id);
            CalculationService.Store(p, VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.ThreePhase, 400, Math.Round(ib, 1), l.length, Math.Max(section, 1.5), PowerFactor: l.pf, LimitPercent: 4)), $"Voltage drop {cable.Tag}");
            if (l.motor)
            {
                var m = b.Place(feeders, "IEC.MOTOR.3PH", cx, 150, function: l.name);
                foreach (var (from, to) in new[] { ("2", "U1"), ("4", "V1"), ("6", "W1") })
                    CableService.AssignWire(cable, b.Wire(feeders, q, from, m, to));
                var pe = b.Supply(feeders, "PE", cx + 30, 270);
                CableService.AssignWire(cable, b.Wire(feeders, pe, "1", m, "PE"));
                p.Motors.Add(new Motor { DeviceId = m.DeviceId, Tag = b.Device(m).Tag, Description = l.name, PowerKw = l.kw, VoltageV = 400, PowerFactor = l.pf, Efficiency = 0.94, RatedCurrentA = Math.Round(ib, 1), StartingMethod = l.kw > 50 ? MotorStartingMethod.SoftStarter : MotorStartingMethod.StarDelta, ProtectionDeviceTag = dev.Tag });
            }
            else
            {
                for (int k = 0; k < 3; k++)
                {
                    var t = b.Terminal(feeders, "-X10", (i * 3 + k + 1).ToString(), cx + k * 10, 180);
                    CableService.AssignWire(cable, b.Wire(feeders, q, (2 + 2 * k).ToString(), t, "1"));
                }
                var label = new TextEntity(new Point2(cx, 160), $"→ {l.name}", 2.5) { Layer = StandardLayers.Annotations };
                feeders.Drawing.Add(label);
            }
            p.Loads.Add(new ElectricalLoad
            {
                Tag = $"MDB-F{i + 1}", Description = l.name, Board = "MDB", Circuit = $"F{i + 1}", Category = l.cat, Phase = PhaseAssignment.ThreePhase,
                PowerKw = l.kw, PowerFactor = l.pf, DemandFactor = l.ku, VoltageV = 400, CableLengthM = l.length, CableCrossSectionMm2 = section,
                ProtectionRatingA = rating, ProtectionDeviceTag = dev.Tag, CableId = cable.Id,
            });
        }
        var installed = p.Loads.Sum(l => l.PowerKw);
        CalculationService.Store(p, LoadCalculator.Summarize(p.Loads.Select(l => new LoadItem(l.Description, l.PowerKw, l.PowerFactor, l.DemandFactor, Category: l.Category.ToString())).ToList(), 400, 0.9), "MDB load summary");
        CalculationService.Store(p, TransformerSizingCalculator.Calculate(installed, 0.75, 0.9, 25), "Transformer -T1 sizing");
        CalculationService.Store(p, ShortCircuitCalculator.Calculate(new ShortCircuitInput(350, 1000, 6, 10.5, 400, 15, 240, Conductor.Copper, 4)), "Short-circuit at MDB busbar");
        CalculationService.Store(p, ProtectionCalculator.SelectBreaker(1443, 1600, 25.0, 65), "Incoming breaker -Q0");
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-CAB-004

    private ElectricalProject ControlCabinet()
    {
        var (p, b) = NewProject("DEMO-CAB-004", "Machine Builder Demo GmbH (fictional)", "Packaging machine PM-4");
        var power = b.Sheet("Main switch and supply", SheetKind.Power, "Incoming supply", "=PM4", "+CAB1");
        var q0 = MainSwitch(b, power, CircuitBuilder.Col(1));
        b.Spec(q0, "DEMO-DISC-3P-63A", "Switch-disconnector 3P 63 A", 92.00m, 90, 90, 80, "63 A");
        var nS = b.Supply(power, "N", CircuitBuilder.Col(1) + 30, 270); var nR = b.Return(power, "N", CircuitBuilder.Col(1) + 30, 190);
        b.Wire(power, nS, "1", nR, "1");
        var g1 = b.PowerSupply24V(power, CircuitBuilder.Col(6), "1L1", "N");
        b.Spec(g1, "DEMO-PSU-24V-10A", "Power supply 24 V DC 10 A", 129.00m, 50, 125, 125);
        var fps = power.Drawing.Entities.OfType<SymbolInstanceEntity>().First(i => i.SymbolId == "IEC.CB.1P");
        b.Spec(fps, "DEMO-MCB-1P-C6", "Miniature circuit breaker 1P C6", 9.80m, 18, 90, 70, "C6");

        var motors = b.Sheet("Motor starters", SheetKind.Power, "Three DOL starters", "=PM4", "+CAB1");
        var names = new[] { "Infeed conveyor", "Sealing unit fan", "Outfeed conveyor" };
        var feeders = new List<CircuitBuilder.MotorFeeder>();
        for (int i = 0; i < 3; i++)
        {
            var f = b.DolFeeder(motors, CircuitBuilder.Col(1 + i * 3), names[i], "-X1", 1 + i * 4, phases: Main);
            b.Spec(f.Breaker, "DEMO-MPCB-4A", "Motor protection breaker 2.5–4 A", 47.50m, 45, 90, 80, "2.5–4 A");
            b.Spec(f.Contactor, "DEMO-CONT-9A-24DC", "Contactor 9 A AC-3, coil 24 V DC", 29.50m, 45, 82, 90);
            b.Spec(f.Overload!, "DEMO-OLR-2.5-4A", "Thermal overload relay 2.5–4 A", 28.00m, 45, 75, 80);
            b.Spec(f.Motor, "DEMO-MOT-1K1-4P", "Three-phase motor 1.1 kW", 210.00m, rating: "1.1 kW");
            foreach (var t in f.Terminals) b.Device(t).PartNumber = "DEMO-TB-2.5";
            feeders.Add(f);
            p.Motors.Add(new Motor { DeviceId = f.Motor.DeviceId, Tag = b.Device(f.Motor).Tag, Description = names[i], PowerKw = 1.1, VoltageV = 400, PowerFactor = 0.78, Efficiency = 0.84, RatedCurrentA = 2.4, StartingCurrentRatio = 6, ProtectionDeviceTag = b.Device(f.Breaker).Tag });
        }
        b.Product("DEMO-TB-2.5", "Feed-through terminal 2.5 mm²", 0.95m, 5.2, 45, 40, "Terminals");

        var control = b.Sheet("Starter control", SheetKind.Control, "Start/stop circuits", "=PM4", "+CAB1");
        for (int i = 0; i < 3; i++)
        {
            var (coil, start, stop) = b.StartStopRung(control, CircuitBuilder.Col(1 + i * 3), b.Device(feeders[i].Contactor).Tag, b.Device(feeders[i].Overload!).Tag,
                $"Start {names[i]}", $"Stop {names[i]}", i == 0 ? "-S0" : null);
            b.Spec(start, "DEMO-PB-GN-1NO", "Push button green, 1NO", 12.40m, 30, 30, 45);
            b.Spec(stop, "DEMO-PB-RD-1NC", "Push button red, 1NC", 12.40m, 30, 30, 45);
        }
        var es = control.Drawing.Entities.OfType<SymbolInstanceEntity>().First(e => e.SymbolId == "IEC.ESTOP");
        b.Spec(es, "DEMO-ESTOP-40", "Emergency stop mushroom 40 mm", 24.60m, 40, 40, 60);

        var st = PlcService.CreateStation(p, "-A2", "PM-4 controller", "codesys-style");
        PlcService.AddModule(st, st.Racks[0], "GEN-CPU");
        PlcService.AddModule(st, st.Racks[0], "GEN-DI16");
        PlcService.AddModule(st, st.Racks[0], "GEN-DO16");

        var cab = new Cabinet { Tag = "+CAB1", Name = "PM-4 control cabinet", Width = 800, Height = 1200, Depth = 300, PlateWidth = 700, PlateHeight = 1100, ClearanceMm = 0, EnclosurePartNumber = "DEMO-ENC-800-1200", IpRating = "IP54" };
        b.Product("DEMO-ENC-800-1200", "Steel enclosure 800×1200×300 IP54", 485.00m, 800, 1200, 300, "Enclosures");
        b.Product("DEMO-TS35", "DIN rail TS35×7.5 (per m)", 4.20m, category: "Mounting");
        b.Product("DEMO-DUCT", "Slotted cable duct 60×60 (per m)", 6.80m, category: "Mounting");
        p.Cabinets.Add(cab);
        foreach (var y in new[] { 1020.0, 760, 500, 240 }) CabinetLayoutService.AddDuct(cab, 10, y, 680, 60);
        CabinetLayoutService.AddDuct(cab, 10, 10, 60, 230);
        var rails = new[] { 880.0, 620, 360, 100 }.Select((y, i) => CabinetLayoutService.AddRail(cab, 80, y, i == 3 ? 440 : 600)).ToList();
        PlaceDevice(p, cab, rails[0], q0, 10);
        foreach (var f in feeders) PlaceDevice(p, cab, rails[0], f.Breaker, 2);
        PlaceDevice(p, cab, rails[0], fps, 2);
        foreach (var f in feeders) { PlaceDevice(p, cab, rails[1], f.Contactor, 2); PlaceDevice(p, cab, rails[1], f.Overload!, 8); }
        PlaceDevice(p, cab, rails[2], g1, 10);
        foreach (var m in st.Racks[0].Modules)
            CabinetLayoutService.PlaceOnRail(cab, rails[2], new CabinetItem { Kind = CabinetItemKind.Accessory, Name = $"{st.Tag} {m.TypeName}", PartNumber = m.PartNumber, Width = m.Kind == PlcModuleKind.Cpu ? 70 : 35, Height = 100, Depth = 75 }, 1);
        CabinetLayoutService.PlaceOnRail(cab, rails[3], new CabinetItem { Kind = CabinetItemKind.TerminalStrip, Name = "-X1", PartNumber = "DEMO-TB-2.5", Width = 12 * 5.2, Height = 45, Depth = 40 }, 0);
        CabinetLayoutService.Add(cab, new CabinetItem { Kind = CabinetItemKind.Fan, Name = "Filter fan", PartNumber = "DEMO-FAN-120", X = 560, Y = 100, Width = 120, Height = 120, Depth = 60 });
        b.Product("DEMO-FAN-120", "Filter fan 120 m³/h 230 V", 76.00m, 120, 120, 60, "Climate");
        p.TerminalStrips.Add(new TerminalStrip { Tag = "-X1", Location = "+CAB1", Description = "Motor terminals", TerminalPartNumber = "DEMO-TB-2.5" });
        Finish(p);
        return p;
    }

    // ===================================================================== DEMO-BLD-005

    private ElectricalProject BuildingInstallation()
    {
        var (p, b) = NewProject("DEMO-BLD-005", "Green Offices SCI (fictional)", "Office building – ground floor");
        p.Settings.StandardProfileId = "NFC15100";
        p.Settings.EarthingSystem = "TT";
        var inc = b.Sheet("Incoming and RCDs", SheetKind.Power, "Main switch and residual current devices", "=DB1", "+DB1");
        var x = CircuitBuilder.Col(1);
        var sup = new[] { "L1", "L2", "L3", "N" }.Select((n, i) => b.Supply(inc, n, x + i * 10, 270)).ToArray();
        var q0 = b.Place(inc, "IEC.CB.4P", x, 230, "-Q0", "Main breaker 63 A");
        for (int i = 0; i < 4; i++) b.Wire(inc, sup[i], "1", q0, (1 + 2 * i).ToString());
        var mid = new[] { "1L1", "1L2", "1L3", "1N" };
        for (int i = 0; i < 4; i++) b.Wire(inc, q0, (2 + 2 * i).ToString(), b.Return(inc, mid[i], x + i * 10, 190), "1");
        b.Spec(q0, "DEMO-MCB-4P-C63", "Main circuit breaker 4P C63", 96.00m, 72, 90, 70, "63 A");
        for (int r = 0; r < 3; r++)
        {
            var rx = CircuitBuilder.Col(4 + r * 2);
            var lp = b.Supply(inc, mid[r], rx, 270); var np = b.Supply(inc, "1N", rx + 10, 270);
            var rcd = b.Place(inc, "IEC.RCD.2P", rx, 230, function: $"RCD 30 mA group {r + 1}");
            b.Wire(inc, lp, "1", rcd, "1"); b.Wire(inc, np, "1", rcd, "N1");
            b.Wire(inc, rcd, "2", b.Return(inc, $"2L{r + 1}", rx, 190), "1");
            b.Wire(inc, rcd, "N2", b.Return(inc, $"2N{r + 1}", rx + 10, 190), "1");
            b.Spec(rcd, "DEMO-RCD-2P-40-30", "RCCB 2P 40 A 30 mA type A", 44.00m, 36, 90, 70, "40 A / 30 mA");
        }

        var circuits = new (string name, double kw, LoadCategory cat, double length, double section, int group, string symbol, double rating)[]
        {
            ("Lighting open office", 0.9, LoadCategory.Lighting, 32, 1.5, 1, "IEC.LAMP", 10),
            ("Lighting meeting rooms", 0.6, LoadCategory.Lighting, 24, 1.5, 2, "IEC.LAMP", 10),
            ("Lighting corridors & WC", 0.4, LoadCategory.Lighting, 38, 1.5, 3, "IEC.LAMP", 10),
            ("Emergency lighting", 0.15, LoadCategory.Lighting, 45, 1.5, 1, "IEC.LAMP", 10),
            ("Sockets open office", 2.5, LoadCategory.Sockets, 28, 2.5, 2, "IEC.SOCKET", 16),
            ("Sockets meeting rooms", 1.8, LoadCategory.Sockets, 22, 2.5, 3, "IEC.SOCKET", 16),
            ("Sockets kitchenette", 3.0, LoadCategory.Sockets, 18, 2.5, 1, "IEC.SOCKET", 20),
            ("Water heater", 2.2, LoadCategory.Heating, 15, 2.5, 2, "IEC.HEATER", 16),
        };
        var light = b.Sheet("Lighting circuits", SheetKind.Power, "Final circuits – lighting", "=DB1", "+DB1");
        var sockets = b.Sheet("Socket and heating circuits", SheetKind.Power, "Final circuits – sockets and heater", "=DB1", "+DB1");
        int li = 0, si = 0;
        for (int c = 0; c < circuits.Length; c++)
        {
            var ci = circuits[c];
            var sheet = ci.cat == LoadCategory.Lighting ? light : sockets;
            var col = ci.cat == LoadCategory.Lighting ? 1 + 2 * li++ : 1 + 2 * si++;
            var cx = CircuitBuilder.Col(col);
            var lp = b.Supply(sheet, $"2L{ci.group}", cx, 270);
            var q = b.Place(sheet, "IEC.CB.1P", cx, 235, function: ci.name);
            b.Wire(sheet, lp, "1", q, "1");
            var load = b.Place(sheet, ci.symbol, cx, 165, function: ci.name);
            var nR = b.Supply(sheet, $"2N{ci.group}", cx + 15, 270);
            var cable = CableService.CreateCable(p, ci.section >= 2.5 ? "H07V-U 3G2.5" : "H07V-U 3G1.5", 3, ci.section, ci.length, "+DB1", ci.name);
            if (ci.symbol == "IEC.SOCKET")
            {
                CableService.AssignWire(cable, b.Wire(sheet, q, "2", load, "L"));
                CableService.AssignWire(cable, b.Wire(sheet, nR, "1", load, "N"));
                var pe = b.Supply(sheet, "PE", cx + 25, 270);
                CableService.AssignWire(cable, b.Wire(sheet, pe, "1", load, "PE", new Point2(cx + 25, 190), new Point2(cx, 190)));
            }
            else
            {
                var first = load.Definition!.Pins[0].Id; var second = load.Definition.Pins[1].Id;
                CableService.AssignWire(cable, b.Wire(sheet, q, "2", load, first));
                var n = b.Return(sheet, $"2N{ci.group}", cx, 120);
                CableService.AssignWire(cable, b.Wire(sheet, load, second, n, "1"));
                sheet.Drawing.Remove(nR);
            }
            b.Spec(q, $"DEMO-MCB-1P-C{ci.rating:0}", $"Miniature circuit breaker 1P+N C{ci.rating:0}", 11.50m, 18, 90, 70, $"C{ci.rating:0}");
            b.Device(load).Category = ci.cat.ToString();
            var phase = (PhaseAssignment)(ci.group - 1);
            p.Loads.Add(new ElectricalLoad
            {
                Tag = $"C{c + 1}", Description = ci.name, Board = "DB1", Circuit = $"C{c + 1}", Category = ci.cat, Phase = phase, PowerKw = ci.kw,
                PowerFactor = ci.cat == LoadCategory.Lighting ? 0.95 : 1.0, DemandFactor = ci.cat == LoadCategory.Sockets ? 0.5 : 1.0,
                VoltageV = 230, CableLengthM = ci.length, CableCrossSectionMm2 = ci.section, ProtectionRatingA = ci.rating, ProtectionDeviceTag = b.Device(q).Tag, CableId = cable.Id,
            });
            var ib = ci.kw * 1000 / 230 / (ci.cat == LoadCategory.Lighting ? 0.95 : 1.0);
            CalculationService.Store(p, VoltageDropCalculator.Calculate(new VoltageDropInput(SupplySystem.SinglePhase, 230, Math.Round(ib, 2), ci.length, ci.section,
                PowerFactor: ci.cat == LoadCategory.Lighting ? 0.95 : 1.0, LimitPercent: ci.cat == LoadCategory.Lighting ? 3 : 5), "NF C 15-100 525 / IEC 60364-5-52 G.52"),
                $"Voltage drop {ci.name}", cable.Id);
        }
        var (balance, _) = LoadBalancingCalculator.Balance(p.Loads.Select(l => new LoadItem(l.Description, l.PowerKw, l.PowerFactor, l.DemandFactor, "1P", l.Category.ToString())).ToList());
        CalculationService.Store(p, balance, "DB1 phase balancing (proposal)");
        CalculationService.Store(p, LoadCalculator.Summarize(p.Loads.Select(l => new LoadItem(l.Description, l.PowerKw, l.PowerFactor, l.DemandFactor, "1P", l.Category.ToString())).ToList(), 400), "DB1 load summary");
        CalculationService.Store(p, ProtectionCalculator.TnMaximumLength(230, 2.5, 2.5, 160, actualLengthM: 28), "Fault loop check sockets (informative – TT system uses RCDs)");
        Finish(p);
        return p;
    }
}
