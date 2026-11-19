namespace DND.Electrical.Domain;

public enum ProjectStatus { Draft, InProgress, ForReview, Approved, ForConstruction, AsBuilt, Archived }

public enum ProjectIndustry
{
    General, Industrial, Manufacturing, OilAndGas, WaterTreatment, PowerDistribution,
    Building, Hvac, Automation, RenewableEnergy,
}

public enum SheetKind { Cover, Schematic, Power, Control, Plc, Terminal, Cabinet, Single_Line, Notes }

public enum RevisionStatus { Draft, Issued, Superseded }

public enum WireNumberingMode
{
    /// <summary>One number per potential (all wires of a potential share the number).</summary>
    PerPotential,
    /// <summary>Each wire (connection) gets its own number.</summary>
    PerWire,
}

public enum ConductorMaterial { Copper, Aluminium }

public enum InsulationType { PVC, XLPE, EPR, Rubber }

public enum TerminalType { FeedThrough, ProtectiveEarth, Fuse, Disconnect, Sensor, MultiLevel }

public enum PlcSignalKind { DigitalInput, DigitalOutput, AnalogInput, AnalogOutput }

public enum PlcModuleKind { Cpu, PowerSupply, DigitalInput, DigitalOutput, AnalogInput, AnalogOutput, Communication, RemoteIoAdapter }

public enum MotorStartingMethod { DirectOnLine, StarDelta, SoftStarter, VariableFrequencyDrive, Reversing }

public enum CabinetItemKind { Device, DinRail, CableDuct, TerminalStrip, Accessory, Fan, Light }

public enum CabinetFace { MountingPlate, Door }

public enum ProcurementStatus { NotOrdered, Requested, Quoted, Ordered, Delivered, Installed, Cancelled }

public enum CalculationStatus { Pass, Warning, Fail, Informational }

public enum LoadCategory { Lighting, Sockets, Motor, Hvac, Heating, ItEquipment, Process, Other }

public enum PhaseAssignment { L1, L2, L3, ThreePhase }
