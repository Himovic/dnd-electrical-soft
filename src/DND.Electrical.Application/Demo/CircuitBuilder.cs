using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Application.Demo;

/// <summary>Concise API for constructing schematics in code (templates, demos, tests).</summary>
public sealed class CircuitBuilder(ElectricalProject project, SchematicService schematic)
{
    public ElectricalProject Project { get; } = project;
    public SchematicService Schematic { get; } = schematic;

    public const string DemoManufacturer = "D&D Demo Components (fictional)";

    /// <summary>Horizontal centre of reference column <paramref name="column"/> on an A3 sheet with 10 columns.</summary>
    public static double Col(int column) => 15 + (column - 0.5) * 40;

    public Sheet Sheet(string title, SheetKind kind, string description = "", string function = "", string location = "")
    {
        var s = Schematic.AddSheet(Project, title, kind, null, description);
        s.Function = function;
        s.Location = location;
        return s;
    }

    public SymbolInstanceEntity Place(Sheet sheet, string symbolId, double x, double y, string? tag = null, string? function = null,
        double rotation = 0, IDictionary<string, string>? values = null, IDictionary<string, string>? pinLabels = null)
    {
        var inst = Schematic.PlaceSymbol(Project, sheet, symbolId, new Point2(x, y), tag, rotation, false, values, function);
        if (pinLabels is not null) foreach (var kv in pinLabels) inst.PinLabels[kv.Key] = kv.Value;
        return inst;
    }

    /// <summary>Potential feed pointing up; its pin is 5 mm below <paramref name="y"/>.</summary>
    public SymbolInstanceEntity Supply(Sheet sheet, string potential, double x, double y) =>
        Place(sheet, "IEC.SUPPLY", x, y, values: new Dictionary<string, string> { [SymbolAttributes.Potential] = potential });

    /// <summary>Potential feed pointing down (return conductor); its pin is 5 mm above <paramref name="y"/>.</summary>
    public SymbolInstanceEntity Return(Sheet sheet, string potential, double x, double y) =>
        Place(sheet, "IEC.SUPPLY", x, y, rotation: 180, values: new Dictionary<string, string> { [SymbolAttributes.Potential] = potential });

    public SymbolInstanceEntity Interruption(Sheet sheet, string name, double x, double y, bool pointingDown = false) =>
        Place(sheet, "IEC.INTERRUPT", x, y, rotation: pointingDown ? 180 : 0, values: new Dictionary<string, string> { [SymbolAttributes.Name] = name });

    public SymbolInstanceEntity Terminal(Sheet sheet, string strip, string number, double x, double y, bool pe = false) =>
        Place(sheet, pe ? "IEC.TERMINAL.PE" : "IEC.TERMINAL", x, y, tag: strip, values: new Dictionary<string, string> { [SymbolAttributes.TerminalNumber] = number });

    public SymbolInstanceEntity Contact(Sheet sheet, string tag, bool normallyOpen, double x, double y, string pinA, string pinB) =>
        Place(sheet, normallyOpen ? "IEC.CONTACT.NO" : "IEC.CONTACT.NC", x, y, tag,
            pinLabels: normallyOpen ? new Dictionary<string, string> { ["13"] = pinA, ["14"] = pinB } : new Dictionary<string, string> { ["21"] = pinA, ["22"] = pinB });

    public WireEntity Wire(Sheet sheet, SymbolInstanceEntity a, string pinA, SymbolInstanceEntity b, string pinB, params Point2[] via) =>
        Schematic.Connect(sheet, a, pinA, b, pinB, via.Length > 0 ? via : null);

    public WireEntity Wire(Sheet sheet, SymbolInstanceEntity a, string pinA, JunctionEntity j) => Schematic.ConnectToJunction(sheet, a, pinA, j);

    public JunctionEntity Junction(Sheet sheet, double x, double y) => Schematic.AddJunction(sheet, new Point2(x, y));

    /// <summary>Wires three poles (1→1, 3→3, 5→5 style pin pairs).</summary>
    public void Wire3(Sheet sheet, SymbolInstanceEntity a, string[] pinsA, SymbolInstanceEntity b, string[] pinsB)
    {
        for (int i = 0; i < pinsA.Length; i++) Wire(sheet, a, pinsA[i], b, pinsB[i]);
    }

    public Device Device(SymbolInstanceEntity inst) => Project.FindDevice(inst.DeviceId!.Value)!;

    /// <summary>Assigns a (fictional) catalogue product to the device and records it in the project catalogue.</summary>
    public Device Spec(SymbolInstanceEntity inst, string partNumber, string description, decimal price, double w = 18, double h = 90, double d = 70, string? rating = null, string category = "")
    {
        var device = Device(inst);
        device.Manufacturer = DemoManufacturer;
        device.PartNumber = partNumber;
        if (rating is not null) device.Properties[DeviceProperties.Rating] = rating;
        Product(partNumber, description, price, w, h, d, string.IsNullOrEmpty(category) ? device.Category : category);
        return device;
    }

    public CatalogProduct Product(string partNumber, string description, decimal price, double w = 0, double h = 0, double d = 0, string category = "")
    {
        var existing = Project.FindProduct(DemoManufacturer, partNumber);
        if (existing is not null) return existing;
        var p = new CatalogProduct
        {
            Manufacturer = DemoManufacturer, PartNumber = partNumber, Description = description, Category = category,
            UnitPrice = price, Currency = Project.Settings.Currency, Supplier = "Demo Supplier (fictional)", WidthMm = w, HeightMm = h, DepthMm = d,
            IsDemoData = true, Source = "Built-in fictional demonstration catalogue",
        };
        Project.Catalog.Add(p);
        return p;
    }

    // ------------------------------------------------------------------ reusable circuits

    public sealed record MotorFeeder(SymbolInstanceEntity Breaker, SymbolInstanceEntity Contactor, SymbolInstanceEntity? Overload,
        IReadOnlyList<SymbolInstanceEntity> Terminals, SymbolInstanceEntity Motor, IReadOnlyList<WireEntity> MotorWires);

    /// <summary>
    /// Direct-on-line motor feeder drawn top-down at <paramref name="x"/>: L1/L2/L3 feeds → MPCB → contactor →
    /// (overload) → terminals → motor, PE through a PE terminal.
    /// </summary>
    public MotorFeeder DolFeeder(Sheet sheet, double x, string motorFunction, string? terminalStrip = "-X1", int firstTerminal = 1, bool withOverload = true,
        string? breakerSymbol = "IEC.MPCB.3P", string[]? phases = null)
    {
        phases ??= new[] { "L1", "L2", "L3" };
        var l1 = Supply(sheet, phases[0], x, 270); var l2 = Supply(sheet, phases[1], x + 10, 270); var l3 = Supply(sheet, phases[2], x + 20, 270);
        var pe = Supply(sheet, "PE", x + 30, 270);
        var q = Place(sheet, breakerSymbol ?? "IEC.MPCB.3P", x, 230, function: $"Protection {motorFunction}");
        var k = Place(sheet, "IEC.KM.3P", x, 190);
        Wire(sheet, l1, "1", q, "1"); Wire(sheet, l2, "1", q, "3"); Wire(sheet, l3, "1", q, "5");
        Wire3(sheet, q, new[] { "2", "4", "6" }, k, new[] { "1", "3", "5" });
        SymbolInstanceEntity? f = null;
        var last = k;
        if (withOverload)
        {
            f = Place(sheet, "IEC.OL.3P", x, 150, function: "Overload protection");
            Wire3(sheet, k, new[] { "2", "4", "6" }, f, new[] { "1", "3", "5" });
            last = f;
        }
        var terminals = new List<SymbolInstanceEntity>();
        var motor = Place(sheet, "IEC.MOTOR.3PH", x, 75, function: motorFunction);
        var motorWires = new List<WireEntity>();
        if (terminalStrip is not null)
        {
            for (int i = 0; i < 3; i++) terminals.Add(Terminal(sheet, terminalStrip, (firstTerminal + i).ToString(), x + i * 10, 110));
            var tpe = Terminal(sheet, terminalStrip, (firstTerminal + 3).ToString(), x + 30, 110, pe: true);
            terminals.Add(tpe);
            var outPins = new[] { "2", "4", "6" };
            for (int i = 0; i < 3; i++) Wire(sheet, last, outPins[i], terminals[i], "1");
            Wire(sheet, pe, "1", tpe, "1");
            motorWires.Add(Wire(sheet, terminals[0], "2", motor, "U1"));
            motorWires.Add(Wire(sheet, terminals[1], "2", motor, "V1"));
            motorWires.Add(Wire(sheet, terminals[2], "2", motor, "W1"));
            motorWires.Add(Wire(sheet, tpe, "2", motor, "PE"));
        }
        else
        {
            Wire(sheet, last, "2", motor, "U1"); Wire(sheet, last, "4", motor, "V1"); Wire(sheet, last, "6", motor, "W1");
            Wire(sheet, pe, "1", motor, "PE");
        }
        return new MotorFeeder(q, k, f, terminals, motor, motorWires);
    }

    /// <summary>
    /// Start/stop self-holding control rung at <paramref name="x"/>: +24V → (E-stop) → stop → start ∥ hold → (overload NC) → coil → 0V.
    /// </summary>
    public (SymbolInstanceEntity coil, SymbolInstanceEntity start, SymbolInstanceEntity stop) StartStopRung(Sheet sheet, double x, string contactorTag,
        string? overloadTag, string startFunction, string stopFunction, string? emergencyStopTag = null, string supply = "+24V", string ret = "0V")
    {
        var top = Supply(sheet, supply, x, 272);
        SymbolInstanceEntity upstream = top;
        string upstreamPin = "1";
        if (emergencyStopTag is not null)
        {
            var es = Place(sheet, "IEC.ESTOP", x, 245, emergencyStopTag, "Emergency stop");
            Wire(sheet, top, "1", es, "11");
            upstream = es; upstreamPin = "12";
        }
        var stop = Place(sheet, "IEC.PB.NC", x, 215, function: stopFunction);
        Wire(sheet, upstream, upstreamPin, stop, "11");
        var j1 = Junction(sheet, x, 200);
        Wire(sheet, stop, "12", j1);
        var start = Place(sheet, "IEC.PB.NO", x, 180, function: startFunction);
        var hold = Contact(sheet, contactorTag, true, x + 25, 180, "13", "14");
        Wire(sheet, start, "13", j1);
        var j2 = Junction(sheet, x, 160);
        Wire(sheet, start, "14", j2);
        // Hold contact in parallel with the start button (T-connections at both junctions).
        sheet.Drawing.Add(new WireEntity { Points = { new Point2(x + 25, 190), new Point2(x + 25, 200), new Point2(x, 200) }, Start = WireEnd.Pin(hold.Id, "13"), End = WireEnd.Junction(j1.Id) });
        sheet.Drawing.Add(new WireEntity { Points = { new Point2(x + 25, 170), new Point2(x + 25, 160), new Point2(x, 160) }, Start = WireEnd.Pin(hold.Id, "14"), End = WireEnd.Junction(j2.Id) });
        SymbolInstanceEntity beforeCoil;
        string beforeCoilPin;
        if (overloadTag is not null)
        {
            var ol = Contact(sheet, overloadTag, false, x, 140, "95", "96");
            Wire(sheet, ol, "21", j2);
            beforeCoil = ol; beforeCoilPin = "22";
        }
        else { beforeCoil = start; beforeCoilPin = "14"; }
        var coil = Place(sheet, "IEC.COIL", x, 110, contactorTag);
        if (overloadTag is not null) Wire(sheet, beforeCoil, beforeCoilPin, coil, "A1"); else Wire(sheet, coil, "A1", j2);
        var zero = Return(sheet, ret, x, 85);
        Wire(sheet, coil, "A2", zero, "1");
        return (coil, start, stop);
    }

    /// <summary>Signal lamp rung: +24V → contact → lamp → 0V.</summary>
    public SymbolInstanceEntity LampRung(Sheet sheet, double x, string contactTag, bool normallyOpen, string pinA, string pinB, string lampFunction, string supply = "+24V", string ret = "0V")
    {
        var top = Supply(sheet, supply, x, 272);
        var c = Contact(sheet, contactTag, normallyOpen, x, 215, pinA, pinB);
        var h = Place(sheet, "IEC.LAMP", x, 150, function: lampFunction);
        var zero = Return(sheet, ret, x, 85);
        Wire(sheet, top, "1", c, normallyOpen ? "13" : "21");
        Wire(sheet, c, normallyOpen ? "14" : "22", h, "X1");
        Wire(sheet, h, "X2", zero, "1");
        return h;
    }

    /// <summary>24 V DC power supply: L/N/PE → fuse → PSU → +24V / 0V.</summary>
    public SymbolInstanceEntity PowerSupply24V(Sheet sheet, double x, string phase = "L1", string neutral = "N")
    {
        var l = Supply(sheet, phase, x - 5, 270); var n = Supply(sheet, neutral, x, 270); var pe = Supply(sheet, "PE", x + 5, 270);
        var fu = Place(sheet, "IEC.CB.1P", x - 5, 225, function: "Control supply protection");
        var g = Place(sheet, "IEC.PSU.ACDC", x, 170, function: "24 V DC power supply");
        Wire(sheet, l, "1", fu, "1");
        Wire(sheet, fu, "2", g, "L");
        Wire(sheet, n, "1", g, "N", new Point2(x, 230));
        Wire(sheet, pe, "1", g, "PE");
        var plus = Return(sheet, "+24V", x - 5, 120);
        var minus = Return(sheet, "0V", x + 5, 120);
        Wire(sheet, g, "+", plus, "1");
        Wire(sheet, g, "-", minus, "1");
        return g;
    }
}
