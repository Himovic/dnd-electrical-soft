using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.CAD.Tools;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Editing;

/// <summary>Places symbols: ghost follows the cursor, R rotates, X mirrors, click inserts (repeatable).</summary>
public sealed class InsertSymbolTool : CadTool
{
    private readonly SymbolDefinition _definition;
    private readonly string? _deviceTag;
    private readonly IDictionary<string, string>? _values;
    private double _rotation;
    private bool _mirrored;
    private Point2 _cursor;

    public InsertSymbolTool(SymbolDefinition definition, string? deviceTag = null, IDictionary<string, string>? values = null)
    {
        _definition = definition;
        _deviceTag = deviceTag;
        _values = values;
    }

    public SymbolInstanceEntity? LastInserted { get; private set; }

    public override string Id => "insert-symbol";
    public override string DisplayName => $"Insert {_definition.Name}";
    public override string Prompt => $"Insert {_definition.Name}: click to place · R rotate · X mirror · Esc to finish";

    private SchematicEditor Ed => (SchematicEditor)Editor;

    public override bool AllowSnapPoint(SnapPoint point) => point.Kind == SnapKind.Pin;

    public override void OnPointerMove(ToolInput input)
    {
        _cursor = input.World;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewEntities.Clear();
        var ghost = SchematicService.CreateInstance(_definition, _cursor, _rotation, _mirrored);
        ghost.Display[SymbolAttributes.Tag] = _deviceTag ?? $"-{_definition.DefaultTagPrefix}?";
        PreviewEntities.Add(ghost);
    }

    public override void OnPointerDown(ToolInput input)
    {
        if (input.Button != PointerButton.Left) return;
        _cursor = input.World;
        // A single-device tag can only be reused by symbols that legitimately share a device (contacts, coils).
        var tag = _deviceTag;
        LastInserted = Ed.InsertSymbol(_definition, _cursor, _rotation, _mirrored, tag, _values);
        Editor.Selection.Set(new[] { LastInserted.Id });
        UpdatePreview();
    }

    public override bool OnKey(ToolKey key, KeyModifiers mods)
    {
        switch (key)
        {
            case ToolKey.R:
            case ToolKey.Space:
                _rotation = GeometryMath.NormalizeAngle(_rotation + 90);
                UpdatePreview();
                return true;
            case ToolKey.X:
            case ToolKey.M:
                _mirrored = !_mirrored;
                UpdatePreview();
                return true;
        }
        return false;
    }
}

/// <summary>
/// Draws wires. Clicking a pin, junction or wire end starts/ends a connection; clicking on the middle
/// of a wire creates a junction (T-connection); clicks in free space add bend points.
/// Routes are orthogonal. Enter / right-click finishes an open wire.
/// </summary>
public sealed class WireTool : CadTool
{
    private readonly List<Point2> _points = new();
    private WireEnd? _start;
    private Point2 _cursor;
    private Point2? _startDir;

    public override string Id => "wire";
    public override string DisplayName => "Wire";
    public override string Prompt => _points.Count == 0
        ? "Wire: click a pin, junction or wire to start"
        : "Wire: click a pin/wire to connect, or free space for a bend · Enter to end open · Esc to cancel";

    public override bool IsBusy => _points.Count > 0;
    public override Point2? OrthoReference => _points.Count > 0 ? _points[^1] : null;

    private SchematicEditor Ed => (SchematicEditor)Editor;

    public WireEntity? LastWire { get; private set; }

    public override bool AllowSnapTo(CadEntity entity) => entity is SymbolInstanceEntity or JunctionEntity or WireEntity;

    public override bool AllowSnapPoint(SnapPoint point) => point.Kind is SnapKind.Pin or SnapKind.Endpoint;

    public override void OnPointerMove(ToolInput input)
    {
        _cursor = input.World;
        UpdatePreview(input.Snap);
    }

    private void UpdatePreview(SnapResult snap)
    {
        PreviewEntities.Clear();
        if (_points.Count == 0) return;
        var route = _points.Count == 1
            ? SchematicService.Route(_points[0], _startDir, _cursor, EndDir(snap))
            : _points.Concat(SchematicService.Route(_points[^1], null, _cursor, EndDir(snap)).Skip(1)).ToList();
        PreviewEntities.Add(new WireEntity { Points = SchematicService.Simplify(route) });
    }

    private Point2? EndDir(SnapResult snap)
    {
        if (snap.Tag is PinRef pr && Editor.Drawing.Find(pr.InstanceId) is SymbolInstanceEntity si && si.Definition?.FindPin(pr.PinId) is { } pin)
            return SchematicService.WorldDir(si, pin.Direction);
        return null;
    }

    public override void OnPointerDown(ToolInput input)
    {
        if (input.Button != PointerButton.Left) return;
        var p = input.World;
        var (end, dir) = ResolveConnection(input.Snap, p);

        if (_points.Count == 0)
        {
            _points.Add(p);
            _start = end;
            _startDir = dir;
            return;
        }

        if (input.ClickCount >= 2 && end is null) { Finish(); return; }

        if (end is not null)
        {
            var route = _points.Count == 1
                ? SchematicService.Route(_points[0], _startDir, p, dir)
                : _points.Concat(SchematicService.Route(_points[^1], null, p, dir).Skip(1)).ToList();
            Commit(route, end);
            return;
        }

        // Free space: add a bend point (orthogonal from the previous point).
        if (!_points[^1].AlmostEquals(p, 1e-6)) _points.Add(p);
    }

    /// <summary>Determines the electrical connection at a clicked point (pin, junction, wire end, wire middle).</summary>
    private (WireEnd? end, Point2? dir) ResolveConnection(SnapResult snap, Point2 p)
    {
        switch (snap.Tag)
        {
            case PinRef pr:
                return (WireEnd.Pin(pr.InstanceId, pr.PinId), EndDir(snap));
            case JunctionRef jr:
                return (WireEnd.Junction(jr.JunctionId), null);
            case WireEntity w when snap.Kind == SnapKind.Endpoint:
                {
                    // Connecting to the end of an existing wire: join at its connection point.
                    var atStart = w.StartPoint.AlmostEquals(snap.Point, 1e-6);
                    var existing = atStart ? w.Start : w.End;
                    if (existing.IsConnected) return (existing.Clone(), null);
                    var j = Ed.SplitWireWithJunctionAtEnd(w, atStart);
                    return (WireEnd.Junction(j.Id), null);
                }
        }
        var tol = Editor.Viewport.ToWorldDistance(4);
        var wire = Ed.FindWireAt(p, tol);
        if (wire is not null && wire.FindSegment(p, tol) >= 0)
        {
            var seg = wire.FindSegment(p, tol);
            var onWire = GeometryMath.ClosestPointOnSegment(p, wire.Points[seg], wire.Points[seg + 1]);
            var j = Ed.SplitWireWithJunction(wire, onWire);
            return (WireEnd.Junction(j.Id), null);
        }
        return (null, null);
    }

    private void Commit(List<Point2> route, WireEnd end)
    {
        var simplified = SchematicService.Simplify(route);
        if (simplified.Count >= 2 && !(simplified.Count == 2 && simplified[0].AlmostEquals(simplified[1], 1e-6)))
        {
            LastWire = new WireEntity { Points = simplified, Start = _start ?? WireEnd.Open(), End = end };
            Editor.AddEntity(LastWire, "Draw wire");
        }
        Reset();
    }

    public override bool OnKey(ToolKey key, KeyModifiers mods)
    {
        if (key == ToolKey.Enter && IsBusy) { Finish(); return true; }
        if (key == ToolKey.Escape && IsBusy) { Cancel(); return true; }
        if (key == ToolKey.Backspace && _points.Count > 1) { _points.RemoveAt(_points.Count - 1); return true; }
        return false;
    }

    public override bool Finish()
    {
        if (_points.Count == 0) return false;
        if (_points.Count >= 1 && !_points[^1].AlmostEquals(_cursor, 1e-6) && _points.Count == 1)
            _points.Add(GeometryMath.Orthogonalize(_points[0], _cursor));
        if (_points.Count >= 2) Commit(_points.ToList(), WireEnd.Open());
        else Reset();
        return true;
    }

    public override void Cancel()
    {
        base.Cancel();
        Reset();
    }

    private void Reset()
    {
        _points.Clear();
        _start = null;
        _startDir = null;
        PreviewEntities.Clear();
    }
}
