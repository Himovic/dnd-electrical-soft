using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Editing;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.UnitTests.Schematic;

public class SchematicTests
{
    [Fact]
    public void Built_in_library_is_valid_and_complete()
    {
        var lib = BuiltInSymbolLibrary.Create();
        Assert.Empty(lib.Validate());
        Assert.True(lib.Symbols.Count >= 40);
        foreach (var id in new[] { "IEC.CB.3P", "IEC.FUSE.1P", "IEC.COIL", "IEC.KM.3P", "IEC.OL.3P", "IEC.MOTOR.3PH", "IEC.TR.1PH", "IEC.PSU.ACDC",
                                   "IEC.PB.NO", "IEC.ESTOP", "IEC.SEL.2POS", "IEC.LS.NO", "IEC.PROX", "IEC.PHOTO", "IEC.TEMP", "IEC.PRESS",
                                   "IEC.SOL.COIL", "IEC.TERMINAL", "IEC.GND", "IEC.CONNECTOR", "IEC.LAMP", "IEC.HORN", "IEC.PLC.DI", "IEC.PLC.DO",
                                   "IEC.PLC.AI", "IEC.PLC.AO", "IEC.VFD", "IEC.SOFTSTARTER", "IEC.DISC.3P" })
            Assert.NotNull(lib.Find(id));
    }

    [Fact]
    public void Symbol_library_json_roundtrip()
    {
        var lib = BuiltInSymbolLibrary.Create();
        var back = SymbolLibrary.FromJson(lib.ToJson());
        Assert.Equal(lib.Symbols.Count, back.Symbols.Count);
        Assert.Equal(lib.Find("IEC.MOTOR.3PH")!.Pins.Count, back.Find("IEC.MOTOR.3PH")!.Pins.Count);
        Assert.Empty(back.Validate());
    }

    [Fact]
    public void Custom_library_save_duplicate_versioning()
    {
        var dir = Directory.CreateTempSubdirectory("dndsym").FullName;
        try
        {
            var svc = new SymbolLibraryService(dir);
            var custom = svc.GetOrCreateCustomLibrary("acme", "ACME symbols");
            var copy = svc.Duplicate("IEC.LAMP", custom, "ACME.LAMP.GREEN", "Green lamp");
            Assert.Equal(1, copy.Version);
            copy.Description = "Changed";
            svc.SaveSymbol(custom, copy.Clone());
            Assert.Equal(2, svc.Find("ACME.LAMP.GREEN")!.Version);
            Assert.Throws<InvalidOperationException>(() => svc.SaveSymbol(svc.Libraries.First(l => l.IsReadOnly), copy));

            var reloaded = new SymbolLibraryService(dir);
            Assert.NotNull(reloaded.Find("ACME.LAMP.GREEN"));
            Assert.Empty(reloaded.LoadErrors);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Instance_transform_rotation_and_mirror_place_pins_correctly()
    {
        var def = BuiltInSymbolLibrary.Create().Find("IEC.COIL")!;
        var inst = SchematicService.CreateInstance(def, new Point2(100, 100));
        Assert.Equal(new Point2(100, 110), inst.GetPinPosition("A1"));
        inst.Transform(Matrix2.Rotation(90, new Point2(100, 100)));
        Assert.Equal(90, inst.Rotation, 9);
        Assert.True(inst.GetPinPosition("A1")!.Value.AlmostEquals(new Point2(90, 100), 1e-9));
        var cb = SchematicService.CreateInstance(BuiltInSymbolLibrary.Create().Find("IEC.CB.3P")!, new Point2(0, 0));
        cb.Transform(Matrix2.MirrorX(0));
        Assert.True(cb.Mirrored);
        Assert.True(cb.GetPinPosition("3")!.Value.AlmostEquals(new Point2(-10, 10), 1e-9));
    }

    [Fact]
    public void Connectivity_identifies_potentials_through_explicit_references()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var g = ConnectivityGraph.Build(c.Project);

        Assert.True(g.AreConnected(c.Breaker.Id, "2", c.MainContacts.Id, "1"));
        Assert.False(g.AreConnected(c.MainContacts.Id, "1", c.MainContacts.Id, "2")); // open contact
        Assert.Equal("L1", g.PotentialOfPin(c.Breaker.Id, "1")!.Name);
        Assert.Equal("+24V", g.PotentialOfPin(c.StopButton.Id, "11")!.Name);
        Assert.Equal("0V", g.PotentialOfPin(c.Coil.Id, "A2")!.Name);
        // Start button and hold contact are in parallel: same potentials at both ends.
        Assert.True(g.AreConnected(c.StartButton.Id, "13", c.HoldContact.Id, "13"));
        Assert.True(g.AreConnected(c.StartButton.Id, "14", c.Coil.Id, "A1"));
        Assert.Empty(g.BrokenReferences);
        Assert.DoesNotContain(g.Potentials, p => p.HasNameConflict);
    }

    [Fact]
    public void Visual_overlap_without_reference_is_not_a_connection()
    {
        var svc = SchematicTestKit.NewService();
        var p = SchematicTestKit.NewProject();
        var s = svc.AddSheet(p, "t");
        var a = svc.PlaceSymbol(p, s, "IEC.LAMP", new Point2(100, 100));
        var b = svc.PlaceSymbol(p, s, "IEC.LAMP", new Point2(100, 80)); // b's X1 pin lies exactly on a's X2 pin
        Assert.True(a.GetPinPosition("X2")!.Value.AlmostEquals(b.GetPinPosition("X1")!.Value, 1e-9));
        var g = ConnectivityGraph.Build(p);
        Assert.False(g.AreConnected(a.Id, "X2", b.Id, "X1"));
    }

    [Fact]
    public void Short_circuit_between_named_supplies_is_detected()
    {
        var svc = SchematicTestKit.NewService();
        var p = SchematicTestKit.NewProject();
        var s = svc.AddSheet(p, "t");
        var l1 = svc.PlaceSymbol(p, s, "IEC.SUPPLY", new Point2(50, 200), values: new Dictionary<string, string> { ["POTENTIAL"] = "L1" });
        var n = svc.PlaceSymbol(p, s, "IEC.SUPPLY", new Point2(80, 200), values: new Dictionary<string, string> { ["POTENTIAL"] = "N" });
        svc.Connect(s, l1, "1", n, "1");
        var g = ConnectivityGraph.Build(p);
        Assert.Contains(g.Potentials, pot => pot.HasNameConflict);
    }

    [Fact]
    public void Interruption_points_connect_across_sheets_and_show_cross_reference()
    {
        var svc = SchematicTestKit.NewService();
        var p = SchematicTestKit.NewProject();
        var s1 = svc.AddSheet(p, "a");
        var s2 = svc.AddSheet(p, "b");
        var lampA = svc.PlaceSymbol(p, s1, "IEC.LAMP", new Point2(100, 150));
        var ipA = svc.PlaceSymbol(p, s1, "IEC.INTERRUPT", new Point2(100, 120), rotation: 180, values: new Dictionary<string, string> { ["NAME"] = "X10" });
        var ipB = svc.PlaceSymbol(p, s2, "IEC.INTERRUPT", new Point2(200, 200), values: new Dictionary<string, string> { ["NAME"] = "X10" });
        var lampB = svc.PlaceSymbol(p, s2, "IEC.LAMP", new Point2(200, 170));
        svc.Connect(s1, lampA, "X2", ipA, "1");
        svc.Connect(s2, ipB, "1", lampB, "X1");
        var g = svc.Binder.BindWithConnectivity(p);
        Assert.True(g.AreConnected(lampA.Id, "X2", lampB.Id, "X1"));
        Assert.Equal("/2.5", ipA.Display[SymbolAttributes.CrossReference]);
        Assert.Equal("/1.3", ipB.Display[SymbolAttributes.CrossReference]);
    }

    [Fact]
    public void Coil_contact_cross_references_are_bound()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        // Coil shows its contacts: power contacts on sheet 1, hold contact on sheet 2.
        Assert.Equal(2, c.Coil.ContactMirror.Count);
        Assert.Contains(c.Coil.ContactMirror, l => l.StartsWith("PWR 1-2") && l.EndsWith("/1.2"));
        Assert.Contains(c.Coil.ContactMirror, l => l.StartsWith("NO 13-14") && l.EndsWith("/2.3"));
        // Contacts show where the coil is.
        Assert.EndsWith("/2.3", c.HoldContact.Display[SymbolAttributes.Tag]);
        Assert.StartsWith("-K1", c.MainContacts.Display[SymbolAttributes.Tag]);
    }

    [Fact]
    public void Device_tags_are_generated_per_prefix()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var tags = c.Project.Devices.Select(d => d.Tag).OrderBy(t => t).ToList();
        Assert.Contains("-Q1", tags);
        Assert.Contains("-K1", tags);
        Assert.Contains("-F1", tags);
        Assert.Contains("-M1", tags);
        Assert.Contains("-S1", tags);
        Assert.Contains("-S2", tags);
        Assert.Equal(6, c.Project.Devices.Count); // coil + contacts share -K1
        Assert.Empty(DeviceTaggingService.DuplicateTags(c.Project));
    }

    [Fact]
    public void Wire_numbering_per_potential_uses_names_and_sequence()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var g = ConnectivityGraph.Build(c.Project);
        var r = WireNumberingService.Apply(c.Project, g);
        Assert.Empty(r.Warnings);
        var wires = c.Project.Sheets.SelectMany(s => s.Drawing.Entities.OfType<WireEntity>()).ToList();
        Assert.All(wires, w => Assert.NotEmpty(w.WireNumber));
        Assert.Equal(3, wires.Count(w => w.WireNumber is "L1" or "L2" or "L3"));
        // All wires of a potential share the number.
        foreach (var pot in g.Potentials) Assert.True(pot.WireNumbers.Count <= 1);
        // Numbered potentials: 3 (Q1→K1) + 3 (K1→F1) + 3 (F1→M1) + 2 control (S1-S2 node, S2-K1 node) = 11
        Assert.Equal(11, wires.Select(w => w.WireNumber).Where(n => int.TryParse(n, out _)).Distinct().Count());
        // Second run without renumbering keeps numbers.
        var before = wires.ToDictionary(w => w.Id, w => w.WireNumber);
        WireNumberingService.Apply(c.Project, ConnectivityGraph.Build(c.Project));
        Assert.All(wires, w => Assert.Equal(before[w.Id], w.WireNumber));
    }

    [Theory]
    [InlineData("{GSEQ}", 7, 3, 4, 2, "7")]
    [InlineData("{SHEET}.{COL}{SEQ}", 7, 3, 4, 2, "3.42")]
    [InlineData("W{GSEQ:000}", 7, 3, 4, 2, "W007")]
    public void Wire_number_format_tokens(string format, int g, int sheet, int col, int seq, string expected) =>
        Assert.Equal(expected, WireNumberingService.FormatNumber(format, g, sheet, col, seq));

    [Fact]
    public void Wired_neighbours_resolve_through_junctions()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var g = ConnectivityGraph.Build(c.Project);
        var a1 = g.GetPin(c.Coil.Id, "A1")!;
        var n = g.WiredNeighbours(a1).Select(x => x.Designation).ToList();
        Assert.Equal(new[] { "-K1:14", "-S2:14" }, n);
    }

    [Fact]
    public void Project_drawing_serialization_preserves_schematic_entities()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var json = CadJson.SerializeDrawing(c.Control.Drawing);
        var back = CadJson.DeserializeDrawing(json);
        Assert.Equal(c.Control.Drawing.Entities.Count, back.Entities.Count);
        var coil = back.Entities.OfType<SymbolInstanceEntity>().Single(i => i.SymbolId == "IEC.COIL");
        Assert.Equal(c.Coil.DeviceId, coil.DeviceId);
        Assert.Equal(c.Coil.Id, coil.Id);
        Assert.Equal(2, back.Entities.OfType<JunctionEntity>().Count());
        var w = back.Entities.OfType<WireEntity>().First(x => x.End.JunctionId is not null);
        Assert.NotNull(w.End.JunctionId);
    }

    [Fact]
    public void Dxf_export_contains_blocks_with_tag_attributes()
    {
        var c = SchematicTestKit.BuildMotorCircuit(SchematicTestKit.NewService());
        var fields = FieldResolver.CreateResolver(c.Project, c.Control);
        var dxf = DxfWriter.Write(c.Control.Drawing, fields);
        Assert.Contains("INSERT", dxf);
        Assert.Contains("DND_IEC_COIL_V1", dxf);
        Assert.Contains("ATTRIB", dxf);
        Assert.Contains("-K1", dxf);
        Assert.Contains("Unit test project", dxf); // title block field resolved
        var back = DxfReader.Read(dxf);
        Assert.True(back.BlockReferencesExpanded >= 6);
        Assert.Contains(back.Entities.OfType<DND.Electrical.CAD.Entities.TextEntity>(), t => t.Value == "-K1");
    }

    // ------------------------------------------------------------------ interactive editor

    private static (SchematicEditor ed, ElectricalProject p) NewEditor()
    {
        var svc = SchematicTestKit.NewService();
        var p = SchematicTestKit.NewProject();
        var sheet = svc.AddSheet(p, "Editor test");
        var ed = new SchematicEditor(p, sheet, svc);
        ed.Viewport.SetSize(1600, 1200);
        ed.Viewport.SetView(4, new Point2(0, 1200));
        return (ed, p);
    }

    private static Point2 S(CadEditor ed, double x, double y) => ed.Viewport.ToScreen(new Point2(x, y));

    [Fact]
    public void Insert_tool_creates_devices_and_undo_removes_them()
    {
        var (ed, p) = NewEditor();
        var cb = SchematicTestKit.Library.Find("IEC.CB.1P")!;
        ed.SetTool(new InsertSymbolTool(cb));
        ed.PointerMove(S(ed, 50, 200));
        ed.PointerDown(S(ed, 50, 200), PointerButton.Left);
        ed.PointerDown(S(ed, 100, 200), PointerButton.Left);
        Assert.Equal(new[] { "-Q1", "-Q2" }, p.Devices.Select(d => d.Tag));
        Assert.Equal(2, ed.Drawing.Entities.OfType<SymbolInstanceEntity>().Count());
        ed.UndoLast();
        Assert.Single(p.Devices);
        Assert.Single(ed.Drawing.Entities.OfType<SymbolInstanceEntity>());
    }

    [Fact]
    public void Wire_tool_connects_pins_and_creates_t_junction()
    {
        var (ed, p) = NewEditor();
        var lamp = SchematicTestKit.Library.Find("IEC.LAMP")!;
        var h1 = ed.InsertSymbol(lamp, new Point2(100, 200));
        var h2 = ed.InsertSymbol(lamp, new Point2(150, 150));
        var h3 = ed.InsertSymbol(lamp, new Point2(200, 120));

        ed.SetTool(new WireTool());
        ed.PointerDown(S(ed, 100, 190), PointerButton.Left); // H1:X2
        ed.PointerMove(S(ed, 150, 160));
        ed.PointerDown(S(ed, 150, 160), PointerButton.Left); // H2:X1
        var w = ed.Drawing.Entities.OfType<WireEntity>().Single();
        Assert.Equal(h1.Id, w.Start.InstanceId);
        Assert.Equal("X2", w.Start.PinId);
        Assert.Equal(h2.Id, w.End.InstanceId);
        Assert.Equal("X1", w.End.PinId);
        Assert.All(w.Segments(), s => Assert.True(Math.Abs(s.A.X - s.B.X) < 1e-9 || Math.Abs(s.A.Y - s.B.Y) < 1e-9));

        // Start a wire on the middle of the existing horizontal segment → junction + split.
        var horizontal = w.Segments().First(s => Math.Abs(s.A.Y - s.B.Y) < 1e-9);
        var mid = Point2.Lerp(horizontal.A, horizontal.B, 0.5);
        ed.PointerDown(S(ed, mid.X, mid.Y), PointerButton.Left);
        ed.PointerDown(S(ed, 200, 130), PointerButton.Left); // H3:X1
        Assert.Single(ed.Drawing.Entities.OfType<JunctionEntity>());
        Assert.Equal(3, ed.Drawing.Entities.OfType<WireEntity>().Count());
        var g = ConnectivityGraph.Build(p);
        Assert.True(g.AreConnected(h1.Id, "X2", h3.Id, "X1"));
        Assert.True(g.AreConnected(h2.Id, "X1", h3.Id, "X1"));
    }

    [Fact]
    public void Moving_a_symbol_drags_connected_wires_orthogonally()
    {
        var (ed, p) = NewEditor();
        var lamp = SchematicTestKit.Library.Find("IEC.LAMP")!;
        var a = ed.InsertSymbol(lamp, new Point2(100, 200));
        var b = ed.InsertSymbol(lamp, new Point2(100, 150));
        var w = ed.Schematic.Connect(ed.Sheet, a, "X2", b, "X1");
        ed.Selection.Set(new[] { b.Id });
        ed.MoveSelection(new Point2(20, -10));
        var moved = ed.Drawing.Find<WireEntity>(w.Id)!;
        Assert.True(moved.EndPoint.AlmostEquals(b.GetPinPosition("X1")!.Value, 1e-9));
        Assert.True(moved.StartPoint.AlmostEquals(a.GetPinPosition("X2")!.Value, 1e-9));
        Assert.All(moved.Segments(), s => Assert.True(Math.Abs(s.A.X - s.B.X) < 1e-9 || Math.Abs(s.A.Y - s.B.Y) < 1e-9));
        ed.UndoLast();
        Assert.True(ed.Drawing.Find<WireEntity>(w.Id)!.EndPoint.AlmostEquals(new Point2(100, 160), 1e-9));
    }

    [Fact]
    public void Deleting_symbol_detaches_wires_and_removes_orphan_device_undoably()
    {
        var (ed, p) = NewEditor();
        var lamp = SchematicTestKit.Library.Find("IEC.LAMP")!;
        var a = ed.InsertSymbol(lamp, new Point2(100, 200));
        var b = ed.InsertSymbol(lamp, new Point2(100, 150));
        var w = ed.Schematic.Connect(ed.Sheet, a, "X2", b, "X1");
        ed.Selection.Set(new[] { b.Id });
        ed.DeleteSelection();
        Assert.False(ed.Drawing.Find<WireEntity>(w.Id)!.End.IsConnected);
        Assert.Single(p.Devices);
        ed.UndoLast();
        Assert.Equal(2, p.Devices.Count);
        Assert.Equal(b.Id, ed.Drawing.Find<WireEntity>(w.Id)!.End.InstanceId);
    }

    [Fact]
    public void Copy_paste_creates_new_devices_and_remaps_internal_wires()
    {
        var (ed, p) = NewEditor();
        var lamp = SchematicTestKit.Library.Find("IEC.LAMP")!;
        var a = ed.InsertSymbol(lamp, new Point2(100, 200));
        var b = ed.InsertSymbol(lamp, new Point2(100, 150));
        ed.Schematic.Connect(ed.Sheet, a, "X2", b, "X1");
        ed.SelectAll();
        ed.Selection.Set(ed.Selection.Ids.Where(id => ed.Drawing.Find(id) is SymbolInstanceEntity or WireEntity));
        ed.CopySelection();
        var pasted = ed.Paste(offset: new Point2(50, 0));
        Assert.Equal(4, p.Devices.Count);
        var pw = pasted.OfType<WireEntity>().Single();
        var pastedIds = pasted.Select(e => e.Id).ToHashSet();
        Assert.Contains(pw.Start.InstanceId!.Value, pastedIds);
        Assert.Contains(pw.End.InstanceId!.Value, pastedIds);
    }

    [Fact]
    public void Editor_number_wires_is_undoable()
    {
        var (ed, p) = NewEditor();
        var lamp = SchematicTestKit.Library.Find("IEC.LAMP")!;
        var a = ed.InsertSymbol(lamp, new Point2(100, 200));
        var b = ed.InsertSymbol(lamp, new Point2(100, 150));
        var w = ed.Schematic.Connect(ed.Sheet, a, "X2", b, "X1");
        ed.NumberWires(renumberExisting: false);
        Assert.Equal("1", w.WireNumber);
        ed.UndoLast();
        Assert.Equal(string.Empty, w.WireNumber);
    }
}
