using DND.Electrical.CAD.Dxf;
using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.CAD.Tools;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.UnitTests.Cad;

public class CadEngineTests
{
    private static CadEditor NewEditor()
    {
        var ed = new CadEditor(new CadDrawing());
        ed.Viewport.SetSize(1000, 800);
        ed.Viewport.SetView(4, new Point2(0, 800)); // 4 px/mm, world origin at bottom-left
        return ed;
    }

    private static Point2 S(CadEditor ed, double x, double y) => ed.Viewport.ToScreen(new Point2(x, y));

    [Fact]
    public void Viewport_roundtrip_and_zoom_keeps_point_under_cursor()
    {
        var vp = new Viewport();
        vp.SetSize(800, 600);
        var screen = new Point2(123, 456);
        var before = vp.ToWorld(screen);
        vp.ZoomAt(screen, 2.5);
        Assert.True(vp.ToWorld(screen).AlmostEquals(before, 1e-9));
    }

    [Fact]
    public void Spatial_index_finds_entities_after_modification()
    {
        var d = new CadDrawing();
        var line = new LineEntity(new Point2(0, 0), new Point2(10, 0));
        d.Add(line);
        Assert.Contains(line, d.Query(new Rect2(4, -1, 6, 1)));
        line.Transform(Matrix2.Translation(1000, 1000));
        d.NotifyModified(line);
        Assert.DoesNotContain(line, d.Query(new Rect2(4, -1, 6, 1)));
        Assert.Contains(line, d.Query(new Rect2(1004, 999, 1006, 1001)));
    }

    [Fact]
    public void Spatial_index_handles_thousands_of_entities()
    {
        var d = new CadDrawing();
        for (int i = 0; i < 5000; i++) d.Add(new CircleEntity(new Point2(i % 100 * 10, i / 100 * 10), 2));
        var hits = d.Query(new Rect2(-1, -1, 21, 21)).ToList();
        Assert.Equal(9, hits.Count);
        Assert.NotNull(d.HitTest(new Point2(502, 300), 0.5));
    }

    [Fact]
    public void Line_tool_creates_chained_lines_with_grid_snap_and_undo()
    {
        var ed = NewEditor();
        ed.SetTool(new LineTool());
        ed.PointerDown(S(ed, 10.4, 10.3), PointerButton.Left);
        ed.PointerDown(S(ed, 50.2, 11.0), PointerButton.Left); // ortho → horizontal
        ed.PointerDown(S(ed, 50.0, 40.6), PointerButton.Left);
        ed.KeyDown(ToolKey.Escape);

        var lines = ed.Drawing.Entities.OfType<LineEntity>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(new Point2(10, 10), lines[0].Start);
        Assert.Equal(new Point2(50, 10), lines[0].End);
        Assert.Equal(new Point2(50, 40), lines[1].End);

        Assert.True(ed.UndoLast());
        Assert.Single(ed.Drawing.Entities);
        Assert.True(ed.RedoLast());
        Assert.Equal(2, ed.Drawing.Entities.Count);
    }

    [Fact]
    public void Object_snap_prefers_endpoint_over_grid()
    {
        var ed = NewEditor();
        ed.Drawing.Add(new LineEntity(new Point2(12.3, 7.7), new Point2(40, 7.7)));
        var snap = ed.ComputeSnap(S(ed, 12.9, 8.1), null);
        Assert.Equal(SnapKind.Endpoint, snap.Kind);
        Assert.Equal(new Point2(12.3, 7.7), snap.Point);
    }

    [Fact]
    public void Window_and_crossing_selection()
    {
        var ed = NewEditor();
        var inside = new LineEntity(new Point2(10, 10), new Point2(20, 10));
        var crossing = new LineEntity(new Point2(15, 5), new Point2(100, 5));
        ed.Drawing.AddRange(new CadEntity[] { inside, crossing });

        ed.SelectWindow(new Rect2(0, 0, 30, 30), crossing: false, add: false);
        Assert.Equal(new[] { inside.Id }, ed.Selection.Ids);

        ed.SelectWindow(new Rect2(0, 0, 30, 30), crossing: true, add: false);
        Assert.Equal(2, ed.Selection.Count);
    }

    [Fact]
    public void Select_tool_drag_moves_selection_and_is_undoable()
    {
        var ed = NewEditor();
        var c = new CircleEntity(new Point2(20, 20), 5);
        ed.Drawing.Add(c);
        ed.PointerDown(S(ed, 25, 20), PointerButton.Left);
        ed.PointerMove(S(ed, 35, 20));
        ed.PointerUp(S(ed, 35, 20), PointerButton.Left);
        Assert.Equal(new Point2(30, 20), ed.Drawing.Find<CircleEntity>(c.Id)!.Center);
        ed.UndoLast();
        Assert.Equal(new Point2(20, 20), ed.Drawing.Find<CircleEntity>(c.Id)!.Center);
    }

    [Fact]
    public void Rotate_mirror_copy_paste_group()
    {
        var ed = NewEditor();
        var l = new LineEntity(new Point2(0, 0), new Point2(10, 0));
        ed.Drawing.Add(l);
        ed.Selection.Set(new[] { l.Id });
        ed.TransformSelection(Matrix2.Rotation(90, Point2.Zero), "rot");
        Assert.True(ed.Drawing.Find<LineEntity>(l.Id)!.End.AlmostEquals(new Point2(0, 10), 1e-9));

        ed.CopySelection();
        var pasted = ed.Paste(offset: new Point2(5, 0));
        Assert.Single(pasted);
        Assert.NotEqual(l.Id, pasted[0].Id);
        Assert.Equal(2, ed.Drawing.Entities.Count);

        ed.SelectAll();
        var g = ed.GroupSelection("pair");
        Assert.NotNull(g);
        Assert.Single(ed.Drawing.Entities);
        ed.UngroupSelection();
        Assert.Equal(2, ed.Drawing.Entities.Count);
        ed.UndoLast(); // ungroup
        ed.UndoLast(); // group
        Assert.Equal(2, ed.Drawing.Entities.Count);
        Assert.Contains(ed.Drawing.Entities, e => e.Id == l.Id);
    }

    [Fact]
    public void Arc_from_three_points_and_mirror()
    {
        var arc = ArcEntity.FromThreePoints(new Point2(10, 0), new Point2(0, 10), new Point2(-10, 0))!;
        Assert.Equal(0, arc.Center.Length, 9);
        Assert.Equal(10, arc.Radius, 9);
        Assert.Equal(180, arc.SweepAngle, 6);
        arc.Transform(Matrix2.MirrorY());
        // Mirroring the upper half-circle gives the lower half: points at 0° and 180°, passing 270°.
        Assert.True(arc.GetBounds().MinY < -9.99);
        Assert.Equal(180, arc.SweepAngle, 6);
    }

    [Fact]
    public void Undo_dirty_tracking()
    {
        var u = new UndoManager();
        var d = new CadDrawing();
        Assert.False(u.IsDirty);
        u.Execute(new AddEntitiesAction(d, new[] { new LineEntity() }));
        Assert.True(u.IsDirty);
        u.MarkSaved();
        Assert.False(u.IsDirty);
        u.Undo();
        Assert.True(u.IsDirty);
        u.Redo();
        Assert.False(u.IsDirty);
    }

    [Fact]
    public void Json_roundtrip_preserves_all_entity_types()
    {
        var d = new CadDrawing();
        d.Add(new LineEntity(new Point2(1, 2), new Point2(3, 4)) { Color = CadColor.Red });
        d.Add(new PolylineEntity(new[] { new Point2(0, 0), new Point2(5, 0), new Point2(5, 5) }, true) { Filled = true });
        d.Add(new CircleEntity(new Point2(1, 1), 3));
        d.Add(new ArcEntity(new Point2(0, 0), 4, 30, 120));
        d.Add(new TextEntity(new Point2(2, 2), "{PROJECT.NAME}", 3.5) { HAlign = TextHAlign.Center });
        d.Add(new DimensionEntity { Start = new Point2(0, 0), End = new Point2(50, 0), Offset = 8 });
        d.Add(new LeaderEntity { Points = { new Point2(0, 0), new Point2(10, 10) }, Text = "Note" });
        d.Add(new GroupEntity { Children = { new LineEntity(new Point2(0, 0), new Point2(1, 1)) } });
        d.Add(new ImageEntity { ImageKey = "branding:logo" });

        var json = CadJson.SerializeDrawing(d);
        var back = CadJson.DeserializeDrawing(json);
        Assert.Equal(d.Entities.Count, back.Entities.Count);
        Assert.Equal(d.Entities.Select(e => e.TypeName), back.Entities.Select(e => e.TypeName));
        Assert.Equal(CadColor.Red, back.Entities[0].Color);
        Assert.Equal(TextHAlign.Center, ((TextEntity)back.Entities[4]).HAlign);
        Assert.Equal(d.Layers.Count, back.Layers.Count);
        Assert.NotNull(back.Find(d.Entities[3].Id)); // index rebuilt after deserialization
    }

    [Fact]
    public void Command_processor_handles_commands_and_coordinates()
    {
        var ed = NewEditor();
        var cp = new CadCommandProcessor();
        cp.Execute(ed, "LINE");
        cp.Execute(ed, "0,0");
        cp.Execute(ed, "@100,0");
        cp.Execute(ed, "@50<90");
        cp.Execute(ed, "");
        var lines = ed.Drawing.Entities.OfType<LineEntity>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.True(lines[1].End.AlmostEquals(new Point2(100, 50), 1e-9));
        Assert.StartsWith("Unknown command", cp.Execute(ed, "FOOBAR"));
    }

    [Fact]
    public void Dxf_roundtrip_preserves_geometry_and_layers()
    {
        var d = new CadDrawing();
        d.Add(new LineEntity(new Point2(0, 0), new Point2(100, 0)) { Layer = StandardLayers.Wires });
        d.Add(new CircleEntity(new Point2(50, 50), 10));
        d.Add(new ArcEntity(new Point2(0, 0), 20, 0, 90));
        d.Add(new PolylineEntity(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) }, true));
        d.Add(new TextEntity(new Point2(5, 5), "-Q1", 3.5) { HAlign = TextHAlign.Center, VAlign = TextVAlign.Middle });

        var dxf = DxfWriter.Write(d);
        Assert.Contains("AC1009", dxf);
        var r = DxfReader.Read(dxf);
        Assert.Equal(5, r.Entities.Count);
        Assert.Contains(r.Layers, l => l.Name == "WIRES");
        var line = r.Entities.OfType<LineEntity>().Single();
        Assert.Equal("WIRES", line.Layer);
        Assert.Equal(100, line.Length, 6);
        var arc = r.Entities.OfType<ArcEntity>().Single();
        Assert.Equal(90, arc.SweepAngle, 6);
        var text = r.Entities.OfType<TextEntity>().Single();
        Assert.Equal("-Q1", text.Value);
        Assert.Equal(TextHAlign.Center, text.HAlign);
        Assert.True(text.Position.AlmostEquals(new Point2(5, 5), 1e-6));
    }

    [Fact]
    public void Dxf_reader_expands_lwpolyline_bulge_and_reports_unknown()
    {
        var dxf = string.Join("\n", "0", "SECTION", "2", "ENTITIES",
            "0", "LWPOLYLINE", "8", "0", "90", "2", "70", "0", "10", "0", "20", "0", "42", "1", "10", "10", "20", "0",
            "0", "HATCH", "8", "0",
            "0", "ENDSEC", "0", "EOF", "");
        var r = DxfReader.Read(dxf);
        var p = r.Entities.OfType<PolylineEntity>().Single();
        Assert.True(p.Points.Count > 5);
        Assert.All(p.Points, pt => Assert.Equal(5, pt.DistanceTo(new Point2(5, 0)), 6)); // semicircle of radius 5
        Assert.Equal(1, r.SkippedEntityTypes["HATCH"]);
    }
}

public class ReadOnlyEditorTests
{
    [Fact]
    public void Read_only_editor_refuses_every_modification()
    {
        var ed = new CadEditor(new CadDrawing());
        var line = new LineEntity(new Point2(0, 0), new Point2(10, 0));
        ed.Drawing.Add(line);
        ed.IsReadOnly = true;
        string? status = null;
        ed.StatusMessage += (_, m) => status = m;
        ed.AddEntity(new CircleEntity(Point2.Zero, 5));
        ed.Selection.Set(new[] { line.Id });
        ed.MoveSelection(new Point2(5, 5));
        ed.DeleteSelection();
        ed.CopySelection();
        Assert.Empty(ed.Paste());
        ed.SetTool(new LineTool());
        Assert.IsType<SelectTool>(ed.ActiveTool);
        Assert.Single(ed.Drawing.Entities);
        Assert.Equal(new Point2(0, 0), ed.Drawing.Find<LineEntity>(line.Id)!.Start);
        Assert.NotNull(status);
        // Viewing still works.
        ed.ZoomExtents();
        ed.SelectAll();
        Assert.Single(ed.Selection.Ids);
    }
}
