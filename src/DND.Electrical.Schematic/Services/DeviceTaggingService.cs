using System.Text.RegularExpressions;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Services;

/// <summary>Automatic device identification (tags) using the project's tag format.</summary>
public static partial class DeviceTaggingService
{
    [GeneratedRegex(@"(\d+)(?!.*\d)")]
    private static partial Regex LastNumber();

    /// <summary>Formats a tag from the project format, e.g. "-{PREFIX}{N}" → "-K3".</summary>
    public static string Format(ProjectSettings settings, string prefix, int n, int sheet = 0, int column = 0) =>
        settings.DeviceTagFormat
            .Replace("{PREFIX}", prefix)
            .Replace("{N}", n.ToString())
            .Replace("{SHEET}", sheet.ToString())
            .Replace("{COL}", column.ToString());

    /// <summary>Next free tag for <paramref name="prefix"/> (lowest unused counter).</summary>
    public static string NextTag(ElectricalProject project, string prefix, int sheet = 0, int column = 0)
    {
        var used = new HashSet<string>(project.Devices.Select(d => d.Tag), StringComparer.OrdinalIgnoreCase);
        used.UnionWith(project.TerminalStrips.Select(t => t.Tag));
        used.UnionWith(project.PlcStations.Select(p => p.Tag));
        for (int n = 1; n < 100000; n++)
        {
            var tag = Format(project.Settings, prefix, n, sheet, column);
            if (!used.Contains(tag)) return tag;
        }
        throw new InvalidOperationException("No free tag available.");
    }

    /// <summary>Creates a device for the symbol with the next free tag (or reuses an existing tag).</summary>
    public static Device GetOrCreateDevice(ElectricalProject project, SymbolDefinition def, string? requestedTag = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedTag) && project.FindDeviceByTag(requestedTag) is { } existing) return existing;
        var device = new Device
        {
            Prefix = def.DefaultTagPrefix,
            Category = def.Category.ToString(),
            Tag = string.IsNullOrWhiteSpace(requestedTag) ? NextTag(project, def.DefaultTagPrefix) : requestedTag.Trim(),
            TagLocked = !string.IsNullOrWhiteSpace(requestedTag),
        };
        project.Devices.Add(device);
        return device;
    }

    /// <summary>
    /// Renumbers all unlocked devices by order of first appearance (sheet, then left→right, top→bottom),
    /// grouped by prefix. Returns (old, new) tag pairs that changed.
    /// </summary>
    public static IReadOnlyList<(string oldTag, string newTag)> RenumberAll(ElectricalProject project)
    {
        var occ = project.OccurrencesByDevice();
        var firstSeen = project.Devices
            .Select(d => (device: d, first: occ[d.Id].OrderBy(o => o.Sheet.Number).ThenBy(o => o.Instance.Position.X).ThenByDescending(o => o.Instance.Position.Y).FirstOrDefault()))
            .ToList();
        var changes = new List<(string, string)>();
        var locked = new HashSet<string>(project.Devices.Where(d => d.TagLocked).Select(d => d.Tag), StringComparer.OrdinalIgnoreCase);
        foreach (var group in firstSeen.Where(x => !x.device.TagLocked && x.first is not null).GroupBy(x => x.device.Prefix))
        {
            int n = 1;
            foreach (var (device, _) in group.OrderBy(x => x.first!.Sheet.Number).ThenBy(x => x.first!.Instance.Position.X).ThenByDescending(x => x.first!.Instance.Position.Y))
            {
                string tag;
                do tag = Format(project.Settings, group.Key, n++);
                while (locked.Contains(tag));
                if (!string.Equals(tag, device.Tag, StringComparison.Ordinal)) changes.Add((device.Tag, tag));
                device.Tag = tag;
            }
        }
        // Keep PLC channels / motors referencing tags in sync.
        foreach (var m in project.Motors)
            if (m.DeviceId is { } did && project.FindDevice(did) is { } d) m.Tag = d.Tag;
        return changes;
    }

    public static IReadOnlyList<string> DuplicateTags(ElectricalProject project) =>
        project.Devices.GroupBy(d => d.Tag, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .OrderBy(t => t, NaturalStringComparer.Instance).ToList();
}
