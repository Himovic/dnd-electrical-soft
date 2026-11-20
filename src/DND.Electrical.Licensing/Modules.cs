namespace DND.Electrical.Licensing;

/// <summary>Commercially licensable engineering modules.</summary>
public enum EngineeringModule
{
    ELEC, AUTO, POWER, ThreeD, BIM, INFRA, TWIN, AI, DOCS, CATALOG, PROJECT, INTEGRATION,
}

public sealed record ModuleDescriptor(EngineeringModule Module, string Code, string Name, string Purpose, string Maturity);

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDescriptor> All = new[]
    {
        new ModuleDescriptor(EngineeringModule.ELEC, "ELEC", "Electrical Engineering", "Electrical design, schematics, calculations", "Implemented and tested"),
        new ModuleDescriptor(EngineeringModule.AUTO, "AUTO", "Automation Engineering", "PLC, instrumentation and control", "Implemented, partially validated"),
        new ModuleDescriptor(EngineeringModule.POWER, "POWER", "Power Systems Engineering (D&D PowerStudio)", "Network modelling, load flow, short circuit", "Partially implemented"),
        new ModuleDescriptor(EngineeringModule.ThreeD, "3D", "3D Engineering", "3D layouts, cabinets, collision checks", "Partially implemented"),
        new ModuleDescriptor(EngineeringModule.BIM, "BIM", "BIM Engineering", "IFC coordination and MEP objects", "Partially implemented"),
        new ModuleDescriptor(EngineeringModule.INFRA, "INFRA", "Infrastructure Engineering", "Railway, data centre, oil and gas, solar templates", "Partially implemented"),
        new ModuleDescriptor(EngineeringModule.TWIN, "TWIN", "Digital Twin", "Operational monitoring and telemetry", "Partially implemented (simulator + Modbus TCP)"),
        new ModuleDescriptor(EngineeringModule.AI, "AI", "AI Engineering Assistant", "Engineering assistance with reviewed proposals", "Partially implemented (offline provider)"),
        new ModuleDescriptor(EngineeringModule.DOCS, "DOCS", "Documentation Engineering", "Reports, drawing sets and exports", "Implemented and tested"),
        new ModuleDescriptor(EngineeringModule.CATALOG, "CATALOG", "Engineering Equipment Catalog", "Manufacturer-neutral equipment database", "Implemented, partially validated"),
        new ModuleDescriptor(EngineeringModule.PROJECT, "PROJECT", "Project Management", "Lifecycle, revisions, approvals, registers", "Implemented, partially validated"),
        new ModuleDescriptor(EngineeringModule.INTEGRATION, "INTEGRATION", "External Integrations", "ERP, PLM, SCADA, OPC UA, MQTT, Modbus", "Partially implemented"),
    };

    public static ModuleDescriptor Get(EngineeringModule m) => All.First(d => d.Module == m);

    public static string Code(EngineeringModule m) => Get(m).Code;

    public static bool TryParse(string code, out EngineeringModule module)
    {
        var d = All.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        module = d?.Module ?? default;
        return d is not null;
    }
}
