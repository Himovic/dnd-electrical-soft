using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Services;

/// <summary>
/// Computes everything displayed on schematics that derives from project data: symbol definitions,
/// device tags and attributes, PLC addresses, cross-references (coil ↔ contacts, interruption
/// points) and cable core labels. Call after loading and after any engineering change.
/// </summary>
public sealed class SchematicBinder(SymbolResolver resolver)
{
    public SymbolResolver Resolver { get; } = resolver;

    public void BindProject(ElectricalProject project)
    {
        var s = project.Settings;
        var instances = project.SymbolInstances().ToList();
        foreach (var (_, inst) in instances) inst.Definition = Resolver.Resolve(project, inst.SymbolId);
        var byDevice = instances.Where(x => x.instance.DeviceId is not null)
            .GroupBy(x => x.instance.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var interruptions = instances.Where(x => x.instance.Definition?.Role == SymbolRole.Interruption)
            .GroupBy(x => x.instance.GetValue(SymbolAttributes.Name) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var channels = project.PlcStations.SelectMany(st => st.Channels.Select(c => (st, c))).ToDictionary(x => x.c.Id);

        foreach (var (sheet, inst) in instances)
        {
            inst.Display.Clear();
            inst.ContactMirror.Clear();
            var def = inst.Definition;
            if (def is null) continue;
            var device = inst.DeviceId is { } did ? project.FindDevice(did) : null;

            if (device is not null)
            {
                inst.Display[SymbolAttributes.Tag] = device.Tag;
                var function = inst.GetValue(SymbolAttributes.Function) ?? device.Function;
                // Show the function text only on the main representation to avoid clutter.
                if (def.Role != SymbolRole.ChildContact && !string.IsNullOrEmpty(function)) inst.Display[SymbolAttributes.Function] = function;
                var rating = device.GetProperty(DeviceProperties.Rating);
                if (!string.IsNullOrEmpty(rating)) inst.Display[SymbolAttributes.Rating] = rating;
            }

            switch (def.Role)
            {
                case SymbolRole.ChildContact when device is not null:
                    {
                        var main = byDevice[device.Id].FirstOrDefault(x => x.instance.Definition?.Role is SymbolRole.Main);
                        if (main.instance is not null)
                            inst.Display[SymbolAttributes.Tag] = $"{device.Tag}\n{SheetLocation.Of(main.sheet, main.instance.Position, s)}";
                        break;
                    }
                case SymbolRole.Main when device is not null:
                    foreach (var (csheet, contact) in byDevice[device.Id]
                                 .Where(x => x.instance.Definition?.Role == SymbolRole.ChildContact)
                                 .OrderBy(x => x.sheet.Number).ThenBy(x => x.instance.Position.X))
                    {
                        var pins = contact.Definition!.Pins.Select(p => contact.GetPinLabel(p.Id)).ToList();
                        var pinText = contact.Definition.ContactType == ContactType.Power
                            ? string.Join("-", pins.Take(2)) + (pins.Count > 2 ? "…" : string.Empty)
                            : string.Join("-", pins);
                        var symbol = contact.Definition.ContactType switch
                        {
                            ContactType.NormallyOpen => "NO",
                            ContactType.NormallyClosed => "NC",
                            ContactType.ChangeOver => "CO",
                            ContactType.Power => "PWR",
                            _ => string.Empty,
                        };
                        inst.ContactMirror.Add($"{symbol} {pinText} {SheetLocation.Of(csheet, contact.Position, s)}");
                    }
                    break;
                case SymbolRole.Terminal:
                    {
                        var no = inst.GetValue(SymbolAttributes.TerminalNumber);
                        if (!string.IsNullOrEmpty(no)) inst.Display[SymbolAttributes.TerminalNumber] = no;
                        inst.Display.Remove(SymbolAttributes.Function);
                        break;
                    }
                case SymbolRole.PotentialSource:
                    inst.Display[SymbolAttributes.Potential] = inst.GetValue(SymbolAttributes.Potential) ?? def.DefaultPotential ?? string.Empty;
                    break;
                case SymbolRole.Interruption:
                    {
                        var name = inst.GetValue(SymbolAttributes.Name) ?? string.Empty;
                        inst.Display[SymbolAttributes.Name] = name;
                        if (interruptions.TryGetValue(name, out var peers))
                        {
                            var refs = peers.Where(p => p.instance.Id != inst.Id)
                                .Select(p => SheetLocation.Of(p.sheet, p.instance.Position, s).ToString()).ToList();
                            if (refs.Count > 0) inst.Display[SymbolAttributes.CrossReference] = string.Join(" ", refs);
                        }
                        break;
                    }
                case SymbolRole.PlcIo when inst.PlcChannelId is { } chId && channels.TryGetValue(chId, out var ch):
                    inst.Display[SymbolAttributes.Address] = ch.c.Address;
                    if (!string.IsNullOrEmpty(ch.c.Tag)) inst.Display[SymbolAttributes.PlcTag] = ch.c.Tag;
                    inst.Display[SymbolAttributes.Tag] = ch.st.Tag;
                    break;
            }
        }

        // Cable core labels on wires
        var cables = project.Cables.ToDictionary(c => c.Id);
        foreach (var (_, wire) in project.WireEntities())
        {
            wire.CableLabel = null;
            if (wire.CableId is { } cid && cables.TryGetValue(cid, out var cable))
            {
                var core = cable.Cores.FirstOrDefault(c => c.WireId == wire.Id);
                wire.CableLabel = core is null ? cable.Tag : $"{cable.Tag}:{core.Marking}";
            }
        }
    }

    /// <summary>Binds and also computes potential names onto wires.</summary>
    public ConnectivityGraph BindWithConnectivity(ElectricalProject project)
    {
        BindProject(project);
        var g = ConnectivityGraph.Build(project);
        foreach (var p in g.Potentials)
            foreach (var w in p.Wires) w.Wire.PotentialName = p.Name;
        return g;
    }
}
