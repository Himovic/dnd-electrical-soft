using System.Globalization;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.PLC;

/// <summary>
/// Addressing convention of a PLC family. Profiles are generic structural templates; they do not
/// contain manufacturer catalogue data and do not imply integration with vendor programming tools.
/// Address formats must be verified against the vendor documentation of the actual hardware.
/// </summary>
public sealed class PlcProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// Address templates per signal kind. Tokens: {BYTE}, {BIT}, {WORD}, {SLOT}, {CH}, {RACK}, {OCT} (octal channel index).
    /// </summary>
    public Dictionary<PlcSignalKind, string> AddressFormats { get; init; } = new();

    /// <summary>Bits per byte-addressed digital group (8 for byte-oriented families).</summary>
    public int DigitalGroupSize { get; init; } = 8;

    /// <summary>Bytes per analog channel for word-addressed analog (2 for 16-bit words).</summary>
    public int AnalogBytesPerChannel { get; init; } = 2;

    public static readonly IReadOnlyList<PlcProfile> BuiltIn = new[]
    {
        new PlcProfile
        {
            Id = "generic-iec", Name = "Generic IEC 61131-3", Vendor = "Generic",
            Notes = "Direct addresses according to IEC 61131-3 (%IX/%QX/%IW/%QW).",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "%IX{BYTE}.{BIT}", [PlcSignalKind.DigitalOutput] = "%QX{BYTE}.{BIT}", [PlcSignalKind.AnalogInput] = "%IW{WORD}", [PlcSignalKind.AnalogOutput] = "%QW{WORD}" },
        },
        new PlcProfile
        {
            Id = "siemens-style", Name = "Siemens-style byte/bit addressing", Vendor = "Siemens",
            Notes = "Byte.bit process image addressing (I/Q, IW/QW). Verify against the hardware configuration.",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "%I{BYTE}.{BIT}", [PlcSignalKind.DigitalOutput] = "%Q{BYTE}.{BIT}", [PlcSignalKind.AnalogInput] = "%IW{WORD}", [PlcSignalKind.AnalogOutput] = "%QW{WORD}" },
        },
        new PlcProfile
        {
            Id = "rockwell-style", Name = "Rockwell-style tag addressing", Vendor = "Rockwell Automation",
            Notes = "Local chassis module tags (Local:slot:I.Data.bit). Verify against the controller organizer.",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "Local:{SLOT}:I.Data.{CH}", [PlcSignalKind.DigitalOutput] = "Local:{SLOT}:O.Data.{CH}", [PlcSignalKind.AnalogInput] = "Local:{SLOT}:I.Ch{CH}Data", [PlcSignalKind.AnalogOutput] = "Local:{SLOT}:O.Ch{CH}Data" },
        },
        new PlcProfile
        {
            Id = "schneider-style", Name = "Schneider-style topological addressing", Vendor = "Schneider Electric",
            Notes = "Rack.slot.channel topological addresses. Verify against the project configuration.",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "%I{RACK}.{SLOT}.{CH}", [PlcSignalKind.DigitalOutput] = "%Q{RACK}.{SLOT}.{CH}", [PlcSignalKind.AnalogInput] = "%IW{RACK}.{SLOT}.{CH}", [PlcSignalKind.AnalogOutput] = "%QW{RACK}.{SLOT}.{CH}" },
        },
        new PlcProfile
        {
            Id = "mitsubishi-style", Name = "Mitsubishi-style X/Y device addressing", Vendor = "Mitsubishi Electric",
            Notes = "X/Y devices numbered in octal (FX-style). Verify the numbering base for your CPU series.",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "X{OCT}", [PlcSignalKind.DigitalOutput] = "Y{OCT}", [PlcSignalKind.AnalogInput] = "D{WORD}", [PlcSignalKind.AnalogOutput] = "D{WORD}" },
        },
        new PlcProfile
        {
            Id = "omron-style", Name = "Omron-style CIO word.bit addressing", Vendor = "Omron",
            Notes = "CIO word.bit addressing. Verify word allocation in the I/O table.",
            DigitalGroupSize = 16, AnalogBytesPerChannel = 1,
            AddressFormats = { [PlcSignalKind.DigitalInput] = "CIO {WORD}.{BIT:00}", [PlcSignalKind.DigitalOutput] = "CIO {WORD}.{BIT:00}", [PlcSignalKind.AnalogInput] = "CIO {WORD}", [PlcSignalKind.AnalogOutput] = "CIO {WORD}" },
        },
        new PlcProfile
        {
            Id = "codesys-style", Name = "CODESYS-based (ABB, WAGO, Phoenix Contact…)", Vendor = "ABB / WAGO / Phoenix Contact",
            Notes = "IEC 61131-3 direct addresses used by CODESYS-based controllers. Many platforms prefer symbolic variables.",
            AddressFormats = { [PlcSignalKind.DigitalInput] = "%IX{BYTE}.{BIT}", [PlcSignalKind.DigitalOutput] = "%QX{BYTE}.{BIT}", [PlcSignalKind.AnalogInput] = "%IW{WORD}", [PlcSignalKind.AnalogOutput] = "%QW{WORD}" },
        },
    };

    public static PlcProfile Get(string id) => BuiltIn.FirstOrDefault(p => p.Id == id) ?? BuiltIn[0];
}

/// <summary>Generic module types used to configure stations (no vendor catalogue data).</summary>
public sealed record PlcModuleType(string Id, string Name, PlcModuleKind Kind, int Channels, string SignalType)
{
    public static readonly IReadOnlyList<PlcModuleType> Generic = new[]
    {
        new PlcModuleType("GEN-CPU", "CPU (generic)", PlcModuleKind.Cpu, 0, string.Empty),
        new PlcModuleType("GEN-PS", "Power supply module (generic)", PlcModuleKind.PowerSupply, 0, "24 V DC"),
        new PlcModuleType("GEN-DI8", "Digital input 8×24 V DC", PlcModuleKind.DigitalInput, 8, "24 V DC"),
        new PlcModuleType("GEN-DI16", "Digital input 16×24 V DC", PlcModuleKind.DigitalInput, 16, "24 V DC"),
        new PlcModuleType("GEN-DO8", "Digital output 8×24 V DC 0.5 A", PlcModuleKind.DigitalOutput, 8, "24 V DC"),
        new PlcModuleType("GEN-DO16", "Digital output 16×24 V DC 0.5 A", PlcModuleKind.DigitalOutput, 16, "24 V DC"),
        new PlcModuleType("GEN-RO8", "Relay output 8×", PlcModuleKind.DigitalOutput, 8, "Relay 230 V AC / 2 A"),
        new PlcModuleType("GEN-AI4", "Analog input 4×4-20 mA", PlcModuleKind.AnalogInput, 4, "4-20 mA"),
        new PlcModuleType("GEN-AI8", "Analog input 8×0-10 V / 4-20 mA", PlcModuleKind.AnalogInput, 8, "0-10 V / 4-20 mA"),
        new PlcModuleType("GEN-AO4", "Analog output 4×4-20 mA", PlcModuleKind.AnalogOutput, 4, "4-20 mA"),
        new PlcModuleType("GEN-COM", "Communication module (Ethernet)", PlcModuleKind.Communication, 0, string.Empty),
        new PlcModuleType("GEN-RIO", "Remote I/O adapter", PlcModuleKind.RemoteIoAdapter, 0, string.Empty),
    };

    public static PlcModuleType? Find(string id) => Generic.FirstOrDefault(m => m.Id == id);
}

public sealed record PlcIoRow(string Station, string Rack, int Slot, string Module, int Channel, string Kind, string Address, string Tag,
    string Description, string SignalType, string Device, string SchematicLocation, bool Spare);

public sealed record PlcValidationIssue(string Severity, string Message, Guid? ChannelId);

/// <summary>PLC hardware configuration, address assignment, I/O lists and validation.</summary>
public static class PlcService
{
    public static PlcStation CreateStation(ElectricalProject project, string tag, string name, string profileId)
    {
        var st = new PlcStation { Tag = tag, Name = name, ProfileId = profileId };
        st.Racks.Add(new PlcRack { Index = 0, Name = "Rack 0" });
        project.PlcStations.Add(st);
        return st;
    }

    /// <summary>Adds a module of a generic type to a rack and assigns addresses to its channels.</summary>
    public static PlcModule AddModule(PlcStation station, PlcRack rack, string moduleTypeId)
    {
        var type = PlcModuleType.Find(moduleTypeId) ?? throw new KeyNotFoundException($"Module type '{moduleTypeId}' not found.");
        var slot = rack.Modules.Count == 0 ? 0 : rack.Modules.Max(m => m.Slot) + 1;
        var module = new PlcModule { Slot = slot, Kind = type.Kind, TypeName = type.Name, PartNumber = type.Id, Manufacturer = "Generic" };
        var kind = type.Kind switch
        {
            PlcModuleKind.DigitalInput => PlcSignalKind.DigitalInput,
            PlcModuleKind.DigitalOutput => PlcSignalKind.DigitalOutput,
            PlcModuleKind.AnalogInput => PlcSignalKind.AnalogInput,
            _ => PlcSignalKind.AnalogOutput,
        };
        for (int i = 0; i < type.Channels; i++)
            module.Channels.Add(new PlcChannel { Index = i, Kind = kind, SignalType = type.SignalType });
        rack.Modules.Add(module);
        AssignAddresses(station);
        return module;
    }

    /// <summary>
    /// Recomputes start addresses for all modules (separate counters for inputs/outputs, digital/analog
    /// as the profile dictates) and formats channel addresses.
    /// </summary>
    public static void AssignAddresses(PlcStation station)
    {
        var profile = PlcProfile.Get(station.ProfileId);
        int inBytes = 0, outBytes = 0, aiWords = 64, aoWords = 64, inWord = 0, outWord = 100;
        var octIn = 0; var octOut = 0;
        foreach (var rack in station.Racks.OrderBy(r => r.Index))
            foreach (var m in rack.Modules.OrderBy(m => m.Slot))
            {
                switch (m.Kind)
                {
                    case PlcModuleKind.DigitalInput:
                        m.StartAddress = profile.DigitalGroupSize == 16 ? inWord : inBytes;
                        foreach (var c in m.Channels)
                            c.Address = Format(profile, c.Kind, m.StartAddress + c.Index / profile.DigitalGroupSize, c.Index % profile.DigitalGroupSize, rack.Index, m.Slot, c.Index, octIn + c.Index);
                        var groupsIn = (m.Channels.Count + profile.DigitalGroupSize - 1) / profile.DigitalGroupSize;
                        if (profile.DigitalGroupSize == 16) inWord += groupsIn; else inBytes += groupsIn;
                        octIn += RoundUp(m.Channels.Count, 8);
                        break;
                    case PlcModuleKind.DigitalOutput:
                        m.StartAddress = profile.DigitalGroupSize == 16 ? outWord : outBytes;
                        foreach (var c in m.Channels)
                            c.Address = Format(profile, c.Kind, m.StartAddress + c.Index / profile.DigitalGroupSize, c.Index % profile.DigitalGroupSize, rack.Index, m.Slot, c.Index, octOut + c.Index);
                        var groupsOut = (m.Channels.Count + profile.DigitalGroupSize - 1) / profile.DigitalGroupSize;
                        if (profile.DigitalGroupSize == 16) outWord += groupsOut; else outBytes += groupsOut;
                        octOut += RoundUp(m.Channels.Count, 8);
                        break;
                    case PlcModuleKind.AnalogInput:
                        m.StartAddress = aiWords;
                        foreach (var c in m.Channels)
                            c.Address = Format(profile, c.Kind, 0, 0, rack.Index, m.Slot, c.Index, 0, aiWords + c.Index * profile.AnalogBytesPerChannel);
                        aiWords += m.Channels.Count * profile.AnalogBytesPerChannel;
                        break;
                    case PlcModuleKind.AnalogOutput:
                        m.StartAddress = aoWords;
                        foreach (var c in m.Channels)
                            c.Address = Format(profile, c.Kind, 0, 0, rack.Index, m.Slot, c.Index, 0, aoWords + c.Index * profile.AnalogBytesPerChannel);
                        aoWords += m.Channels.Count * profile.AnalogBytesPerChannel;
                        break;
                }
            }
    }

    private static int RoundUp(int v, int m) => (v + m - 1) / m * m;

    public static string Format(PlcProfile profile, PlcSignalKind kind, int @byte, int bit, int rack, int slot, int ch, int linear, int word = -1)
    {
        var fmt = profile.AddressFormats.GetValueOrDefault(kind, "{BYTE}.{BIT}");
        var w = word >= 0 ? word : @byte;
        return fmt
            .Replace("{BYTE}", @byte.ToString(CultureInfo.InvariantCulture))
            .Replace("{BIT:00}", bit.ToString("00", CultureInfo.InvariantCulture))
            .Replace("{BIT}", bit.ToString(CultureInfo.InvariantCulture))
            .Replace("{WORD}", w.ToString(CultureInfo.InvariantCulture))
            .Replace("{RACK}", rack.ToString(CultureInfo.InvariantCulture))
            .Replace("{SLOT}", slot.ToString(CultureInfo.InvariantCulture))
            .Replace("{CH}", ch.ToString(CultureInfo.InvariantCulture))
            .Replace("{OCT}", Convert.ToString(linear, 8).PadLeft(3, '0'));
    }

    public static IReadOnlyList<PlcIoRow> IoList(ElectricalProject project)
    {
        var instances = Schematic.Services.ProjectQueries.SymbolInstances(project).ToDictionary(x => x.instance.Id);
        var rows = new List<PlcIoRow>();
        foreach (var st in project.PlcStations.OrderBy(s => s.Tag, NaturalStringComparer.Instance))
            foreach (var rack in st.Racks.OrderBy(r => r.Index))
                foreach (var m in rack.Modules.OrderBy(m => m.Slot))
                    foreach (var c in m.Channels.OrderBy(c => c.Index))
                    {
                        var dev = c.LinkedDeviceId is { } d ? project.FindDevice(d)?.Tag ?? string.Empty : string.Empty;
                        var loc = c.SymbolInstanceId is { } sid && instances.TryGetValue(sid, out var x)
                            ? Schematic.Services.SheetLocation.Of(x.sheet, x.instance.Position, project.Settings).ToString() : string.Empty;
                        rows.Add(new PlcIoRow(st.Tag, rack.Name, m.Slot, m.TypeName, c.Index, KindCode(c.Kind), c.Address, c.Tag, c.Description,
                            c.SignalType, dev, loc, c.IsSpare));
                    }
        return rows;
    }

    public static string KindCode(PlcSignalKind k) => k switch
    {
        PlcSignalKind.DigitalInput => "DI",
        PlcSignalKind.DigitalOutput => "DO",
        PlcSignalKind.AnalogInput => "AI",
        _ => "AO",
    };

    /// <summary>Links a schematic PLC I/O symbol to a channel (both directions).</summary>
    public static Result LinkSymbol(ElectricalProject project, PlcChannel channel, Schematic.Entities.SymbolInstanceEntity instance)
    {
        var expected = channel.Kind switch
        {
            PlcSignalKind.DigitalInput => "IEC.PLC.DI",
            PlcSignalKind.DigitalOutput => "IEC.PLC.DO",
            PlcSignalKind.AnalogInput => "IEC.PLC.AI",
            _ => "IEC.PLC.AO",
        };
        if (!string.Equals(instance.SymbolId, expected, StringComparison.OrdinalIgnoreCase))
            return Result.Fail($"Channel {channel.Address} ({KindCode(channel.Kind)}) cannot be linked to symbol {instance.SymbolId}.");
        foreach (var other in project.PlcStations.SelectMany(s => s.Channels).Where(c => c.SymbolInstanceId == instance.Id)) other.SymbolInstanceId = null;
        channel.SymbolInstanceId = instance.Id;
        instance.PlcChannelId = channel.Id;
        return Result.Ok();
    }

    public static IReadOnlyList<PlcValidationIssue> Validate(ElectricalProject project)
    {
        var issues = new List<PlcValidationIssue>();
        var instances = Schematic.Services.ProjectQueries.SymbolInstances(project).Select(x => x.instance).ToDictionary(i => i.Id);
        foreach (var st in project.PlcStations)
        {
            var profile = PlcProfile.Get(st.ProfileId);
            foreach (var dup in st.Channels.Where(c => c.Address.Length > 0).GroupBy(c => c.Address, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var c in dup) issues.Add(new("Error", $"{st.Tag}: duplicate PLC address {c.Address}.", c.Id));
            foreach (var dup in st.Channels.Where(c => c.Tag.Length > 0).GroupBy(c => c.Tag, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var c in dup) issues.Add(new("Warning", $"{st.Tag}: PLC tag '{c.Tag}' used on several channels.", c.Id));
            foreach (var c in st.Channels)
            {
                if (string.IsNullOrWhiteSpace(c.Address)) issues.Add(new("Error", $"{st.Tag}: channel {c.Index} has no address.", c.Id));
                else if (!MatchesProfile(profile, c)) issues.Add(new("Warning", $"{st.Tag}: address {c.Address} does not follow the {profile.Name} convention.", c.Id));
                if (c.SymbolInstanceId is { } sid && !instances.ContainsKey(sid))
                    issues.Add(new("Error", $"{st.Tag}: channel {c.Address} refers to a deleted schematic symbol.", c.Id));
                if (!c.IsSpare && string.IsNullOrWhiteSpace(c.Description))
                    issues.Add(new("Info", $"{st.Tag}: channel {c.Address} ({c.Tag}) has no signal description.", c.Id));
            }
        }
        foreach (var inst in instances.Values.Where(i => i.Definition?.Role == Symbols.SymbolRole.PlcIo && i.PlcChannelId is null))
            issues.Add(new("Warning", $"PLC I/O symbol without assigned channel ({inst.SymbolId}).", null));
        return issues;
    }

    private static bool MatchesProfile(PlcProfile profile, PlcChannel c)
    {
        var fmt = profile.AddressFormats.GetValueOrDefault(c.Kind);
        if (fmt is null) return true;
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(fmt)
            .Replace(@"\{BYTE}", @"\d+").Replace(@"\{BIT:00}", @"\d{2}").Replace(@"\{BIT}", @"\d+").Replace(@"\{WORD}", @"\d+")
            .Replace(@"\{RACK}", @"\d+").Replace(@"\{SLOT}", @"\d+").Replace(@"\{CH}", @"\d+").Replace(@"\{OCT}", "[0-7]+") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(c.Address, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
