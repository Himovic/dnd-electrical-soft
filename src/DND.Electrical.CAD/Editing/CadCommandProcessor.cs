using System.Globalization;
using DND.Electrical.CAD.Tools;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Editing;

public sealed record CadCommand(string Name, string[] Aliases, string Description, Func<CadEditor, string[], string?> Execute);

/// <summary>
/// Command-line interpreter: typed commands (LINE, L, CIRCLE…), absolute ("100,50") and relative ("@20,0")
/// coordinates, polar input ("@50&lt;45"), and free text forwarded to the active tool.
/// Modules register additional commands (e.g. WIRE, INSERT).
/// </summary>
public sealed class CadCommandProcessor
{
    private readonly Dictionary<string, CadCommand> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CadCommand> _commands = new();
    private string? _lastCommand;

    public CadCommandProcessor()
    {
        Register(new("LINE", new[] { "L" }, "Draw lines", (e, _) => Tool(e, new LineTool())));
        Register(new("PLINE", new[] { "PL", "POLYLINE" }, "Draw a polyline", (e, _) => Tool(e, new PolylineTool())));
        Register(new("RECTANGLE", new[] { "REC", "RECT" }, "Draw a rectangle", (e, _) => Tool(e, new RectangleTool())));
        Register(new("CIRCLE", new[] { "C", "CI" }, "Draw a circle", (e, _) => Tool(e, new CircleTool())));
        Register(new("ARC", new[] { "A" }, "Draw an arc through three points", (e, _) => Tool(e, new ArcTool())));
        Register(new("TEXT", new[] { "T", "DT" }, "Place text", (e, _) => Tool(e, new TextTool())));
        Register(new("DIMENSION", new[] { "DIM", "DLI" }, "Aligned dimension", (e, _) => Tool(e, new DimensionTool())));
        Register(new("CALLOUT", new[] { "LE", "LEADER" }, "Callout with leader", (e, _) => Tool(e, new LeaderTool())));
        Register(new("MOVE", new[] { "M" }, "Move selection", (e, _) => Tool(e, new MoveTool())));
        Register(new("COPY", new[] { "CO", "CP" }, "Copy selection", (e, _) => Tool(e, new MoveTool(copy: true))));
        Register(new("ROTATE", new[] { "RO" }, "Rotate selection", (e, a) =>
        {
            if (a.Length > 0 && double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var deg))
            {
                e.RotateSelection(deg);
                return $"Rotated by {deg}°";
            }
            return Tool(e, new RotateTool());
        }));
        Register(new("MIRROR", new[] { "MI" }, "Mirror selection", (e, _) => Tool(e, new MirrorTool())));
        Register(new("ERASE", new[] { "E", "DELETE", "DEL" }, "Delete selection", (e, _) =>
        {
            var n = e.Selection.Count;
            e.DeleteSelection();
            return $"{n} object(s) deleted";
        }));
        Register(new("UNDO", new[] { "U" }, "Undo", (e, _) => e.UndoLast() ? "Undone" : "Nothing to undo"));
        Register(new("REDO", Array.Empty<string>(), "Redo", (e, _) => e.RedoLast() ? "Redone" : "Nothing to redo"));
        Register(new("ZOOM", new[] { "Z" }, "ZOOM E = extents", (e, a) =>
        {
            e.ZoomExtents();
            return "Zoom extents";
        }));
        Register(new("ZE", Array.Empty<string>(), "Zoom extents", (e, _) => { e.ZoomExtents(); return "Zoom extents"; }));
        Register(new("PAN", new[] { "P" }, "Pan the view", (e, _) => Tool(e, new PanTool())));
        Register(new("SELECTALL", new[] { "ALL" }, "Select everything", (e, _) => { e.SelectAll(); return $"{e.Selection.Count} selected"; }));
        Register(new("GRID", Array.Empty<string>(), "Toggle grid display or set spacing: GRID 5", (e, a) =>
        {
            if (a.Length > 0 && double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0)
            {
                e.Snap.GridSpacing = s;
                e.RequestRedraw();
                return $"Grid spacing {s} mm";
            }
            e.Snap.ShowGrid = !e.Snap.ShowGrid;
            e.RequestRedraw();
            return e.Snap.ShowGrid ? "Grid on" : "Grid off";
        }));
        Register(new("SNAP", Array.Empty<string>(), "Toggle grid snap", (e, _) => { e.Snap.GridSnap = !e.Snap.GridSnap; return e.Snap.GridSnap ? "Snap on" : "Snap off"; }));
        Register(new("OSNAP", Array.Empty<string>(), "Toggle object snap", (e, _) => { e.Snap.ObjectSnap = !e.Snap.ObjectSnap; return e.Snap.ObjectSnap ? "Object snap on" : "Object snap off"; }));
        Register(new("ORTHO", Array.Empty<string>(), "Toggle orthogonal mode", (e, _) => { e.Snap.Ortho = !e.Snap.Ortho; return e.Snap.Ortho ? "Ortho on" : "Ortho off"; }));
        Register(new("GROUP", Array.Empty<string>(), "Group selection", (e, _) => e.GroupSelection() is null ? "Select at least two objects" : "Grouped"));
        Register(new("UNGROUP", Array.Empty<string>(), "Ungroup selection", (e, _) => { e.UngroupSelection(); return "Ungrouped"; }));
    }

    public IReadOnlyList<CadCommand> Commands => _commands;

    public void Register(CadCommand command)
    {
        _commands.RemoveAll(c => string.Equals(c.Name, command.Name, StringComparison.OrdinalIgnoreCase));
        _commands.Add(command);
        _lookup[command.Name] = command;
        foreach (var a in command.Aliases) _lookup[a] = command;
    }

    private static string? Tool(CadEditor editor, CadTool tool)
    {
        editor.SetTool(tool);
        return tool.Prompt;
    }

    /// <summary>Executes one line of input and returns a message for the command history.</summary>
    public string? Execute(CadEditor editor, string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            if (editor.ActiveTool.IsBusy) { editor.ActiveTool.Finish(); editor.RequestRedraw(); return null; }
            return _lastCommand is null ? null : Execute(editor, _lastCommand);
        }

        if (TryParseCoordinate(text, editor, out var point))
        {
            editor.SubmitPoint(point);
            return $"Point {point}";
        }

        if (editor.ActiveTool.IsBusy && editor.SubmitText(text)) return null;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_lookup.TryGetValue(parts[0], out var cmd))
        {
            _lastCommand = text;
            return cmd.Execute(editor, parts.Skip(1).ToArray());
        }

        if (editor.SubmitText(text)) return null;
        return $"Unknown command '{parts[0]}'. Type HELP for the list of commands.";
    }

    /// <summary>Parses "x,y", "@dx,dy" and "@dist&lt;angle" (invariant culture).</summary>
    public static bool TryParseCoordinate(string text, CadEditor editor, out Point2 point)
    {
        point = default;
        var relative = text.StartsWith('@');
        var body = relative ? text[1..] : text;
        var basePoint = editor.ActiveTool.OrthoReference ?? editor.CursorWorld;

        var lt = body.IndexOf('<');
        if (lt > 0 && relative)
        {
            if (double.TryParse(body[..lt], NumberStyles.Float, CultureInfo.InvariantCulture, out var dist) &&
                double.TryParse(body[(lt + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var ang))
            {
                point = basePoint + Point2.FromPolar(dist, ang);
                return true;
            }
            return false;
        }

        if (!Point2.TryParse(body, out var p)) return false;
        point = relative ? basePoint + p : p;
        return true;
    }
}
