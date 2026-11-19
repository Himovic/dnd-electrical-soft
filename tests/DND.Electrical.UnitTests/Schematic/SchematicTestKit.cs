using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.UnitTests.Schematic;

/// <summary>Builds small, realistic circuits for tests.</summary>
public static class SchematicTestKit
{
    public static readonly SymbolLibraryService Library = new();

    public static SchematicService NewService()
    {
        var resolver = new SymbolResolver(Library);
        return new SchematicService(resolver, new SchematicBinder(resolver));
    }

    public static ElectricalProject NewProject() => new()
    {
        Info = new ProjectInfo { Number = "TEST-001", Name = "Unit test project", CompanyName = "Test Co" },
    };

    public sealed record MotorCircuit(ElectricalProject Project, Sheet Power, Sheet Control,
        SymbolInstanceEntity Breaker, SymbolInstanceEntity MainContacts, SymbolInstanceEntity Overload, SymbolInstanceEntity Motor,
        SymbolInstanceEntity StopButton, SymbolInstanceEntity StartButton, SymbolInstanceEntity Coil, SymbolInstanceEntity HoldContact,
        SymbolInstanceEntity ZeroVolt);

    private static Dictionary<string, string> Pot(string name) => new() { [SymbolAttributes.Potential] = name };

    /// <summary>
    /// Sheet 1 (power): L1/L2/L3 → -Q1 (MPCB) → -K1 main contacts → -F1 overload → -M1.
    /// Sheet 2 (control): +24V → -S1 (stop, NC) → -S2 (start, NO) ∥ -K1 13-14 (hold) → -K1 coil → 0V.
    /// </summary>
    public static MotorCircuit BuildMotorCircuit(SchematicService svc)
    {
        var p = NewProject();
        var power = svc.AddSheet(p, "Power circuit", SheetKind.Power);
        var control = svc.AddSheet(p, "Control circuit", SheetKind.Control);

        var l1 = svc.PlaceSymbol(p, power, "IEC.SUPPLY", new Point2(60, 250), values: Pot("L1"));
        var l2 = svc.PlaceSymbol(p, power, "IEC.SUPPLY", new Point2(70, 250), values: Pot("L2"));
        var l3 = svc.PlaceSymbol(p, power, "IEC.SUPPLY", new Point2(80, 250), values: Pot("L3"));
        var q1 = svc.PlaceSymbol(p, power, "IEC.MPCB.3P", new Point2(60, 220), function: "Motor protection");
        var k1 = svc.PlaceSymbol(p, power, "IEC.KM.3P", new Point2(60, 180));
        var f1 = svc.PlaceSymbol(p, power, "IEC.OL.3P", new Point2(60, 145));
        var m1 = svc.PlaceSymbol(p, power, "IEC.MOTOR.3PH", new Point2(60, 100), function: "Pump motor");
        svc.Connect(power, l1, "1", q1, "1");
        svc.Connect(power, l2, "1", q1, "3");
        svc.Connect(power, l3, "1", q1, "5");
        foreach (var (a, b) in new[] { ("2", "1"), ("4", "3"), ("6", "5") }) svc.Connect(power, q1, a, k1, b);
        foreach (var (a, b) in new[] { ("2", "1"), ("4", "3"), ("6", "5") }) svc.Connect(power, k1, a, f1, b);
        svc.Connect(power, f1, "2", m1, "U1");
        svc.Connect(power, f1, "4", m1, "V1");
        svc.Connect(power, f1, "6", m1, "W1");

        var k1Tag = p.FindDevice(k1.DeviceId!.Value)!.Tag;
        var p24 = svc.PlaceSymbol(p, control, "IEC.SUPPLY", new Point2(100, 250), values: Pot("+24V"));
        var s1 = svc.PlaceSymbol(p, control, "IEC.PB.NC", new Point2(100, 225), function: "Stop");
        var s2 = svc.PlaceSymbol(p, control, "IEC.PB.NO", new Point2(100, 185), function: "Start");
        var hold = svc.PlaceSymbol(p, control, "IEC.CONTACT.NO", new Point2(130, 185), tag: k1Tag);
        var coil = svc.PlaceSymbol(p, control, "IEC.COIL", new Point2(100, 140), tag: k1Tag, function: "Pump contactor");
        var zero = svc.PlaceSymbol(p, control, "IEC.SUPPLY", new Point2(100, 115), rotation: 180, values: Pot("0V"));
        svc.Connect(control, p24, "1", s1, "11");
        var top = svc.AddJunction(control, new Point2(100, 205));
        var bottom = svc.AddJunction(control, new Point2(100, 165));
        svc.ConnectToJunction(control, s1, "12", top);
        svc.ConnectToJunction(control, s2, "13", top);
        svc.ConnectToJunction(control, s2, "14", bottom);
        svc.ConnectToJunction(control, coil, "A1", bottom);
        control.Drawing.Add(new WireEntity { Points = { new(100, 205), new(130, 205), new(130, 195) }, Start = WireEnd.Junction(top.Id), End = WireEnd.Pin(hold.Id, "13") });
        control.Drawing.Add(new WireEntity { Points = { new(130, 175), new(130, 165), new(100, 165) }, Start = WireEnd.Pin(hold.Id, "14"), End = WireEnd.Junction(bottom.Id) });
        svc.Connect(control, coil, "A2", zero, "1");

        svc.Binder.BindProject(p);
        return new MotorCircuit(p, power, control, q1, k1, f1, m1, s1, s2, coil, hold, zero);
    }
}
