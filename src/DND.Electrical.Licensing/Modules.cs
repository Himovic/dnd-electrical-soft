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
        new ModuleDescriptor(EngineeringModule.AUTO, "AUTO", "Automation Engineering", "PLC, instrumentation, loops, alarms, cause and effect, architecture", "Implemented but not fully validated"),
        new ModuleDescriptor(EngineeringModule.POWER, "POWER", "Power Systems Engineering (D&D PowerStudio)", "Load flow, IEC 60909 short circuit, protection coordination, harmonics", "Implemented and tested (arc flash and dynamic studies planned)"),
        new ModuleDescriptor(EngineeringModule.ThreeD, "3D", "3D Engineering", "3D cabinets, trays, clashes, sections, glTF/STL", "Implemented and tested (STEP/IGES blocked by external dependency)"),
        new ModuleDescriptor(EngineeringModule.BIM, "BIM", "BIM Engineering", "IFC4 export/import with engineering identities", "Implemented and tested (box geometry; MEP routing planned)"),
        new ModuleDescriptor(EngineeringModule.INFRA, "INFRA", "Infrastructure Engineering", "Railway, data centre, oil and gas, solar templates and calculators", "Implemented but not fully validated"),
        new ModuleDescriptor(EngineeringModule.TWIN, "TWIN", "Digital Twin", "Live values, alarms, trends (Modbus TCP, MQTT, simulator)", "Implemented and tested (OPC UA blocked by external dependency)"),
        new ModuleDescriptor(EngineeringModule.AI, "AI", "AI Engineering Assistant", "Local or Claude assistant, confirmed proposals, audit trail", "Implemented and tested"),
        new ModuleDescriptor(EngineeringModule.DOCS, "DOCS", "Documentation Engineering", "Reports, drawing sets and exports", "Implemented and tested"),
        new ModuleDescriptor(EngineeringModule.CATALOG, "CATALOG", "Engineering Equipment Catalog", "Manufacturer-neutral equipment database", "Implemented, partially validated"),
        new ModuleDescriptor(EngineeringModule.PROJECT, "PROJECT", "Project Management", "Lifecycle, revisions, approvals, registers", "Implemented, partially validated"),
        new ModuleDescriptor(EngineeringModule.INTEGRATION, "INTEGRATION", "External Integrations", "ERP BOM file, CMMS assets, signed webhook", "Partially implemented (SAP, PLM, SharePoint planned)"),
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
