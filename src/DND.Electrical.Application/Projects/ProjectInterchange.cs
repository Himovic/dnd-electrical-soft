using System.Text.Json;
using System.Xml.Linq;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.Domain;
using DND.Electrical.PLC;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Shared;
using DND.Electrical.Wiring;

namespace DND.Electrical.Application.Projects;

/// <summary>Complete JSON interchange and an engineering XML export (devices, connections, cables, PLC I/O).</summary>
public static class ProjectInterchange
{
    public static string ToJson(ElectricalProject project) => JsonSerializer.Serialize(project, CadJson.IndentedOptions);

    public static ElectricalProject FromJson(string json)
    {
        SchematicEntityTypes.Register();
        var p = JsonSerializer.Deserialize<ElectricalProject>(json, CadJson.Options) ?? throw new InvalidDataException("Empty project JSON.");
        if (p.FormatVersion > ProductInfo.ProjectFormatVersion) throw new InvalidDataException("The JSON was produced by a newer version.");
        return p;
    }

    public static XDocument ToEngineeringXml(ElectricalProject project)
    {
        var graph = ConnectivityGraph.Build(project);
        var root = new XElement("ElectricalProject",
            new XAttribute("product", ProductInfo.ProductName),
            new XAttribute("version", ProductInfo.VersionString),
            new XElement("Info",
                new XElement("Number", project.Info.Number), new XElement("Name", project.Info.Name),
                new XElement("Customer", project.Info.CustomerName), new XElement("Revision", project.Info.CurrentRevision)),
            new XElement("Devices", project.Devices.Select(d => new XElement("Device",
                new XAttribute("tag", d.Tag), new XAttribute("function", d.Function), new XAttribute("manufacturer", d.Manufacturer),
                new XAttribute("partNumber", d.PartNumber), new XAttribute("location", d.Location),
                d.Properties.Select(kv => new XElement("Property", new XAttribute("name", kv.Key), kv.Value))))),
            new XElement("Connections", WiringReports.ConnectionList(project, graph).Select(c => new XElement("Connection",
                new XAttribute("wire", c.WireNumber), new XAttribute("from", c.From), new XAttribute("to", c.To),
                new XAttribute("potential", c.Potential), new XAttribute("cable", c.Cable)))),
            new XElement("Cables", project.Cables.Select(c => new XElement("Cable",
                new XAttribute("tag", c.Tag), new XAttribute("type", c.CableType), new XAttribute("cores", c.CoreCount),
                new XAttribute("crossSection", c.CrossSectionMm2), new XAttribute("lengthM", c.LengthM),
                c.Cores.Select(k => new XElement("Core", new XAttribute("index", k.Index), new XAttribute("marking", k.Marking)))))),
            new XElement("PlcIo", PlcService.IoList(project).Select(r => new XElement("Channel",
                new XAttribute("station", r.Station), new XAttribute("address", r.Address), new XAttribute("type", r.Kind),
                new XAttribute("tag", r.Tag), new XAttribute("description", r.Description), new XAttribute("device", r.Device)))));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }
}
