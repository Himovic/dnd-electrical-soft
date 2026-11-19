using System.Text.Json.Serialization;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.Symbols;

public enum SymbolCategory
{
    Protection,
    Switching,
    Contacts,
    Motors,
    Transformers,
    PowerSupplies,
    Drives,
    ControlDevices,
    Sensors,
    Signaling,
    Actuators,
    Terminals,
    Connectors,
    Potentials,
    Plc,
    General,
}

/// <summary>Electrical behaviour of a symbol, used by connectivity, numbering and cross-referencing.</summary>
public enum SymbolRole
{
    /// <summary>Self-contained device representation (motor, lamp, sensor, push button…).</summary>
    Standalone,
    /// <summary>Main function of a device whose contacts appear elsewhere (coil, overload element).</summary>
    Main,
    /// <summary>Contact belonging to a device whose main function is elsewhere (cross-referenced).</summary>
    ChildContact,
    /// <summary>Terminal of a terminal strip; pins are internally connected.</summary>
    Terminal,
    /// <summary>Defines a named potential (L1, N, PE, +24V, 0V…). Same names are connected project-wide.</summary>
    PotentialSource,
    /// <summary>Interruption point / off-sheet connector. Same names are connected project-wide.</summary>
    Interruption,
    /// <summary>PLC input/output channel.</summary>
    PlcIo,
    /// <summary>Plug/socket connector; pins are internally connected.</summary>
    Connector,
}

public enum ContactType { None, NormallyOpen, NormallyClosed, ChangeOver, Power }

public enum PinDirection { Up, Down, Left, Right }

public sealed class SymbolPin
{
    public string Id { get; set; } = "1";
    public string Label { get; set; } = "1";
    public Point2 Position { get; set; }

    /// <summary>Direction in which a connecting wire leaves the pin.</summary>
    public PinDirection Direction { get; set; } = PinDirection.Up;

    public bool ShowLabel { get; set; } = true;

    /// <summary>Optional electrical description (e.g. "+24 V DC supply", "Output").</summary>
    public string Description { get; set; } = string.Empty;

    public SymbolPin Clone() => (SymbolPin)MemberwiseClone();
}

public sealed class SymbolAttributeDefinition
{
    public string Name { get; set; } = SymbolAttributes.Tag;
    public Point2 Position { get; set; }
    public double Height { get; set; } = 2.5;
    public TextHAlign HAlign { get; set; } = TextHAlign.Right;
    public TextVAlign VAlign { get; set; } = TextVAlign.Middle;
    public bool Visible { get; set; } = true;
    public string DefaultValue { get; set; } = string.Empty;
    public string Layer { get; set; } = StandardLayers.Tags;

    public SymbolAttributeDefinition Clone() => (SymbolAttributeDefinition)MemberwiseClone();
}

/// <summary>Well-known attribute names.</summary>
public static class SymbolAttributes
{
    public const string Tag = "TAG";
    public const string Function = "FUNCTION";
    public const string Rating = "RATING";
    public const string Potential = "POTENTIAL";
    public const string Name = "NAME";
    public const string Address = "ADDRESS";
    public const string PlcTag = "PLCTAG";
    public const string TerminalNumber = "TERMINAL";
    public const string CrossReference = "XREF";
    public const string PartNumber = "PART";
}

/// <summary>
/// A reusable electrical symbol: graphics in local millimetre coordinates (insertion point at origin),
/// connection pins, attributes and electrical behaviour. Stored in JSON symbol libraries.
/// </summary>
public sealed class SymbolDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SymbolCategory Category { get; set; } = SymbolCategory.General;
    public SymbolRole Role { get; set; } = SymbolRole.Standalone;
    public ContactType ContactType { get; set; } = ContactType.None;

    /// <summary>Standard/convention the artwork follows, e.g. "IEC 60617 style".</summary>
    public string Standard { get; set; } = "IEC 60617 style";

    public int Version { get; set; } = 1;

    /// <summary>Default device tag prefix (letter code), e.g. "Q", "K", "M".</summary>
    public string DefaultTagPrefix { get; set; } = "A";

    /// <summary>Potential name for <see cref="SymbolRole.PotentialSource"/> symbols (overridable per instance).</summary>
    public string? DefaultPotential { get; set; }

    public List<CadEntity> Graphics { get; set; } = new();
    public List<SymbolPin> Pins { get; set; } = new();
    public List<SymbolAttributeDefinition> Attributes { get; set; } = new();

    /// <summary>Groups of pins that are electrically the same node (e.g. both sides of a terminal).</summary>
    public List<List<string>> InternalConnections { get; set; } = new();

    public List<string> Keywords { get; set; } = new();

    /// <summary>Catalogue part numbers commonly associated with this symbol (optional).</summary>
    public List<string> ManufacturerAssociations { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonIgnore]
    public Rect2 Bounds
    {
        get
        {
            var r = Rect2.Empty;
            foreach (var g in Graphics) r = r.Union(g.GetBounds());
            foreach (var p in Pins) r = r.Union(p.Position);
            return r;
        }
    }

    public SymbolPin? FindPin(string id) => Pins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public SymbolDefinition Clone() => new()
    {
        Id = Id, Name = Name, Description = Description, Category = Category, Role = Role, ContactType = ContactType,
        Standard = Standard, Version = Version, DefaultTagPrefix = DefaultTagPrefix, DefaultPotential = DefaultPotential,
        Graphics = Graphics.Select(g => g.Clone(keepId: true)).ToList(),
        Pins = Pins.Select(p => p.Clone()).ToList(),
        Attributes = Attributes.Select(a => a.Clone()).ToList(),
        InternalConnections = InternalConnections.Select(g => g.ToList()).ToList(),
        Keywords = Keywords.ToList(),
        ManufacturerAssociations = ManufacturerAssociations.ToList(),
        Metadata = new Dictionary<string, string>(Metadata),
    };

    /// <summary>Checks structural consistency. Returns a list of problems (empty when valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) errors.Add("Symbol id is required.");
        else if (Id.Any(c => char.IsWhiteSpace(c) || c is ':' or '/' or '\\')) errors.Add($"Symbol id '{Id}' contains invalid characters.");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Symbol name is required.");
        if (Graphics.Count == 0) errors.Add("Symbol has no graphics.");
        var dup = Pins.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0) errors.Add($"Duplicate pin ids: {string.Join(", ", dup)}.");
        foreach (var grp in InternalConnections)
            foreach (var pid in grp)
                if (FindPin(pid) is null) errors.Add($"Internal connection references unknown pin '{pid}'.");
        if (Role is SymbolRole.PotentialSource or SymbolRole.Interruption && Pins.Count == 0) errors.Add("Potential and interruption symbols need a pin.");
        if (Role == SymbolRole.ChildContact && ContactType == ContactType.None) errors.Add("Child contacts must declare a contact type.");
        if (string.IsNullOrWhiteSpace(DefaultTagPrefix) && Role is not (SymbolRole.PotentialSource or SymbolRole.Interruption))
            errors.Add("Default tag prefix is required.");
        return errors;
    }
}
