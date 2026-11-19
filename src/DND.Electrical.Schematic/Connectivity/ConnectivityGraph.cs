using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Connectivity;

/// <summary>A connection point in the graph: a symbol pin, a junction or an open wire end.</summary>
public readonly record struct NodeKey(string Value)
{
    public static NodeKey ForPin(Guid instanceId, string pinId) => new($"P:{instanceId:N}:{pinId}");
    public static NodeKey ForJunction(Guid junctionId) => new($"J:{junctionId:N}");
    public static NodeKey ForOpenEnd(Guid wireId, bool start) => new($"O:{wireId:N}:{(start ? 'S' : 'E')}");
    public static NodeKey ForNet(string name) => new($"N:{name.ToUpperInvariant()}");
    public static NodeKey ForInterruption(string name) => new($"I:{name.ToUpperInvariant()}");
    public override string ToString() => Value;
}

/// <summary>Placed pin with its resolved context.</summary>
public sealed record PinNode(Sheet Sheet, SymbolInstanceEntity Instance, SymbolPin Pin, Device? Device)
{
    public NodeKey Key => NodeKey.ForPin(Instance.Id, Pin.Id);
    public string Label => Instance.GetPinLabel(Pin.Id);

    /// <summary>Human readable designation, e.g. "-K1:A1" or "-X1:3" for terminals.</summary>
    public string Designation
    {
        get
        {
            var tag = Device?.Tag ?? "?";
            if (Instance.Definition?.Role == SymbolRole.Terminal)
                return $"{tag}:{Instance.GetValue(SymbolAttributes.TerminalNumber) ?? "?"}";
            return $"{tag}:{Label}";
        }
    }
}

public sealed record WireRecord(Sheet Sheet, WireEntity Wire);

/// <summary>Set of electrically connected nodes (equipotential).</summary>
public sealed class Potential
{
    public int Index { get; init; }
    public List<WireRecord> Wires { get; } = new();
    public List<PinNode> Pins { get; } = new();
    public List<(Sheet sheet, JunctionEntity junction)> Junctions { get; } = new();

    /// <summary>Names given by potential-source symbols (L1, N, PE, +24V…). More than one indicates a conflict.</summary>
    public SortedSet<string> SourceNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SortedSet<string> InterruptionNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Name => SourceNames.Count > 0 ? SourceNames.Min : null;
    public bool HasNameConflict => SourceNames.Count > 1;

    /// <summary>Distinct wire numbers used by wires of this potential (consistency check).</summary>
    public IReadOnlyList<string> WireNumbers => Wires.Select(w => w.Wire.WireNumber).Where(n => n.Length > 0).Distinct().ToList();

    public override string ToString() => Name ?? $"Potential {Index}";
}

/// <summary>
/// Project-wide electrical connectivity built from explicit wire end references, symbol internal
/// connections, named potential sources and interruption points (union–find). Geometry is never used
/// to infer connections.
/// </summary>
public sealed class ConnectivityGraph
{
    private readonly Dictionary<NodeKey, NodeKey> _parent = new();
    private readonly Dictionary<NodeKey, Potential> _potentialByRoot = new();
    private readonly Dictionary<Guid, Potential> _potentialByWire = new();
    private readonly Dictionary<NodeKey, PinNode> _pins = new();
    private readonly Dictionary<NodeKey, List<(WireRecord wire, bool atStart)>> _wiresAtNode = new();
    private readonly Dictionary<Guid, (Sheet sheet, JunctionEntity junction)> _junctions = new();

    public IReadOnlyList<Potential> Potentials { get; private set; } = Array.Empty<Potential>();
    public IReadOnlyList<WireRecord> Wires { get; private set; } = Array.Empty<WireRecord>();
    public IReadOnlyCollection<PinNode> Pins => _pins.Values;

    /// <summary>Wire ends that reference instances/junctions that no longer exist.</summary>
    public List<(WireRecord wire, bool atStart)> BrokenReferences { get; } = new();

    public static ConnectivityGraph Build(ElectricalProject project)
    {
        var g = new ConnectivityGraph();
        g.BuildInternal(project);
        return g;
    }

    private void BuildInternal(ElectricalProject project)
    {
        var wires = new List<WireRecord>();
        var instances = new Dictionary<Guid, (Sheet, SymbolInstanceEntity)>();

        foreach (var sheet in project.OrderedSheets)
            foreach (var e in sheet.Drawing.Entities)
                switch (e)
                {
                    case SymbolInstanceEntity si:
                        instances[si.Id] = (sheet, si);
                        si.Definition ??= project.GetCachedSymbol(si.SymbolId);
                        if (si.Definition is null) break;
                        var dev = si.DeviceId is { } did ? project.FindDevice(did) : null;
                        foreach (var pin in si.Definition.Pins)
                        {
                            var pn = new PinNode(sheet, si, pin, dev);
                            _pins[pn.Key] = pn;
                            Find(pn.Key);
                        }
                        foreach (var grp in si.Definition.InternalConnections)
                            for (int i = 1; i < grp.Count; i++)
                                Union(NodeKey.ForPin(si.Id, grp[0]), NodeKey.ForPin(si.Id, grp[i]));
                        if (si.Definition.Role == SymbolRole.PotentialSource && si.Definition.Pins.Count > 0)
                        {
                            var name = si.GetValue(SymbolAttributes.Potential) ?? si.Definition.DefaultPotential;
                            if (!string.IsNullOrWhiteSpace(name)) Union(NodeKey.ForPin(si.Id, si.Definition.Pins[0].Id), NodeKey.ForNet(name));
                        }
                        if (si.Definition.Role == SymbolRole.Interruption && si.Definition.Pins.Count > 0)
                        {
                            var name = si.GetValue(SymbolAttributes.Name);
                            if (!string.IsNullOrWhiteSpace(name)) Union(NodeKey.ForPin(si.Id, si.Definition.Pins[0].Id), NodeKey.ForInterruption(name));
                        }
                        break;
                    case JunctionEntity j:
                        _junctions[j.Id] = (sheet, j);
                        Find(NodeKey.ForJunction(j.Id));
                        break;
                    case WireEntity w:
                        wires.Add(new WireRecord(sheet, w));
                        break;
                }

        foreach (var wr in wires)
        {
            var a = Resolve(wr, wr.Wire.Start, true, instances);
            var b = Resolve(wr, wr.Wire.End, false, instances);
            Union(a, b);
            AddWireAt(a, wr, true);
            AddWireAt(b, wr, false);
        }
        Wires = wires;

        // Build potentials.
        var list = new List<Potential>();
        foreach (var key in _parent.Keys.ToList())
        {
            var root = Find(key);
            if (!_potentialByRoot.TryGetValue(root, out var pot))
            {
                pot = new Potential { Index = list.Count + 1 };
                _potentialByRoot[root] = pot;
                list.Add(pot);
            }
            if (key.Value.StartsWith("N:")) pot.SourceNames.Add(key.Value[2..]);
            else if (key.Value.StartsWith("I:")) pot.InterruptionNames.Add(key.Value[2..]);
            else if (_pins.TryGetValue(key, out var pin)) pot.Pins.Add(pin);
            else if (key.Value.StartsWith("J:") && _junctions.TryGetValue(Guid.ParseExact(key.Value[2..], "N"), out var j)) pot.Junctions.Add(j);
        }
        foreach (var wr in wires)
        {
            var pot = _potentialByRoot[Find(Resolve(wr, wr.Wire.Start, true, instances))];
            pot.Wires.Add(wr);
            _potentialByWire[wr.Wire.Id] = pot;
        }
        // Use original casing of potential names as written on the symbols.
        foreach (var pot in list)
        {
            if (pot.SourceNames.Count == 0) continue;
            var original = pot.Pins.Where(p => p.Instance.Definition?.Role == SymbolRole.PotentialSource)
                .Select(p => p.Instance.GetValue(SymbolAttributes.Potential) ?? p.Instance.Definition!.DefaultPotential)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (original.Count > 0)
            {
                pot.SourceNames.Clear();
                foreach (var n in original) pot.SourceNames.Add(n);
            }
        }
        Potentials = list.Where(p => p.Wires.Count > 0 || p.Pins.Count > 1 || p.SourceNames.Count > 0).ToList();
    }

    private NodeKey Resolve(WireRecord wr, WireEnd end, bool start, Dictionary<Guid, (Sheet, SymbolInstanceEntity)> instances)
    {
        if (end.InstanceId is { } iid && end.PinId is { } pid)
        {
            var key = NodeKey.ForPin(iid, pid);
            if (instances.ContainsKey(iid) && _pins.ContainsKey(key)) return key;
            BrokenReferences.Add((wr, start));
        }
        else if (end.JunctionId is { } jid)
        {
            if (_junctions.ContainsKey(jid)) return NodeKey.ForJunction(jid);
            BrokenReferences.Add((wr, start));
        }
        var open = NodeKey.ForOpenEnd(wr.Wire.Id, start);
        Find(open);
        return open;
    }

    private void AddWireAt(NodeKey node, WireRecord wr, bool atStart)
    {
        if (!_wiresAtNode.TryGetValue(node, out var l)) _wiresAtNode[node] = l = new();
        l.Add((wr, atStart));
    }

    private NodeKey Find(NodeKey k)
    {
        if (!_parent.TryGetValue(k, out var p)) { _parent[k] = k; return k; }
        if (p.Equals(k)) return k;
        var root = Find(p);
        _parent[k] = root;
        return root;
    }

    private void Union(NodeKey a, NodeKey b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (!ra.Equals(rb)) _parent[rb] = ra;
    }

    // ------------------------------------------------------------------ queries

    public Potential? PotentialOfWire(Guid wireId) => _potentialByWire.GetValueOrDefault(wireId);

    public Potential? PotentialOfPin(Guid instanceId, string pinId)
    {
        var key = NodeKey.ForPin(instanceId, pinId);
        return _parent.ContainsKey(key) ? _potentialByRoot.GetValueOrDefault(Find(key)) : null;
    }

    public bool AreConnected(Guid instanceA, string pinA, Guid instanceB, string pinB)
    {
        var a = NodeKey.ForPin(instanceA, pinA);
        var b = NodeKey.ForPin(instanceB, pinB);
        return _parent.ContainsKey(a) && _parent.ContainsKey(b) && Find(a).Equals(Find(b));
    }

    /// <summary>Number of wires attached directly to a pin.</summary>
    public int WireCountAt(Guid instanceId, string pinId) =>
        _wiresAtNode.TryGetValue(NodeKey.ForPin(instanceId, pinId), out var l) ? l.Count : 0;

    public int WireCountAtJunction(Guid junctionId) =>
        _wiresAtNode.TryGetValue(NodeKey.ForJunction(junctionId), out var l) ? l.Count : 0;

    public PinNode? GetPin(Guid instanceId, string pinId) => _pins.GetValueOrDefault(NodeKey.ForPin(instanceId, pinId));

    /// <summary>
    /// Pins physically wired to <paramref name="pin"/>: follows wires and junctions but not internal
    /// device connections or named potentials. Used for connection lists and terminal diagrams.
    /// </summary>
    public IReadOnlyList<PinNode> WiredNeighbours(PinNode pin) => WiredFrom(pin.Key, excludeWire: null);

    /// <summary>Pins reached from one end of a wire (resolving junctions).</summary>
    public IReadOnlyList<PinNode> PinsAtWireEnd(WireRecord wr, bool start)
    {
        var end = start ? wr.Wire.Start : wr.Wire.End;
        if (end.InstanceId is { } iid && end.PinId is { } pid && _pins.TryGetValue(NodeKey.ForPin(iid, pid), out var p))
            return new[] { p };
        if (end.JunctionId is { } jid) return WiredFrom(NodeKey.ForJunction(jid), wr.Wire.Id);
        return Array.Empty<PinNode>();
    }

    private IReadOnlyList<PinNode> WiredFrom(NodeKey start, Guid? excludeWire)
    {
        var result = new List<PinNode>();
        var visited = new HashSet<NodeKey> { start };
        var usedWires = new HashSet<Guid>();
        if (excludeWire is { } ex) usedWires.Add(ex);
        var queue = new Queue<NodeKey>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (!_wiresAtNode.TryGetValue(n, out var wires)) continue;
            foreach (var (wr, atStart) in wires)
            {
                if (!usedWires.Add(wr.Wire.Id)) continue;
                var otherEnd = atStart ? wr.Wire.End : wr.Wire.Start;
                NodeKey other;
                if (otherEnd.InstanceId is { } iid && otherEnd.PinId is { } pid) other = NodeKey.ForPin(iid, pid);
                else if (otherEnd.JunctionId is { } jid) other = NodeKey.ForJunction(jid);
                else continue;
                if (!visited.Add(other)) continue;
                if (_pins.TryGetValue(other, out var pn)) result.Add(pn);
                else queue.Enqueue(other);
            }
        }
        return result.OrderBy(p => p.Designation, NaturalStringComparer.Instance).ToList();
    }
}
