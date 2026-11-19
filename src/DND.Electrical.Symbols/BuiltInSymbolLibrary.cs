using DND.Electrical.CAD.Model;

namespace DND.Electrical.Symbols;

/// <summary>
/// The built-in "D&amp;D IEC Basic" library. All artwork is original and follows common IEC 60617
/// drawing conventions (vertical current paths, pins on a 5 mm grid, multipole spacing 10 mm).
/// </summary>
public static class BuiltInSymbolLibrary
{
    public const string LibraryId = "dnd-iec-basic";

    public static SymbolLibrary Create()
    {
        var lib = new SymbolLibrary
        {
            Id = LibraryId,
            Name = "D&D IEC Basic",
            Description = "Built-in IEC-style electrical symbols (original artwork by D&D | Data&Development).",
            Author = "D&D | Data&Development",
            Standard = "IEC 60617 style",
            Version = 3,
            IsReadOnly = true,
        };
        lib.Symbols.AddRange(Protection());
        lib.Symbols.AddRange(Switching());
        lib.Symbols.AddRange(Machines());
        lib.Symbols.AddRange(Control());
        lib.Symbols.AddRange(Sensors());
        lib.Symbols.AddRange(Signaling());
        lib.Symbols.AddRange(ConnectionSymbols());
        lib.Symbols.AddRange(Plc());
        return lib;
    }

    private static string[] Poles(int n, int start = 1) => Enumerable.Range(0, n).Select(i => (start + 2 * i).ToString()).ToArray();

    private static IEnumerable<SymbolDefinition> Protection()
    {
        // Circuit breakers: NO contact with a cross on the fixed contact.
        foreach (var poles in new[] { 1, 2, 3, 4 })
        {
            var b = new SymbolBuilder($"IEC.CB.{poles}P", $"Circuit breaker {poles}-pole", SymbolCategory.Protection, "Q")
                .Describe($"Miniature/moulded-case circuit breaker, {poles} pole(s).", "breaker", "MCB", "MCCB", "disjoncteur");
            for (int i = 0; i < poles; i++)
            {
                double x = i * 10;
                b.NoContact(x).Line(x - 1.5, 2.5, x + 1.5, 5.5).Line(x - 1.5, 5.5, x + 1.5, 2.5);
                b.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
            }
            if (poles > 1) b.Linkage(-2, (poles - 1) * 10 - 2);
            b.Attr(SymbolAttributes.Rating, (poles - 1) * 10 + 4, -6, Core_Left(), height: 2, layer: StandardLayers.Annotations);
            yield return b.Build();
        }

        var mpcb = new SymbolBuilder("IEC.MPCB.3P", "Motor protection circuit breaker 3-pole", SymbolCategory.Protection, "Q")
            .Describe("Motor protective circuit breaker with thermal (overload) and magnetic (short-circuit) releases.", "motor breaker", "MPCB", "disjoncteur moteur");
        for (int i = 0; i < 3; i++)
        {
            double x = i * 10;
            mpcb.NoContact(x).Line(x - 1.5, 2.5, x + 1.5, 5.5).Line(x - 1.5, 5.5, x + 1.5, 2.5);
            mpcb.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
        }
        mpcb.Linkage(-2, 26).Rect(24, -2, 30, 3).Text(27, 0.5, "I>", 1.8).Line(30, 0.5, 33, 0.5).Rect(33, -2, 39, 3).Poly(false, 34, -1, 35.5, -1, 35.5, 2, 37, 2, 37, -1, 38, -1);
        mpcb.Attr(SymbolAttributes.Rating, 24, -6, Core_Left(), height: 2, layer: StandardLayers.Annotations);
        yield return mpcb.Build();

        foreach (var poles in new[] { 1, 3 })
        {
            var f = new SymbolBuilder($"IEC.FUSE.{poles}P", poles == 1 ? "Fuse" : "Fuse 3-pole", SymbolCategory.Protection, "F")
                .Describe("Fuse link / fuse holder.", "fuse", "fusible");
            for (int i = 0; i < poles; i++)
            {
                double x = i * 10;
                f.Line(x, 10, x, -10).Rect(x - 1.5, -5, x + 1.5, 5);
                f.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
            }
            f.Attr(SymbolAttributes.Rating, (poles - 1) * 10 + 4, -6, Core_Left(), height: 2, layer: StandardLayers.Annotations);
            yield return f.Build();
        }

        var rcd = new SymbolBuilder("IEC.RCD.2P", "Residual current device 2-pole", SymbolCategory.Protection, "Q")
            .Describe("Residual current circuit breaker (RCCB), 2 poles.", "RCD", "RCCB", "differential", "différentiel");
        rcd.NoContact(0).NoContact(10).Linkage(-2, 8).Line(0, -7, 10, -7).Rect(12, -9, 18, -5).Text(15, -7, "IΔ", 1.8)
            .Line(10, -7, 12, -7)
            .Pin("1", 0, 10, PinDirection.Up).Pin("2", 0, -10, PinDirection.Down)
            .Pin("N1", 10, 10, PinDirection.Up, label: "N").Pin("N2", 10, -10, PinDirection.Down, label: "N");
        rcd.Attr(SymbolAttributes.Rating, 14, 2, Core_Left(), height: 2, layer: StandardLayers.Annotations);
        yield return rcd.Build();

        var ol = new SymbolBuilder("IEC.OL.3P", "Thermal overload relay 3-pole", SymbolCategory.Protection, "F", SymbolRole.Main)
            .Describe("Thermal overload relay (main current path). Its auxiliary contacts 95-96 / 97-98 are drawn with contact symbols.", "overload", "thermal", "relais thermique");
        for (int i = 0; i < 3; i++)
        {
            double x = i * 10;
            ol.Line(x, 10, x, 3).Poly(false, x, 3, x, 1.5, x + 2, 1.5, x + 2, -1.5, x, -1.5, x, -3).Line(x, -3, x, -10);
            ol.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
        }
        ol.Rect(-3, -5, 24, 5);
        ol.Attr(SymbolAttributes.Rating, 26, -6, Core_Left(), height: 2, layer: StandardLayers.Annotations);
        yield return ol.Build();
    }

    private static IEnumerable<SymbolDefinition> Switching()
    {
        var coil = new SymbolBuilder("IEC.COIL", "Contactor / relay coil", SymbolCategory.Switching, "K", SymbolRole.Main)
            .Describe("Operating coil of a contactor or relay. Contacts of the same device are cross-referenced automatically.", "coil", "contactor", "relay", "bobine")
            .Line(0, 10, 0, 3).Line(0, -3, 0, -10).Rect(-5, -3, 5, 3)
            .Pin("A1", 0, 10, PinDirection.Up).Pin("A2", 0, -10, PinDirection.Down);
        yield return coil.Build();

        var timer = new SymbolBuilder("IEC.COIL.TON", "Timer relay coil (on-delay)", SymbolCategory.Switching, "K", SymbolRole.Main)
            .Describe("On-delay timer relay coil.", "timer", "temporisation", "TON")
            .Line(0, 10, 0, 3).Line(0, -3, 0, -10).Rect(-5, -3, 5, 3).Rect(-5, -3, -2, 3).Line(-5, -3, -2, 3)
            .Pin("A1", 0, 10, PinDirection.Up).Pin("A2", 0, -10, PinDirection.Down);
        yield return timer.Build();

        foreach (var poles in new[] { 3, 4 })
        {
            var km = new SymbolBuilder($"IEC.KM.{poles}P", $"Contactor main contacts {poles}-pole", SymbolCategory.Switching, "K", SymbolRole.ChildContact)
                .Contact(ContactType.Power)
                .Describe("Main (power) contacts of a contactor. Associate with the coil by giving them the same device tag.", "contactor", "power contacts", "contacteur");
            for (int i = 0; i < poles; i++)
            {
                double x = i * 10;
                km.NoContact(x).Arc(x, 4, 1.5, 180, 180);
                km.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
            }
            km.Linkage(-2, (poles - 1) * 10 - 2);
            yield return km.Build();
        }

        yield return new SymbolBuilder("IEC.CONTACT.NO", "Auxiliary contact NO", SymbolCategory.Contacts, "K", SymbolRole.ChildContact)
            .Contact(ContactType.NormallyOpen)
            .Describe("Normally open auxiliary contact (e.g. 13-14).", "contact", "NO", "auxiliary", "contact auxiliaire")
            .NoContact().Pin("13", 0, 10, PinDirection.Up).Pin("14", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.CONTACT.NC", "Auxiliary contact NC", SymbolCategory.Contacts, "K", SymbolRole.ChildContact)
            .Contact(ContactType.NormallyClosed)
            .Describe("Normally closed auxiliary contact (e.g. 21-22, overload 95-96).", "contact", "NC", "auxiliary", "contact auxiliaire")
            .NcContact().Pin("21", 0, 10, PinDirection.Up).Pin("22", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.CONTACT.CO", "Change-over contact", SymbolCategory.Contacts, "K", SymbolRole.ChildContact)
            .Contact(ContactType.ChangeOver)
            .Describe("Change-over contact (11 common, 12 NC, 14 NO).", "contact", "changeover", "inverseur")
            .Line(0, -10, 0, -4).Line(0, -4, -3.5, 4.5).Line(-5, 10, -5, 4).Line(5, 10, 5, 4).Line(5, 4, 1, 4)
            .Pin("11", 0, -10, PinDirection.Down).Pin("12", 5, 10, PinDirection.Up).Pin("14", -5, 10, PinDirection.Up).Build();

        var disc = new SymbolBuilder("IEC.DISC.3P", "Switch-disconnector 3-pole", SymbolCategory.Switching, "Q")
            .Describe("Load-break switch-disconnector / main isolator.", "isolator", "disconnector", "main switch", "sectionneur", "interrupteur");
        for (int i = 0; i < 3; i++)
        {
            double x = i * 10;
            disc.NoContact(x).Line(x - 1.5, 4, x + 1.5, 4).Arc(x, 4, 1.5, 180, 180);
            disc.Pin((1 + 2 * i).ToString(), x, 10, PinDirection.Up).Pin((2 + 2 * i).ToString(), x, -10, PinDirection.Down);
        }
        disc.Linkage(-2, 18).Line(-2, 0.5, -8, 0.5, dashed: true).Line(-8, -2, -8, 3).Line(-8, 3, -10, 3);
        yield return disc.Build();
    }

    private static IEnumerable<SymbolDefinition> Machines()
    {
        var m3 = new SymbolBuilder("IEC.MOTOR.3PH", "Three-phase motor", SymbolCategory.Motors, "M")
            .Describe("Three-phase asynchronous motor with protective earth terminal.", "motor", "induction", "moteur", "3~")
            .Circle(10, -6, 11).Text(10, -4, "M", 5).Text(10, -11, "3~", 3)
            .Line(0, 10, 0, -1.42).Line(10, 10, 10, 5).Line(20, 10, 20, -1.42)
            .Line(30, 10, 30, -6).Line(30, -6, 21, -6)
            .Pin("U1", 0, 10, PinDirection.Up).Pin("V1", 10, 10, PinDirection.Up).Pin("W1", 20, 10, PinDirection.Up).Pin("PE", 30, 10, PinDirection.Up);
        m3.Attr(SymbolAttributes.Rating, 23, -16, Core_Left(), height: 2, layer: StandardLayers.Annotations);
        yield return m3.Build();

        var m1 = new SymbolBuilder("IEC.MOTOR.1PH", "Single-phase motor", SymbolCategory.Motors, "M")
            .Describe("Single-phase AC motor.", "motor", "moteur", "1~")
            .Circle(5, -6, 8).Text(5, -4.5, "M", 4).Text(5, -9.5, "1~", 2.5)
            .Line(0, 10, 0, 1.33).Line(10, 10, 10, 1.33)
            .Pin("L", 0, 10, PinDirection.Up).Pin("N", 10, 10, PinDirection.Up);
        yield return m1.Build();

        yield return new SymbolBuilder("IEC.TR.1PH", "Single-phase transformer", SymbolCategory.Transformers, "T")
            .Describe("Two-winding single-phase transformer (control transformer).", "transformer", "transformateur")
            .Circle(0, 3, 5).Circle(0, -3, 5)
            .Line(-5, 10, -5, 7).Line(-5, 7, -3, 6.6).Line(5, 10, 5, 7).Line(5, 7, 3, 6.6)
            .Line(-5, -10, -5, -7).Line(-5, -7, -3, -6.6).Line(5, -10, 5, -7).Line(5, -7, 3, -6.6)
            .Pin("1", -5, 10, PinDirection.Up).Pin("2", 5, 10, PinDirection.Up).Pin("3", -5, -10, PinDirection.Down).Pin("4", 5, -10, PinDirection.Down)
            .Build();

        var t3 = new SymbolBuilder("IEC.TR.3PH", "Three-phase transformer", SymbolCategory.Transformers, "T")
            .Describe("Three-phase two-winding power transformer.", "transformer", "power", "transformateur", "Dyn11")
            .Circle(10, 4, 7).Circle(10, -4, 7)
            .Line(0, 10, 0, 8).Line(0, 8, 4, 7).Line(10, 10, 10, 11).Line(20, 10, 20, 8).Line(20, 8, 16, 7)
            .Line(0, -10, 0, -8).Line(0, -8, 4, -7).Line(10, -10, 10, -11).Line(20, -10, 20, -8).Line(20, -8, 16, -7)
            .Pin("1U", 0, 10, PinDirection.Up).Pin("1V", 10, 15, PinDirection.Up).Pin("1W", 20, 10, PinDirection.Up)
            .Pin("2U", 0, -10, PinDirection.Down).Pin("2V", 10, -15, PinDirection.Down).Pin("2W", 20, -10, PinDirection.Down);
        t3.Line(10, 15, 10, 11).Line(10, -15, 10, -11);
        t3.Attr(SymbolAttributes.Rating, 22, 0, Core_Left(), height: 2, layer: StandardLayers.Annotations);
        yield return t3.Build();

        yield return new SymbolBuilder("IEC.PSU.ACDC", "Power supply AC/DC", SymbolCategory.PowerSupplies, "G")
            .Describe("Switched-mode power supply, AC input, DC output.", "power supply", "SMPS", "alimentation", "24V")
            .Rect(-10, -7.5, 10, 7.5).Line(-10, -7.5, 10, 7.5).Text(-5, 3.5, "~", 3.5).Text(5, -4, "=", 3.5)
            .Line(-5, 15, -5, 7.5).Line(0, 15, 0, 7.5).Line(5, 15, 5, 7.5).Line(-5, -15, -5, -7.5).Line(5, -15, 5, -7.5)
            .Pin("L", -5, 15, PinDirection.Up).Pin("N", 0, 15, PinDirection.Up).Pin("PE", 5, 15, PinDirection.Up)
            .Pin("+", -5, -15, PinDirection.Down, description: "DC positive output").Pin("-", 5, -15, PinDirection.Down, description: "DC negative output")
            .Build();

        var vfd = new SymbolBuilder("IEC.VFD", "Variable frequency drive", SymbolCategory.Drives, "T")
            .Describe("Frequency converter / variable speed drive for three-phase motors.", "VFD", "inverter", "drive", "variateur")
            .Rect(-5, -10, 25, 10).Line(-5, -10, 25, 10).Text(3, 4.5, "~", 3.5).Text(17, -5, "~", 3.5).Text(17, 5, "f", 2.5)
            .Line(0, 15, 0, 10).Line(10, 15, 10, 10).Line(20, 15, 20, 10).Line(0, -15, 0, -10).Line(10, -15, 10, -10).Line(20, -15, 20, -10)
            .Line(25, 5, 30, 5).Line(25, 0, 30, 0).Line(25, -5, 30, -5)
            .Pin("L1", 0, 15, PinDirection.Up).Pin("L2", 10, 15, PinDirection.Up).Pin("L3", 20, 15, PinDirection.Up)
            .Pin("U", 0, -15, PinDirection.Down).Pin("V", 10, -15, PinDirection.Down).Pin("W", 20, -15, PinDirection.Down)
            .Pin("DI1", 30, 5, PinDirection.Right, description: "Run command").Pin("AI1", 30, 0, PinDirection.Right, description: "Speed reference").Pin("COM", 30, -5, PinDirection.Right);
        vfd.Attr(SymbolAttributes.Tag, -7, 6).Attr(SymbolAttributes.Function, -7, 2, height: 1.8, layer: StandardLayers.Annotations)
            .Attr(SymbolAttributes.Rating, -7, -2, height: 2, layer: StandardLayers.Annotations);
        yield return vfd.Build();

        var ss = new SymbolBuilder("IEC.SOFTSTARTER", "Soft starter", SymbolCategory.Drives, "T")
            .Describe("Electronic soft starter for three-phase motors.", "soft starter", "démarreur progressif")
            .Rect(-5, -10, 25, 10).Text(10, 2.5, "SOFT", 2.5).Text(10, -2.5, "START", 2.5)
            .Line(0, 15, 0, 10).Line(10, 15, 10, 10).Line(20, 15, 20, 10).Line(0, -15, 0, -10).Line(10, -15, 10, -10).Line(20, -15, 20, -10)
            .Pin("1L1", 0, 15, PinDirection.Up).Pin("3L2", 10, 15, PinDirection.Up).Pin("5L3", 20, 15, PinDirection.Up)
            .Pin("2T1", 0, -15, PinDirection.Down).Pin("4T2", 10, -15, PinDirection.Down).Pin("6T3", 20, -15, PinDirection.Down);
        ss.Attr(SymbolAttributes.Tag, -7, 4).Attr(SymbolAttributes.Rating, -7, 0, height: 2, layer: StandardLayers.Annotations);
        yield return ss.Build();
    }

    private static IEnumerable<SymbolDefinition> Control()
    {
        yield return new SymbolBuilder("IEC.PB.NO", "Push button NO", SymbolCategory.ControlDevices, "S")
            .Describe("Manually operated push button, normally open (start).", "push button", "start", "bouton poussoir", "marche")
            .NoContact().Line(-2, 0.5, -7, 0.5, dashed: true).Line(-7, -2, -7, 3).Line(-7, 3, -9, 3).Line(-7, -2, -9, -2)
            .Pin("13", 0, 10, PinDirection.Up).Pin("14", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.PB.NC", "Push button NC", SymbolCategory.ControlDevices, "S")
            .Describe("Manually operated push button, normally closed (stop).", "push button", "stop", "bouton poussoir", "arrêt")
            .NcContact().Line(1.5, 0.5, -5, 0.5, dashed: true).Line(-5, -2, -5, 3).Line(-5, 3, -7, 3).Line(-5, -2, -7, -2)
            .Pin("11", 0, 10, PinDirection.Up).Pin("12", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.ESTOP", "Emergency stop push button", SymbolCategory.ControlDevices, "S")
            .Describe("Emergency stop device: NC contact, mushroom head, latching (positive opening).", "emergency stop", "e-stop", "arrêt d'urgence", "AU")
            .NcContact().Line(1.5, 0.5, -5, 0.5, dashed: true).Arc(-8, 0.5, 3, 270, 180).Line(-8, -2.5, -8, 3.5)
            .Pin("11", 0, 10, PinDirection.Up).Pin("12", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.SEL.2POS", "Selector switch 2-position", SymbolCategory.ControlDevices, "S")
            .Describe("Rotary selector switch, maintained, NO contact.", "selector", "switch", "commutateur", "sélecteur")
            .NoContact().Line(-2, 0.5, -7, 0.5, dashed: true).Poly(false, -9, -2, -7, -2, -7, 3, -5.5, 3)
            .Pin("13", 0, 10, PinDirection.Up).Pin("14", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.LS.NO", "Limit switch NO", SymbolCategory.ControlDevices, "B")
            .Describe("Position (limit) switch with mechanical actuator, NO contact.", "limit switch", "position", "fin de course")
            .NoContact().Line(-2, 0.5, -6, 0.5, dashed: true).Poly(true, -6, 0.5, -9, 3, -9, -2)
            .Pin("13", 0, 10, PinDirection.Up).Pin("14", 0, -10, PinDirection.Down).Build();
    }

    private static IEnumerable<SymbolDefinition> Sensors()
    {
        SymbolBuilder ThreeWire(string id, string name, string desc, params string[] kw) =>
            new SymbolBuilder(id, name, SymbolCategory.Sensors, "B").Describe(desc, kw)
                .Rect(-7.5, -5, 7.5, 5)
                .Line(-5, 10, -5, 5).Line(5, 10, 5, 5).Line(0, -10, 0, -5)
                .Pin("BN", -5, 10, PinDirection.Up, description: "+24 V DC (brown)")
                .Pin("BU", 5, 10, PinDirection.Up, description: "0 V (blue)")
                .Pin("BK", 0, -10, PinDirection.Down, description: "Switching output (black)");

        yield return ThreeWire("IEC.PROX", "Proximity sensor (3-wire PNP)", "Inductive proximity sensor, 3-wire DC, PNP output.", "proximity", "inductive", "détecteur", "capteur")
            .Poly(true, -2.5, 0, 0, 2.5, 2.5, 0, 0, -2.5).Line(-6, -3.5, -3.5, -3.5).Build();
        yield return ThreeWire("IEC.PHOTO", "Photoelectric sensor (3-wire PNP)", "Photoelectric (optical) sensor, 3-wire DC, PNP output.", "photoelectric", "optical", "cellule", "capteur")
            .Circle(-2, 0, 2).Line(0.5, 1.5, 4, 3).Line(0.5, -1.5, 4, -3).Build();

        yield return new SymbolBuilder("IEC.TEMP", "Temperature transmitter (4-20 mA)", SymbolCategory.Sensors, "B")
            .Describe("Two-wire temperature transmitter, 4-20 mA loop powered.", "temperature", "PT100", "transmitter", "température")
            .Circle(0, 0, 5).Text(0, 0, "θ", 4).Line(0, 10, 0, 5).Line(0, -10, 0, -5)
            .Pin("+", 0, 10, PinDirection.Up).Pin("-", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.PRESS", "Pressure transmitter (4-20 mA)", SymbolCategory.Sensors, "B")
            .Describe("Two-wire pressure transmitter, 4-20 mA loop powered.", "pressure", "transmitter", "pression")
            .Circle(0, 0, 5).Text(0, 0, "P", 4).Line(0, 10, 0, 5).Line(0, -10, 0, -5)
            .Pin("+", 0, 10, PinDirection.Up).Pin("-", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.LEVEL", "Level switch", SymbolCategory.Sensors, "B")
            .Describe("Float level switch, NO contact.", "level", "float", "niveau")
            .NoContact().Line(-2, 0.5, -6, 0.5, dashed: true).Circle(-8, 0.5, 2).Line(-10.5, -2.5, -5.5, -2.5)
            .Pin("13", 0, 10, PinDirection.Up).Pin("14", 0, -10, PinDirection.Down).Build();
    }

    private static IEnumerable<SymbolDefinition> Signaling()
    {
        yield return new SymbolBuilder("IEC.LAMP", "Indicator lamp", SymbolCategory.Signaling, "H")
            .Describe("Signal / pilot lamp.", "lamp", "indicator", "pilot light", "voyant")
            .Circle(0, 0, 4).Line(-2.83, -2.83, 2.83, 2.83).Line(-2.83, 2.83, 2.83, -2.83).Line(0, 10, 0, 4).Line(0, -10, 0, -4)
            .Pin("X1", 0, 10, PinDirection.Up).Pin("X2", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.HORN", "Horn / buzzer", SymbolCategory.Signaling, "H")
            .Describe("Audible signalling device.", "horn", "buzzer", "siren", "klaxon", "avertisseur")
            .Arc(0, -3, 6, 0, 180).Line(-6, -3, 6, -3).Line(0, 10, 0, 3).Line(0, -10, 0, -3)
            .Pin("X1", 0, 10, PinDirection.Up).Pin("X2", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.SOL.COIL", "Solenoid valve coil", SymbolCategory.Actuators, "Y")
            .Describe("Electrically operated (solenoid) valve coil.", "solenoid", "valve", "électrovanne")
            .Rect(-5, -3, 5, 3).Line(-5, -3, 5, 3).Line(0, 10, 0, 3).Line(0, -3, 0, -10)
            .Pin("A1", 0, 10, PinDirection.Up).Pin("A2", 0, -10, PinDirection.Down).Build();

        yield return new SymbolBuilder("IEC.HEATER", "Heating element", SymbolCategory.Actuators, "E")
            .Describe("Resistive heating element.", "heater", "resistance", "chauffage")
            .Rect(-2.5, -6, 2.5, 6).Line(-1.5, -4, -1.5, 4).Line(0, -4, 0, 4).Line(1.5, -4, 1.5, 4).Line(0, 10, 0, 6).Line(0, -6, 0, -10)
            .Pin("1", 0, 10, PinDirection.Up).Pin("2", 0, -10, PinDirection.Down).Build();
    }

    private static IEnumerable<SymbolDefinition> ConnectionSymbols()
    {
        var term = new SymbolBuilder("IEC.TERMINAL", "Terminal", SymbolCategory.Terminals, "X", SymbolRole.Terminal)
            .Describe("Feed-through terminal of a terminal strip. Top (1) and bottom (2) are the same potential.", "terminal", "borne", "terminal block")
            .Circle(0, 0, 1.2).Line(0, 5, 0, 1.2).Line(0, -1.2, 0, -5)
            .Pin("1", 0, 5, PinDirection.Up, showLabel: false).Pin("2", 0, -5, PinDirection.Down, showLabel: false)
            .Internal("1", "2")
            .Attr(SymbolAttributes.Tag, -2, 1.5, height: 2)
            .Attr(SymbolAttributes.TerminalNumber, 2, -1.5, Core_Left(), height: 2);
        yield return term.Build();

        var pe = new SymbolBuilder("IEC.TERMINAL.PE", "PE terminal", SymbolCategory.Terminals, "X", SymbolRole.Terminal)
            .Describe("Protective earth terminal (green/yellow).", "PE", "earth terminal", "borne terre")
            .Circle(0, 0, 1.2).Line(0, 5, 0, 1.2).Line(0, -1.2, 0, -5).Line(2.5, 0, 5, 0).Line(5, 1.5, 5, -1.5).Line(6, 1, 6, -1).Line(7, 0.5, 7, -0.5)
            .Pin("1", 0, 5, PinDirection.Up, showLabel: false).Pin("2", 0, -5, PinDirection.Down, showLabel: false)
            .Internal("1", "2")
            .Attr(SymbolAttributes.Tag, -2, 1.5, height: 2)
            .Attr(SymbolAttributes.TerminalNumber, -2, -1.5, height: 2);
        yield return pe.Build();

        yield return new SymbolBuilder("IEC.GND", "Protective earth", SymbolCategory.Potentials, "", SymbolRole.PotentialSource)
            .Potential("PE")
            .Describe("Protective earth connection. Defines the PE potential.", "earth", "ground", "PE", "terre")
            .Line(0, 5, 0, 0).Line(-4, 0, 4, 0).Line(-2.7, -1.3, 2.7, -1.3).Line(-1.3, -2.6, 1.3, -2.6)
            .Pin("1", 0, 5, PinDirection.Up, showLabel: false)
            .Build();

        yield return new SymbolBuilder("IEC.SUPPLY", "Potential feed", SymbolCategory.Potentials, "", SymbolRole.PotentialSource)
            .Potential("L1")
            .Describe("Named potential (L1, L2, L3, N, PE, +24V, 0V…). Identical names are connected throughout the project.", "potential", "supply", "net label", "potentiel")
            .Line(0, 0, 0, -5).Poly(true, true, -1.5, 0, 1.5, 0, 0, 2.5)
            .Pin("1", 0, -5, PinDirection.Down, showLabel: false)
            .Attr(SymbolAttributes.Potential, 0, 3.5, TextHAlign.Center, TextVAlign.Bottom, height: 2.5, layer: StandardLayers.WireNumbers)
            .Build();

        yield return new SymbolBuilder("IEC.INTERRUPT", "Interruption point", SymbolCategory.Potentials, "", SymbolRole.Interruption)
            .Describe("Interruption point / sheet connector. Points with the same name are connected; the cross-reference shows the counterpart.", "interruption", "off-page", "renvoi", "cross-reference")
            .Line(0, 0, 0, 2).Poly(false, -2, 4.5, 0, 2, 2, 4.5)
            .Pin("1", 0, 0, PinDirection.Down, showLabel: false)
            .Attr(SymbolAttributes.Name, 3, 4, Core_Left(), height: 2.5, layer: StandardLayers.WireNumbers)
            .Attr(SymbolAttributes.CrossReference, 3, 1, Core_Left(), height: 1.8, layer: StandardLayers.CrossReferences)
            .Build();

        yield return new SymbolBuilder("IEC.CONNECTOR", "Plug and socket", SymbolCategory.Connectors, "X", SymbolRole.Connector)
            .Describe("Plug-and-socket connector pin. Both sides are the same potential.", "connector", "plug", "socket", "connecteur")
            .Arc(0, 1.5, 2.5, 180, 180).Line(0, 10, 0, 4).Line(0, 4, 0, 1.5).Poly(true, true, -1.2, -1.5, 1.2, -1.5, 1.2, 0, -1.2, 0).Line(0, -1.5, 0, -10)
            .Pin("1", 0, 10, PinDirection.Up).Pin("2", 0, -10, PinDirection.Down)
            .Internal("1", "2").Build();

        yield return new SymbolBuilder("IEC.BLACKBOX", "Device box (generic)", SymbolCategory.General, "A")
            .Describe("Generic device outline with four terminals, for devices without a dedicated symbol.", "black box", "device", "appareil")
            .Rect(-5, -10, 15, 10, dashed: false)
            .Line(0, 15, 0, 10).Line(10, 15, 10, 10).Line(0, -15, 0, -10).Line(10, -15, 10, -10)
            .Pin("1", 0, 15, PinDirection.Up).Pin("2", 10, 15, PinDirection.Up).Pin("3", 0, -15, PinDirection.Down).Pin("4", 10, -15, PinDirection.Down)
            .Build();
    }

    private static IEnumerable<SymbolDefinition> Plc()
    {
        SymbolBuilder Channel(string id, string name, string label, bool input) =>
            new SymbolBuilder(id, name, SymbolCategory.Plc, "A", SymbolRole.PlcIo)
                .Describe($"PLC {name.ToLowerInvariant()} channel. Address and PLC tag are taken from the assigned PLC channel.", "PLC", "I/O", "automate", label)
                .Rect(-6, -4, 6, 4).Text(0, 0, label, 2.5)
                .Line(0, input ? 10 : -10, 0, input ? 4 : -4)
                .Pin(input ? "IN" : "OUT", 0, input ? 10 : -10, input ? PinDirection.Up : PinDirection.Down, showLabel: false)
                .Attr(SymbolAttributes.Tag, -8, 2)
                .Attr(SymbolAttributes.Address, 8, 1.5, Core_Left(), height: 2.2, layer: StandardLayers.Tags)
                .Attr(SymbolAttributes.PlcTag, 8, -1.8, Core_Left(), height: 1.8, layer: StandardLayers.Annotations);

        yield return Channel("IEC.PLC.DI", "Digital input", "DI", input: true).Build();
        yield return Channel("IEC.PLC.DO", "Digital output", "DO", input: false).Build();
        yield return Channel("IEC.PLC.AI", "Analog input", "AI", input: true).Build();
        yield return Channel("IEC.PLC.AO", "Analog output", "AO", input: false).Build();

        yield return new SymbolBuilder("IEC.PLC.PS", "PLC power supply terminals", SymbolCategory.Plc, "A")
            .Describe("PLC / I/O module supply terminals (L+ / M).", "PLC", "supply", "L+", "M")
            .Rect(-6, -4, 16, 4).Text(5, 0, "PLC 24V", 2.2)
            .Line(0, 10, 0, 4).Line(10, 10, 10, 4)
            .Pin("L+", 0, 10, PinDirection.Up).Pin("M", 10, 10, PinDirection.Up).Build();
    }

    // Short alias to keep the tables readable.
    private static CAD.Model.TextHAlign Core_Left() => TextHAlign.Left;
}

file static class SymbolBuilderExtensions
{
    public static SymbolBuilder Rect(this SymbolBuilder b, double x1, double y1, double x2, double y2, bool dashed) => b.Rect(x1, y1, x2, y2);
}
