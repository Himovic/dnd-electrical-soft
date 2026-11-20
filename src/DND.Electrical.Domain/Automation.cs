namespace DND.Electrical.Domain;

public enum MeasuredVariable { Pressure, Temperature, Level, Flow, Analysis, Speed, Position, Vibration, Weight, Gas, Flame, Other }

public enum InstrumentFunction { Transmitter, Switch, Indicator, Controller, ControlValve, OnOffValve, Analyzer, Detector, Actuator }

public enum InstrumentSignal { Analog4To20mA, Hart, Voltage0To10V, Digital24VDC, Namur, Fieldbus, Thermocouple, Rtd, Other }

public enum InstrumentStatus { Design, Specified, Procured, Installed, LoopChecked, Calibrated }

/// <summary>Field instrument (ISA-5.1 style tag). Hazardous-area fields record certificate data entered by the user.</summary>
public sealed class Instrument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MeasuredVariable Variable { get; set; } = MeasuredVariable.Pressure;
    public InstrumentFunction Function { get; set; } = InstrumentFunction.Transmitter;
    public InstrumentSignal Signal { get; set; } = InstrumentSignal.Analog4To20mA;
    public double RangeMin { get; set; }
    public double RangeMax { get; set; } = 100;
    public string Unit { get; set; } = "%";
    public string Area { get; set; } = string.Empty;
    public string ProcessLine { get; set; } = string.Empty;
    /// <summary>Loop the instrument belongs to (engineering identity of the loop).</summary>
    public Guid? LoopId { get; set; }
    /// <summary>Schematic device representing the instrument (shared engineering object identity).</summary>
    public Guid? DeviceId { get; set; }
    public Guid? PlcChannelId { get; set; }
    public string JunctionBox { get; set; } = string.Empty;
    public string HazardousZone { get; set; } = string.Empty;
    public string ExMarking { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public InstrumentStatus Status { get; set; } = InstrumentStatus.Design;
    public DateTimeOffset? CalibrationDue { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsAnalog => Signal is InstrumentSignal.Analog4To20mA or InstrumentSignal.Hart or InstrumentSignal.Voltage0To10V or InstrumentSignal.Thermocouple or InstrumentSignal.Rtd;
}

public enum LoopType { Monitoring, Pid, OnOff, Cascade, Ratio, Interlock, Sequence }

public sealed class ControlLoop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public LoopType Type { get; set; } = LoopType.Pid;
    public Guid? MeasurementId { get; set; }
    public Guid? FinalElementId { get; set; }
    /// <summary>Final element that is an electrical device (motor, VFD) rather than an instrument.</summary>
    public Guid? FinalDeviceId { get; set; }
    public string Controller { get; set; } = string.Empty;
    public double? SetPoint { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double? Gain { get; set; }
    public double? IntegralS { get; set; }
    public double? DerivativeS { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public enum AlarmType { HighHigh, High, Low, LowLow, Deviation, RateOfChange, Digital, Fault }
public enum AlarmPriority { Low, Medium, High, Critical }
public enum AlarmClass { Process, Safety, Equipment, Environmental, System }

/// <summary>Alarm definition (alarm database row) in line with an alarm philosophy defined by the project.</summary>
public sealed class AlarmDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public Guid? InstrumentId { get; set; }
    public Guid? DeviceId { get; set; }
    public AlarmType Type { get; set; } = AlarmType.High;
    public double? SetPoint { get; set; }
    public string Unit { get; set; } = string.Empty;
    public double Deadband { get; set; }
    public double DelayS { get; set; }
    public AlarmPriority Priority { get; set; } = AlarmPriority.Medium;
    public AlarmClass Class { get; set; } = AlarmClass.Process;
    public string Message { get; set; } = string.Empty;
    public string Consequence { get; set; } = string.Empty;
    public string OperatorAction { get; set; } = string.Empty;
}

public sealed class CauseEffectCause
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? InstrumentId { get; set; }
    public Guid? AlarmId { get; set; }
    public string Condition { get; set; } = string.Empty;
}

public sealed class CauseEffectEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
    public Guid? InstrumentId { get; set; }
    /// <summary>Safe-state action, e.g. Stop, Close, Open, Trip, De-energise.</summary>
    public string Action { get; set; } = "Stop";
}

public sealed class CauseEffectLink
{
    public Guid CauseId { get; set; }
    public Guid EffectId { get; set; }
    /// <summary>Matrix mark: "X" (action), "T" (time delayed), "N" (note) – project convention.</summary>
    public string Mark { get; set; } = "X";
}

/// <summary>Cause and effect matrix (interlock / shutdown logic documentation).</summary>
public sealed class CauseEffectMatrix
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Cause and effect";
    public string Revision { get; set; } = "A";
    public List<CauseEffectCause> Causes { get; set; } = new();
    public List<CauseEffectEffect> Effects { get; set; } = new();
    public List<CauseEffectLink> Links { get; set; } = new();
}

public enum ScadaNodeKind { ScadaServer, Hmi, EngineeringStation, Historian, Plc, RemoteIo, Drive, Switch, Firewall, Gateway, Router, Rtu, Ied, Sensor, Cloud }

/// <summary>Node of the control system (SCADA/DCS) architecture.</summary>
public sealed class ScadaNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ScadaNodeKind Kind { get; set; } = ScadaNodeKind.Plc;
    public string Network { get; set; } = "Control LAN";
    public string Zone { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    /// <summary>Upstream node this node is connected to (switch, gateway…).</summary>
    public Guid? ParentId { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public bool Redundant { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? PlcStationId { get; set; }
    public string Notes { get; set; } = string.Empty;
}
