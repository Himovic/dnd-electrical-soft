using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.PLC;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Symbols;
using DND.Electrical.UnitTests.Schematic;
using DND.Electrical.Wiring;

namespace DND.Electrical.UnitTests.Engineering;

public class EngineeringModuleTests
{
    [Fact]
    public void Plc_addresses_follow_profiles()
    {
        var p = new ElectricalProject();
        var st = PlcService.CreateStation(p, "-A1", "PLC", "siemens-style");
        var rack = st.Racks[0];
        PlcService.AddModule(st, rack, "GEN-CPU");
        var di = PlcService.AddModule(st, rack, "GEN-DI16");
        var dq = PlcService.AddModule(st, rack, "GEN-DO8");
        var ai = PlcService.AddModule(st, rack, "GEN-AI4");
        Assert.Equal("%I0.0", di.Channels[0].Address);
        Assert.Equal("%I1.7", di.Channels[15].Address);
        Assert.Equal("%Q0.7", dq.Channels[7].Address);
        Assert.Equal("%IW64", ai.Channels[0].Address);
        Assert.Equal("%IW66", ai.Channels[1].Address);
        Assert.DoesNotContain(PlcService.Validate(p), i => i.Severity == "Error");

        st.ProfileId = "rockwell-style";
        PlcService.AssignAddresses(st);
        Assert.Equal("Local:1:I.Data.3", di.Channels[3].Address);
        st.ProfileId = "mitsubishi-style";
        PlcService.AssignAddresses(st);
        Assert.Equal("X017", di.Channels[15].Address); // octal
    }

    [Fact]
    public void Plc_validation_reports_duplicates()
    {
        var p = new ElectricalProject();
        var st = PlcService.CreateStation(p, "-A1", "PLC", "generic-iec");
        var di = PlcService.AddModule(st, st.Racks[0], "GEN-DI8");
        di.Channels[1].Address = di.Channels[0].Address;
        Assert.Equal(2, PlcService.Validate(p).Count(i => i.Severity == "Error" && i.Message.Contains("duplicate")));
    }

    [Fact]
    public void Cabinet_rail_placement_and_collisions()
    {
        var cab = new Cabinet { PlateWidth = 500, PlateHeight = 600 };
        var rail = CabinetLayoutService.AddRail(cab, 20, 400, 300);
        var a = new CabinetItem { Name = "-Q1", Width = 54, Height = 90 };
        var b = new CabinetItem { Name = "-K1", Width = 45, Height = 80 };
        Assert.True(CabinetLayoutService.PlaceOnRail(cab, rail, a));
        Assert.True(CabinetLayoutService.PlaceOnRail(cab, rail, b));
        Assert.Equal(20, a.X);
        Assert.Equal(74, b.X);
        Assert.Empty(CabinetLayoutService.Check(cab));
        var big = new CabinetItem { Name = "-T1", Width = 250, Height = 90 };
        Assert.False(CabinetLayoutService.PlaceOnRail(cab, rail, big)); // rail full
        var stray = CabinetLayoutService.Add(cab, new CabinetItem { Name = "-G1", X = 60, Y = 420, Width = 50, Height = 50 });
        Assert.Contains(CabinetLayoutService.Check(cab), i => i.Severity == "Error" && i.ItemId == a.Id || i.OtherItemId == stray.Id);
        var outside = CabinetLayoutService.Add(cab, new CabinetItem { Name = "-X9", X = 480, Y = 10, Width = 50, Height = 50 });
        Assert.Contains(CabinetLayoutService.Check(cab), i => i.ItemId == outside.Id && i.Message.Contains("outside"));
        Assert.True(CabinetLayoutService.Utilization(cab).Percent > 0);
    }

    [Fact]
    public void Cabinet_clearance_warning()
    {
        var cab = new Cabinet { PlateWidth = 500, PlateHeight = 600, ClearanceMm = 10 };
        CabinetLayoutService.Add(cab, new CabinetItem { Name = "A", X = 10, Y = 10, Width = 50, Height = 50 });
        CabinetLayoutService.Add(cab, new CabinetItem { Name = "B", X = 65, Y = 10, Width = 50, Height = 50 });
        Assert.Contains(CabinetLayoutService.Check(cab), i => i.Severity == "Warning");
    }

    [Fact]
    public void Cable_core_assignment_and_schedule()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var cable = CableService.CreateCable(c.Project, "H07RN-F", 4, 2.5, 25, "+CAB1", "Pump");
        var motorWires = c.Power.Drawing.Entities.OfType<WireEntity>().Where(w => w.End.InstanceId == c.Motor.Id).ToList();
        foreach (var w in motorWires) Assert.True(CableService.AssignWire(cable, w).IsSuccess);
        var g = ConnectivityGraph.Build(c.Project);
        var sched = WiringReports.CableSchedule(c.Project).Single();
        Assert.Equal(3, sched.UsedCores);
        Assert.Equal(1, sched.SpareCores);
        var cores = WiringReports.CableCores(c.Project, g);
        Assert.Equal(4, cores.Count);
        Assert.Contains(cores, r => r.To == "-M1:U1");
        Assert.Equal("spare", cores.Single(r => r.Marking == "GNYE").WireNumber);
    }

    [Fact]
    public void Connection_list_from_graph()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var g = ConnectivityGraph.Build(c.Project);
        var rows = WiringReports.ConnectionList(c.Project, g);
        Assert.Equal(g.Wires.Count, rows.Count);
        Assert.Contains(rows, r => r.From == "-Q1:2" && r.To == "-K1:1");
        Assert.Contains(rows, r => r.From.Contains("-K1:14") || r.To.Contains("-K1:14"));
    }

    [Fact]
    public void Terminal_diagram_from_actual_connectivity()
    {
        var svc = SchematicTestKit.NewService();
        var p = SchematicTestKit.NewProject();
        var s = svc.AddSheet(p, "terminals");
        var lamp = svc.PlaceSymbol(p, s, "IEC.LAMP", new Point2(100, 200));
        var x1 = svc.PlaceSymbol(p, s, "IEC.TERMINAL", new Point2(100, 170), tag: "-X1", values: new Dictionary<string, string> { [SymbolAttributes.TerminalNumber] = "1" });
        var sensor = svc.PlaceSymbol(p, s, "IEC.LAMP", new Point2(100, 140));
        svc.Connect(s, lamp, "X2", x1, "1");
        svc.Connect(s, x1, "2", sensor, "X1");
        svc.Binder.BindProject(p);
        Assert.Equal(1, CableService.SyncTerminalStrips(p));
        var g = ConnectivityGraph.Build(p);
        var row = WiringReports.TerminalDiagram(p, g).Single();
        Assert.Equal("-X1", row.Strip);
        Assert.Equal("1", row.Terminal);
        Assert.Equal("-H1:X2", row.InternalTargets);
        Assert.Equal("-H2:X1", row.ExternalTargets);
        Assert.True(g.AreConnected(lamp.Id, "X2", sensor.Id, "X1")); // terminal passes the potential through
    }
}
