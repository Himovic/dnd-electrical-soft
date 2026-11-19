namespace DND.Electrical.Domain;

/// <summary>
/// An electrical device (component) identified by a tag. A device may be represented by several
/// symbols across sheets (coil + contacts) and by a cabinet layout item.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Function { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public Guid? CatalogProductId { get; set; }

    /// <summary>When true, the tag was entered manually and automatic re-tagging leaves it unchanged.</summary>
    public bool TagLocked { get; set; }

    /// <summary>Engineering properties such as rating, voltage, coil voltage, settings.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    public string Notes { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Function) ? Tag : $"{Tag} {Function}";

    public string? GetProperty(string key) => Properties.TryGetValue(key, out var v) ? v : null;
}

public static class DeviceProperties
{
    public const string Rating = "Rating";
    public const string Voltage = "Voltage";
    public const string CoilVoltage = "CoilVoltage";
    public const string CurrentSetting = "CurrentSetting";
    public const string BreakingCapacity = "BreakingCapacity";
    public const string Curve = "Curve";
    public const string Poles = "Poles";
}

public sealed class Cable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string CableType { get; set; } = string.Empty;
    public int CoreCount { get; set; } = 3;
    public double CrossSectionMm2 { get; set; } = 1.5;
    public double LengthM { get; set; }
    public ConductorMaterial Material { get; set; } = ConductorMaterial.Copper;
    public InsulationType Insulation { get; set; } = InsulationType.PVC;
    public bool Shielded { get; set; }
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public Guid? FromDeviceId { get; set; }
    public Guid? ToDeviceId { get; set; }
    public string Route { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public double? OuterDiameterMm { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<CableCore> Cores { get; set; } = new();

    /// <summary>Creates cores with IEC 60445 style markings when none exist.</summary>
    public void EnsureCores()
    {
        var markings = CoreCount switch
        {
            2 => new[] { "BU", "BN" },
            3 => new[] { "BN", "BU", "GNYE" },
            4 => new[] { "BN", "BK", "GY", "GNYE" },
            5 => new[] { "BN", "BK", "GY", "BU", "GNYE" },
            _ => Enumerable.Range(1, CoreCount).Select(i => i.ToString()).ToArray(),
        };
        for (int i = Cores.Count; i < CoreCount; i++)
            Cores.Add(new CableCore { Index = i + 1, Marking = markings[Math.Min(i, markings.Length - 1)] });
        if (Cores.Count > CoreCount) Cores.RemoveRange(CoreCount, Cores.Count - CoreCount);
    }

    public string Designation => $"{CableType} {CoreCount}G{CrossSectionMm2:0.##}".Trim();
}

public sealed class CableCore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public string Marking { get; set; } = string.Empty;

    /// <summary>Wire entity (schematic connection) carried by this core.</summary>
    public Guid? WireId { get; set; }

    public string Notes { get; set; } = string.Empty;
    public bool IsSpare => WireId is null;
}

public sealed class TerminalStrip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "-X1";
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TerminalPartNumber { get; set; } = string.Empty;
    public List<TerminalDefinition> Terminals { get; set; } = new();
}

public sealed class TerminalDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "1";
    public int Level { get; set; } = 1;
    public TerminalType Type { get; set; } = TerminalType.FeedThrough;

    /// <summary>Terminals sharing the same jumper group are bridged.</summary>
    public string? JumperGroup { get; set; }

    public string Description { get; set; } = string.Empty;
    public int SortIndex { get; set; }
}

public sealed class PlcStation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "-A1";
    public string Name { get; set; } = string.Empty;
    public string ProfileId { get; set; } = "generic-iec";
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public List<PlcRack> Racks { get; set; } = new();

    public IEnumerable<PlcModule> Modules => Racks.SelectMany(r => r.Modules);
    public IEnumerable<PlcChannel> Channels => Modules.SelectMany(m => m.Channels);
}

public sealed class PlcRack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public string Name { get; set; } = "Rack 0";
    public bool IsRemote { get; set; }
    public List<PlcModule> Modules { get; set; } = new();
}

public sealed class PlcModule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Slot { get; set; }
    public PlcModuleKind Kind { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Start byte (or word) offset used by the address generator.</summary>
    public int StartAddress { get; set; }

    public List<PlcChannel> Channels { get; set; } = new();
}

public sealed class PlcChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public PlcSignalKind Kind { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SignalType { get; set; } = "24 V DC";
    public Guid? LinkedDeviceId { get; set; }
    public Guid? SymbolInstanceId { get; set; }
    public string TerminalReference { get; set; } = string.Empty;
    public bool IsSpare => string.IsNullOrWhiteSpace(Tag) && LinkedDeviceId is null && SymbolInstanceId is null;
}

public sealed class Motor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DeviceId { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double PowerKw { get; set; }
    public double VoltageV { get; set; } = 400;
    public int Phases { get; set; } = 3;
    public double FrequencyHz { get; set; } = 50;
    public double RatedCurrentA { get; set; }
    public double SpeedRpm { get; set; } = 1450;
    public double PowerFactor { get; set; } = 0.85;
    public double Efficiency { get; set; } = 0.9;
    public MotorStartingMethod StartingMethod { get; set; } = MotorStartingMethod.DirectOnLine;

    /// <summary>Locked-rotor (starting) current ratio Ist/In.</summary>
    public double StartingCurrentRatio { get; set; } = 7;

    public string Duty { get; set; } = "S1";
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string DatasheetUrl { get; set; } = string.Empty;
    public string ProtectionDeviceTag { get; set; } = string.Empty;
    public string CabinetTag { get; set; } = string.Empty;
    public Guid? PlcChannelId { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class Cabinet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "+CAB1";
    public string Name { get; set; } = string.Empty;
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 1200;
    public double Depth { get; set; } = 300;
    public double PlateWidth { get; set; } = 700;
    public double PlateHeight { get; set; } = 1100;

    /// <summary>Minimum clearance (mm) required between devices.</summary>
    public double ClearanceMm { get; set; } = 0;

    public string EnclosurePartNumber { get; set; } = string.Empty;
    public string IpRating { get; set; } = "IP54";
    public List<CabinetItem> Items { get; set; } = new();
}

public sealed class CabinetItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CabinetItemKind Kind { get; set; } = CabinetItemKind.Device;
    public CabinetFace Face { get; set; } = CabinetFace.MountingPlate;
    public Guid? DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>Bottom-left corner on the mounting plate / door, in mm.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 18;
    public double Height { get; set; } = 90;
    public double Depth { get; set; } = 70;

    /// <summary>DIN rail the device is clipped onto, if any.</summary>
    public Guid? RailId { get; set; }

    public string Notes { get; set; } = string.Empty;
}

/// <summary>Product catalogue entry. Demo entries are fictional and flagged with <see cref="IsDemoData"/>.</summary>
public sealed class CatalogProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Dictionary<string, string> Specifications { get; set; } = new();
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Supplier { get; set; } = string.Empty;
    public string SupplierReference { get; set; } = string.Empty;
    public string StockReference { get; set; } = string.Empty;
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double DepthMm { get; set; }
    public bool IsDemoData { get; set; }
    public string Source { get; set; } = string.Empty;
    public List<string> Alternatives { get; set; } = new();
    public string DatasheetUrl { get; set; } = string.Empty;
}

public sealed class ProcurementEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public ProcurementStatus Status { get; set; } = ProcurementStatus.NotOrdered;
    public string Supplier { get; set; } = string.Empty;
    public string PurchaseOrder { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Electrical load used for load lists, balancing and distribution calculations.</summary>
public sealed class ElectricalLoad
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Board { get; set; } = string.Empty;
    public string Circuit { get; set; } = string.Empty;
    public LoadCategory Category { get; set; } = LoadCategory.Other;
    public PhaseAssignment Phase { get; set; } = PhaseAssignment.ThreePhase;
    public double PowerKw { get; set; }
    public double PowerFactor { get; set; } = 0.9;
    public double DemandFactor { get; set; } = 1;
    public double VoltageV { get; set; } = 400;
    public double CableLengthM { get; set; }
    public double CableCrossSectionMm2 { get; set; }
    public double ProtectionRatingA { get; set; }
    public string ProtectionDeviceTag { get; set; } = string.Empty;
    public Guid? CableId { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class CalculationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CalculationType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Author { get; set; } = string.Empty;
    public string StandardProfileId { get; set; } = string.Empty;
    public string InputsJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public string Summary { get; set; } = string.Empty;
    public CalculationStatus Status { get; set; } = CalculationStatus.Informational;
    public Guid? LinkedObjectId { get; set; }
}

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset Added { get; set; } = DateTimeOffset.Now;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public long Size => Content.LongLength;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string User { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class ValidationRunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int Infos { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Copy of a symbol definition stored in the project so it opens without the original library.</summary>
public sealed class ProjectSymbol
{
    public string SymbolId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string LibraryId { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
}
