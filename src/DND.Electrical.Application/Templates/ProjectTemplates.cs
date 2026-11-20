using DND.Electrical.Application.Demo;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Services;

namespace DND.Electrical.Application.Templates;

/// <summary>Reusable starting point for new projects (settings, initial sheets and supply structure).</summary>
public sealed class ProjectTemplate
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ProjectIndustry Industry { get; init; }
    public double Voltage { get; init; } = 400;
    public string EarthingSystem { get; init; } = "TN-S";
    public string StandardProfile { get; init; } = "IEC";
    public Action<CircuitBuilder>? BuildSheets { get; init; }

    public ElectricalProject Create(ProjectInfo info, SchematicService schematic)
    {
        var p = new ElectricalProject { Info = info.Clone() };
        p.Info.Industry = Industry;
        p.Info.TemplateId = Id;
        p.Info.Created = p.Info.Modified = DateTimeOffset.Now;
        p.Settings.NominalVoltage = Voltage;
        p.Settings.EarthingSystem = EarthingSystem;
        p.Settings.StandardProfileId = StandardProfile;
        p.Revisions.Add(new ProjectRevision { Code = p.Info.CurrentRevision, Description = "First issue (draft)", Author = info.Engineer });
        var b = new CircuitBuilder(p, schematic);
        CoverSheet(b);
        BuildSheets?.Invoke(b);
        return p;
    }

    public static Sheet CoverSheet(CircuitBuilder b)
    {
        var s = b.Sheet("Cover sheet", SheetKind.Cover, "Project cover and identification");
        var d = s.Drawing;
        var x = 40.0;
        d.Add(new TextEntity(new Point2(x, 230), "{PROJECT.NAME}", 12) { Layer = StandardLayers.Annotations, Bold = true });
        d.Add(new TextEntity(new Point2(x, 210), "{PROJECT.NUMBER}", 7) { Layer = StandardLayers.Annotations });
        d.Add(new TextEntity(new Point2(x, 195), "{PROJECT.DESCRIPTION}", 4) { Layer = StandardLayers.Annotations });
        string[] labels = { "Customer", "Site", "Company", "Engineer", "Revision", "Status" };
        string[] fields = { "{CUSTOMER.NAME}", "{PROJECT.SITE}", "{COMPANY.NAME}", "{ENGINEER}", "{PROJECT.REVISION}", "{PROJECT.STATUS}" };
        for (int i = 0; i < labels.Length; i++)
        {
            d.Add(new TextEntity(new Point2(x, 165 - i * 12), labels[i], 3) { Layer = StandardLayers.Annotations, Color = CadColor.Grey });
            d.Add(new TextEntity(new Point2(x + 45, 165 - i * 12), fields[i], 4) { Layer = StandardLayers.Annotations });
        }
        d.Add(new ImageEntity { Position = new Point2(300, 150), Width = 90, Height = 90, ImageKey = "branding:logo", Layer = StandardLayers.Annotations });
        d.Add(new TextEntity(new Point2(345, 140), "{PRODUCT}", 3.5) { Layer = StandardLayers.Annotations, HAlign = TextHAlign.Center });
        d.Add(new TextEntity(new Point2(345, 133), "{POWEREDBY}", 2.5) { Layer = StandardLayers.Annotations, HAlign = TextHAlign.Center, Color = CadColor.Grey });
        return s;
    }

    private static void PowerAndControl(CircuitBuilder b, string function)
    {
        var power = b.Sheet("Power distribution", SheetKind.Power, "Main supply and power circuits", function);
        foreach (var (name, i) in new[] { "L1", "L2", "L3", "N", "PE" }.Select((n, i) => (n, i)))
            b.Supply(power, name, CircuitBuilder.Col(1) + i * 10, 270);
        b.Sheet("Control circuits", SheetKind.Control, "24 V DC control circuits", function);
    }

    public static IReadOnlyList<ProjectTemplate> All { get; } = new[]
    {
        new ProjectTemplate { Id = "blank", Name = "Blank project", Description = "Cover sheet only.", Industry = ProjectIndustry.General },
        new ProjectTemplate { Id = "industrial", Name = "Industrial installation", Description = "Power distribution and control circuits for industrial plants.", Industry = ProjectIndustry.Industrial, BuildSheets = b => PowerAndControl(b, "=PLANT") },
        new ProjectTemplate { Id = "manufacturing", Name = "Manufacturing plant", Description = "Machine power, control and PLC sheets (IEC 60204-1 oriented).", Industry = ProjectIndustry.Manufacturing, StandardProfile = "IEC60204",
            BuildSheets = b => { PowerAndControl(b, "=MACH"); b.Sheet("PLC inputs", SheetKind.Plc, "Digital and analog inputs", "=MACH"); b.Sheet("PLC outputs", SheetKind.Plc, "Digital and analog outputs", "=MACH"); } },
        new ProjectTemplate { Id = "oilgas", Name = "Oil and Gas", Description = "MCC, ESD interface and instrumentation with hazardous-area records (zones and Ex markings are entered by the user, not derived).", Industry = ProjectIndustry.OilAndGas,
            BuildSheets = b => { PowerAndControl(b, "=MCC"); b.Sheet("ESD interface", SheetKind.Control, "Emergency shutdown interface (cause and effect in Automation)", "=ESD"); b.Sheet("Instrumentation", SheetKind.Plc, "Field instruments and loop references", "=INST"); b.Sheet("Hazardous area register", SheetKind.Notes, "Zone classification drawing reference and equipment Ex records", "=HAZ"); } },
        new ProjectTemplate { Id = "datacenter", Name = "Data center", Description = "Dual utility/generator incomers, A/B UPS systems, PDUs and rack distribution (redundancy, autonomy and PUE calculators).", Industry = ProjectIndustry.DataCenter,
            BuildSheets = b => { b.Sheet("Single-line diagram", SheetKind.Single_Line, "Utility, generators, UPS A/B, PDUs", "=DC"); b.Sheet("UPS system A", SheetKind.Power, "", "=UPS-A"); b.Sheet("UPS system B", SheetKind.Power, "", "=UPS-B"); b.Sheet("PDU and rack distribution", SheetKind.Power, "", "=PDU"); b.Sheet("EPMS / BMS interfaces", SheetKind.Plc, "Metering and monitoring points", "=EPMS"); } },
        new ProjectTemplate { Id = "solar", Name = "Solar PV plant", Description = "PV strings, combiner boxes, inverters, AC collection and grid connection (string sizing calculator).", Industry = ProjectIndustry.SolarPv,
            BuildSheets = b => { b.Sheet("PV array and strings", SheetKind.Power, "Module strings per inverter MPPT", "=PV"); b.Sheet("DC combiner boxes", SheetKind.Power, "", "=PV"); b.Sheet("Inverters", SheetKind.Power, "", "=INV"); b.Sheet("AC collection and grid connection", SheetKind.Power, "", "=GRID"); b.Sheet("Plant monitoring", SheetKind.Plc, "Power plant controller, meters and weather station", "=SCADA"); } },
        new ProjectTemplate { Id = "railway", Name = "Railway station / depot", Description = "Station auxiliary power: LV distribution, signalling UPS supplies, platform lighting and SCADA interfaces.", Industry = ProjectIndustry.Railway,
            BuildSheets = b => { b.Sheet("Station LV distribution", SheetKind.Power, "", "=STN"); b.Sheet("Signalling power supply", SheetKind.Power, "UPS-backed supplies", "=SIG"); b.Sheet("Platform lighting", SheetKind.Power, "", "=LTG"); b.Sheet("SCADA and telecom interfaces", SheetKind.Plc, "RTU, IEDs and control centre link", "=SCADA"); } },
        new ProjectTemplate { Id = "water", Name = "Water treatment", Description = "Pumping stations: motor feeders, instrumentation and PLC I/O.", Industry = ProjectIndustry.WaterTreatment,
            BuildSheets = b => { PowerAndControl(b, "=PS"); b.Sheet("Instrumentation", SheetKind.Plc, "Level, pressure and flow instruments", "=PS"); } },
        new ProjectTemplate { Id = "power", Name = "Power distribution", Description = "Transformer, main distribution board and outgoing feeders.", Industry = ProjectIndustry.PowerDistribution,
            BuildSheets = b => { b.Sheet("Main distribution board", SheetKind.Power, "Incoming supply and busbar", "=MDB"); b.Sheet("Outgoing feeders", SheetKind.Power, "", "=MDB"); } },
        new ProjectTemplate { Id = "building", Name = "Building installation", Description = "Distribution boards, lighting and socket circuits (single-phase 230 V).", Industry = ProjectIndustry.Building, StandardProfile = "NFC15100",
            BuildSheets = b => { b.Sheet("Distribution board", SheetKind.Power, "Protective devices", "=DB1"); b.Sheet("Lighting circuits", SheetKind.Power, "", "=DB1"); b.Sheet("Socket circuits", SheetKind.Power, "", "=DB1"); } },
        new ProjectTemplate { Id = "hvac", Name = "HVAC systems", Description = "Fans, pumps and compressors with VFDs and BMS interface.", Industry = ProjectIndustry.Hvac,
            BuildSheets = b => { PowerAndControl(b, "=AHU"); b.Sheet("Drives", SheetKind.Power, "Variable frequency drives", "=AHU"); } },
        new ProjectTemplate { Id = "automation", Name = "Industrial automation", Description = "PLC-centred project: rack overview, I/O sheets and field devices.", Industry = ProjectIndustry.Automation,
            BuildSheets = b => { PowerAndControl(b, "=CTRL"); b.Sheet("PLC digital I/O", SheetKind.Plc, "", "=CTRL"); b.Sheet("PLC analog I/O", SheetKind.Plc, "", "=CTRL"); } },
        new ProjectTemplate { Id = "renewable", Name = "Renewable energy", Description = "PV / storage AC side: inverter feeders and protection.", Industry = ProjectIndustry.RenewableEnergy,
            BuildSheets = b => { b.Sheet("AC collection", SheetKind.Power, "Inverter outputs and AC combiner", "=PV"); b.Sheet("Grid connection", SheetKind.Power, "", "=PV"); } },
    };

    public static ProjectTemplate Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
