using DND.Electrical.CAD.Editing;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Editing;

/// <summary>
/// Editing session for one schematic sheet. Adds electrical behaviour to the generic CAD editor:
/// symbol insertion with device creation, wiring with connection references, rubber-banding,
/// automatic junctions, device duplication on paste and re-binding of cross-references.
/// </summary>
public sealed class SchematicEditor : CadEditor
{
    public SchematicEditor(ElectricalProject project, Sheet sheet, SchematicService schematic, UndoManager? undo = null)
        : base(sheet.Drawing, undo)
    {
        Project = project;
        Sheet = sheet;
        Schematic = schematic;
        ActiveLayer = StandardLayers.Annotations;
        Extensions.Add(new Extension(this));
        Undo.Changed += (_, _) => Rebind();
        Rebind();
    }

    public ElectricalProject Project { get; }
    public Sheet Sheet { get; }
    public SchematicService Schematic { get; }

    /// <summary>Raised when project-level data (devices, connectivity) changed through this editor.</summary>
    public event EventHandler? ProjectChanged;

    public void Rebind()
    {
        Schematic.Binder.BindProject(Project);
        Sheet.Modified = DateTimeOffset.Now;
        RequestRedraw();
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public ConnectivityGraph BuildConnectivity() => Schematic.Binder.BindWithConnectivity(Project);

    // ------------------------------------------------------------------ high level operations

    /// <summary>Places a symbol as one undoable step (creating its device when needed).</summary>
    public SymbolInstanceEntity InsertSymbol(SymbolDefinition def, Point2 position, double rotation = 0, bool mirrored = false, string? deviceTag = null, IDictionary<string, string>? values = null)
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyReason);
        Project.CacheSymbol(def, Schematic.Resolver.Libraries.FindLibraryOf(def.Id)?.Id ?? string.Empty);
        var inst = SchematicService.CreateInstance(def, position, rotation, mirrored);
        if (values is not null) foreach (var kv in values) inst.Values[kv.Key] = kv.Value;
        if (def.Role == SymbolRole.Terminal && !inst.Values.ContainsKey(SymbolAttributes.TerminalNumber))
            inst.Values[SymbolAttributes.TerminalNumber] = NextTerminalNumber(deviceTag);

        using (Undo.BeginGroup($"Insert {def.Name}"))
        {
            if (SchematicService.NeedsDevice(def))
            {
                var existing = deviceTag is null ? null : Project.FindDeviceByTag(deviceTag);
                if (existing is null)
                {
                    var device = new Device
                    {
                        Prefix = def.DefaultTagPrefix,
                        Category = def.Category.ToString(),
                        Tag = deviceTag ?? DeviceTaggingService.NextTag(Project, def.DefaultTagPrefix),
                        TagLocked = deviceTag is not null,
                    };
                    Undo.Execute(new DelegateAction("Create device", () => Project.Devices.Add(device), () => Project.Devices.Remove(device)));
                    inst.DeviceId = device.Id;
                }
                else inst.DeviceId = existing.Id;
            }
            Undo.Execute(new AddEntitiesAction(Drawing, new[] { inst }, $"Insert {def.Name}"));
        }
        return inst;
    }

    private string NextTerminalNumber(string? stripTag)
    {
        var numbers = Project.SymbolInstances()
            .Where(x => x.instance.Definition?.Role == SymbolRole.Terminal)
            .Where(x => stripTag is null || (x.instance.DeviceId is { } d && Project.FindDevice(d)?.Tag == stripTag))
            .Select(x => int.TryParse(x.instance.GetValue(SymbolAttributes.TerminalNumber), out var n) ? n : 0);
        return ((numbers.DefaultIfEmpty(0).Max()) + 1).ToString();
    }

    /// <summary>Links an instance to another (existing or new) device tag, e.g. to make a contact belong to a coil.</summary>
    public void AssignDevice(SymbolInstanceEntity instance, string tag)
    {
        if (RefuseIfReadOnly()) return;
        var device = Project.FindDeviceByTag(tag);
        using (Undo.BeginGroup($"Assign {tag}"))
        {
            if (device is null)
            {
                var def = instance.Definition;
                device = new Device { Tag = tag, Prefix = def?.DefaultTagPrefix ?? string.Empty, Category = def?.Category.ToString() ?? string.Empty, TagLocked = true };
                var d = device;
                Undo.Execute(new DelegateAction("Create device", () => Project.Devices.Add(d), () => Project.Devices.Remove(d)));
            }
            var old = instance.DeviceId;
            var target = device.Id;
            ModifyEntities(new[] { instance }, $"Assign {tag}", e => ((SymbolInstanceEntity)e).DeviceId = target);
            if (old is { } o) RemoveOrphanDevice(o);
        }
    }

    /// <summary>Renames a device (all its symbols follow) as an undoable step.</summary>
    public bool RenameDevice(Device device, string newTag)
    {
        if (RefuseIfReadOnly()) return false;
        newTag = newTag.Trim();
        if (newTag.Length == 0 || string.Equals(device.Tag, newTag, StringComparison.Ordinal)) return false;
        if (Project.FindDeviceByTag(newTag) is { } other && other != device) return false;
        var old = device.Tag;
        var oldLocked = device.TagLocked;
        Undo.Execute(new DelegateAction($"Rename {old} → {newTag}",
            () => { device.Tag = newTag; device.TagLocked = true; },
            () => { device.Tag = old; device.TagLocked = oldLocked; }));
        return true;
    }

    public WireNumberingResult NumberWires(bool renumberExisting)
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyReason);
        var graph = BuildConnectivity();
        var wires = Project.WireEntities().Select(x => x.wire).ToList();
        var before = wires.ToDictionary(w => w.Id, w => w.WireNumber);
        var result = WireNumberingService.Apply(Project, graph, new WireNumberingOptions { RenumberExisting = renumberExisting });
        var after = wires.ToDictionary(w => w.Id, w => w.WireNumber);
        Undo.Push(new DelegateAction("Number wires",
            () => { foreach (var w in wires) w.WireNumber = after[w.Id]; },
            () => { foreach (var w in wires) w.WireNumber = before[w.Id]; }));
        Rebind();
        return result;
    }

    public IReadOnlyList<(string oldTag, string newTag)> RenumberDevices()
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyReason);
        var before = Project.Devices.ToDictionary(d => d, d => d.Tag);
        var changes = DeviceTaggingService.RenumberAll(Project);
        var after = Project.Devices.ToDictionary(d => d, d => d.Tag);
        Undo.Push(new DelegateAction("Renumber devices",
            () => { foreach (var kv in after) kv.Key.Tag = kv.Value; },
            () => { foreach (var kv in before) kv.Key.Tag = kv.Value; }));
        Rebind();
        return changes;
    }

    internal void RemoveOrphanDevice(Guid deviceId)
    {
        var stillUsed = Project.SymbolInstances().Any(x => x.instance.DeviceId == deviceId);
        if (stillUsed) return;
        var device = Project.FindDevice(deviceId);
        if (device is null) return;
        if (Project.Motors.Any(m => m.DeviceId == deviceId) || Project.Cabinets.Any(c => c.Items.Any(i => i.DeviceId == deviceId))) return;
        var index = Project.Devices.IndexOf(device);
        Undo.Execute(new DelegateAction($"Remove device {device.Tag}",
            () => Project.Devices.Remove(device),
            () => Project.Devices.Insert(Math.Min(index, Project.Devices.Count), device)));
    }

    /// <summary>Finds a wire whose segment passes through <paramref name="p"/> (for T-connections).</summary>
    public WireEntity? FindWireAt(Point2 p, double tolerance, Guid? exclude = null) =>
        Drawing.Query(Rect2.FromCenter(p, tolerance, tolerance)).OfType<WireEntity>()
            .Where(w => w.Id != exclude && Drawing.IsLayerVisible(w.Layer))
            .FirstOrDefault(w => w.DistanceTo(p) <= tolerance);

    /// <summary>Splits a wire and inserts a junction as an undoable step. Returns the junction.</summary>
    public JunctionEntity SplitWireWithJunction(WireEntity wire, Point2 at)
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyReason);
        var (first, second, junction) = SchematicService.PrepareSplit(wire, at);
        var before = wire.Clone(keepId: true);
        Undo.Execute(new DelegateAction("Add junction",
            () =>
            {
                Drawing.Replace(first.Clone(keepId: true));
                Drawing.Add(junction);
                Drawing.Add(second);
            },
            () =>
            {
                Drawing.Remove(second);
                Drawing.Remove(junction);
                Drawing.Replace(before.Clone(keepId: true));
            }));
        return junction;
    }

    /// <summary>
    /// Attaches a junction to the free end of a wire (so further wires can branch from it).
    /// </summary>
    public JunctionEntity SplitWireWithJunctionAtEnd(WireEntity wire, bool atStart)
    {
        var junction = new JunctionEntity(atStart ? wire.StartPoint : wire.EndPoint);
        using (Undo.BeginGroup("Add junction"))
        {
            Undo.Execute(new AddEntitiesAction(Drawing, new[] { junction }, "Add junction"));
            ModifyEntities(new[] { wire }, "Connect wire end", e =>
            {
                var w = (WireEntity)e;
                if (atStart) w.Start = WireEnd.Junction(junction.Id); else w.End = WireEnd.Junction(junction.Id);
            });
        }
        return junction;
    }

    // ------------------------------------------------------------------ extension hooks

    private sealed class Extension(SchematicEditor ed) : ICadEditorExtension
    {
        public void OnDeleting(CadEditor editor, IReadOnlyList<CadEntity> entities, IList<IUndoableAction> additional)
        {
            var deleted = entities.Select(e => e.Id).ToHashSet();
            var wires = ed.Drawing.Entities.OfType<WireEntity>().Where(w => !deleted.Contains(w.Id))
                .Where(w => (w.Start.InstanceId ?? w.Start.JunctionId) is { } s && deleted.Contains(s)
                         || (w.End.InstanceId ?? w.End.JunctionId) is { } e && deleted.Contains(e))
                .ToList();
            if (wires.Count > 0)
                additional.Add(new LazyAction("Detach wires", () => ModifyEntitiesAction.Apply(ed.Drawing, wires, "Detach wires", list =>
                {
                    foreach (var w in list.Cast<WireEntity>())
                    {
                        if ((w.Start.InstanceId ?? w.Start.JunctionId) is { } s && deleted.Contains(s)) w.Start = WireEnd.Open();
                        if ((w.End.InstanceId ?? w.End.JunctionId) is { } e && deleted.Contains(e)) w.End = WireEnd.Open();
                    }
                })));
            var devices = entities.OfType<SymbolInstanceEntity>().Select(i => i.DeviceId).OfType<Guid>().Distinct().ToList();
            if (devices.Count > 0)
                additional.Add(new DeferredDeviceCleanup(ed, devices, deleted));
        }

        public void OnTransformed(CadEditor editor, IReadOnlyList<CadEntity> entities, Matrix2 transform)
        {
            var moved = entities.Select(e => e.Id).ToHashSet();
            var movedInstances = entities.OfType<SymbolInstanceEntity>().ToDictionary(i => i.Id);
            var movedJunctions = entities.OfType<JunctionEntity>().ToDictionary(j => j.Id);
            if (movedInstances.Count == 0 && movedJunctions.Count == 0) return;
            var affected = ed.Drawing.Entities.OfType<WireEntity>()
                .Where(w => !moved.Contains(w.Id))
                .Where(w => Attached(w.Start) || Attached(w.End))
                .ToList();
            if (affected.Count == 0) return;
            ed.Undo.Push(ModifyEntitiesAction.Apply(ed.Drawing, affected, "Follow connections", list =>
            {
                foreach (var w in list.Cast<WireEntity>())
                {
                    var s = Target(w.Start);
                    var e = Target(w.End);
                    if (s is not null && e is not null && Attached(w.Start) && Attached(w.End))
                    {
                        w.Transform(transform);
                        continue;
                    }
                    if (s is { } sp && Attached(w.Start)) MoveEnd(w, start: true, sp);
                    if (e is { } ep && Attached(w.End)) MoveEnd(w, start: false, ep);
                }
            }));

            bool Attached(WireEnd end) => (end.InstanceId is { } i && movedInstances.ContainsKey(i)) || (end.JunctionId is { } j && movedJunctions.ContainsKey(j));

            Point2? Target(WireEnd end)
            {
                if (end.InstanceId is { } i && movedInstances.TryGetValue(i, out var inst) && end.PinId is { } pin) return inst.GetPinPosition(pin);
                if (end.JunctionId is { } j && movedJunctions.TryGetValue(j, out var jn)) return jn.Position;
                return null;
            }
        }

        /// <summary>Moves one end of a wire, keeping the adjacent segment orthogonal.</summary>
        private static void MoveEnd(WireEntity w, bool start, Point2 target)
        {
            if (w.Points.Count < 2) return;
            var idx = start ? 0 : w.Points.Count - 1;
            var adj = start ? 1 : w.Points.Count - 2;
            var old = w.Points[idx];
            var neighbour = w.Points[adj];
            w.Points[idx] = target;
            if (w.Points.Count == 2)
            {
                if (Math.Abs(target.X - neighbour.X) > 1e-9 && Math.Abs(target.Y - neighbour.Y) > 1e-9)
                {
                    var route = SchematicService.Route(start ? target : neighbour, null, start ? neighbour : target, null);
                    w.Points = route;
                }
                return;
            }
            var wasVertical = Math.Abs(old.X - neighbour.X) < 1e-9;
            w.Points[adj] = wasVertical ? new Point2(target.X, neighbour.Y) : new Point2(neighbour.X, target.Y);
            w.Points = SchematicService.Simplify(w.Points);
        }

        public void OnPasted(CadEditor editor, IReadOnlyList<CadEntity> entities, IReadOnlyDictionary<Guid, Guid> idMap)
        {
            // Pasted symbols get new devices (one per original device); wires keep only internal references.
            var newDevices = new Dictionary<Guid, Device>();
            foreach (var inst in entities.OfType<SymbolInstanceEntity>())
            {
                inst.Definition ??= ed.Project.GetCachedSymbol(inst.SymbolId);
                if (inst.DeviceId is not { } old) continue;
                if (!newDevices.TryGetValue(old, out var dev))
                {
                    var src = ed.Project.FindDevice(old);
                    var prefix = src?.Prefix ?? inst.Definition?.DefaultTagPrefix ?? "A";
                    dev = new Device
                    {
                        Prefix = prefix,
                        Category = src?.Category ?? string.Empty,
                        Function = src?.Function ?? string.Empty,
                        Manufacturer = src?.Manufacturer ?? string.Empty,
                        PartNumber = src?.PartNumber ?? string.Empty,
                        Properties = new Dictionary<string, string>(src?.Properties ?? new()),
                        Tag = DeviceTaggingService.NextTag(ed.Project, prefix),
                    };
                    var d = dev;
                    ed.Undo.Execute(new DelegateAction("Create device", () => ed.Project.Devices.Add(d), () => ed.Project.Devices.Remove(d)));
                    newDevices[old] = dev;
                }
                inst.DeviceId = dev.Id;
                inst.PlcChannelId = null;
            }
            foreach (var w in entities.OfType<WireEntity>())
            {
                w.Start = Remap(w.Start);
                w.End = Remap(w.End);
                w.CableId = null;
                w.CableCoreIndex = null;
                if (!w.NumberLocked) w.WireNumber = string.Empty;
            }
            ed.Drawing.NotifyModified(entities);

            WireEnd Remap(WireEnd end)
            {
                if (end.InstanceId is { } i) return idMap.TryGetValue(i, out var ni) ? WireEnd.Pin(ni, end.PinId!) : WireEnd.Open();
                if (end.JunctionId is { } j) return idMap.TryGetValue(j, out var nj) ? WireEnd.Junction(nj) : WireEnd.Open();
                return WireEnd.Open();
            }
        }
    }

    /// <summary>Action whose concrete change is computed when first executed.</summary>
    private sealed class LazyAction(string description, Func<IUndoableAction> factory) : IUndoableAction
    {
        private IUndoableAction? _inner;
        public string Description { get; } = description;
        public void Redo()
        {
            if (_inner is null) _inner = factory(); // applies the change
            else _inner.Redo();
        }
        public void Undo() => _inner?.Undo();
    }

    /// <summary>Removes devices left without any symbol after a deletion.</summary>
    private sealed class DeferredDeviceCleanup(SchematicEditor ed, List<Guid> deviceIds, HashSet<Guid> deletedEntities) : IUndoableAction
    {
        private readonly List<(int index, Device device)> _removed = new();
        public string Description => "Remove unused devices";

        public void Redo()
        {
            _removed.Clear();
            foreach (var id in deviceIds)
            {
                var used = ed.Project.SymbolInstances().Any(x => x.instance.DeviceId == id && !deletedEntities.Contains(x.instance.Id));
                if (used) continue;
                if (ed.Project.Motors.Any(m => m.DeviceId == id) || ed.Project.Cabinets.Any(c => c.Items.Any(i => i.DeviceId == id))) continue;
                var dev = ed.Project.FindDevice(id);
                if (dev is null) continue;
                _removed.Add((ed.Project.Devices.IndexOf(dev), dev));
                ed.Project.Devices.Remove(dev);
            }
        }

        public void Undo()
        {
            for (int i = _removed.Count - 1; i >= 0; i--)
                ed.Project.Devices.Insert(Math.Min(_removed[i].index, ed.Project.Devices.Count), _removed[i].device);
        }
    }
}
