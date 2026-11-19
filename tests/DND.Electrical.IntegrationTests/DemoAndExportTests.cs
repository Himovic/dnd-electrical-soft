using DND.Electrical.Application.Demo;
using DND.Electrical.Reports;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Validation;

namespace DND.Electrical.IntegrationTests;

public class DemoAndExportTests
{
    public static IEnumerable<object[]> DemoNumbers => DemoProjectFactory.Catalogue.Select(c => new object[] { c.Number });

    [Theory]
    [MemberData(nameof(DemoNumbers))]
    public void Demo_projects_have_no_validation_errors(string number)
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create(number);
        var graph = env.Schematic.Binder.BindWithConnectivity(p);
        var report = new ValidationEngine().Run(p, graph);
        var errors = report.Issues.Where(i => i.Severity == Severity.Error).Select(i => $"{i.RuleId}: {i.Message} {i.Location}").ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.True(p.Info.IsDemo);
        Assert.All(p.Catalog, c => Assert.True(c.IsDemoData));
        Assert.True(p.Sheets.Count >= 3);
        Assert.True(graph.Wires.Count > 10);
    }

    [Fact]
    public void Demo_contents_match_specification()
    {
        using var env = new TestEnvironment();
        var ind = env.Demos.Create("DEMO-IND-001");
        foreach (var tag in new[] { "-Q0", "-Q1", "-K1", "-F1", "-M1", "-S0", "-S1", "-S2", "-H1", "-H2", "-X1", "-G1" })
            Assert.NotNull(ind.FindDeviceByTag(tag));
        Assert.Single(ind.Motors);
        Assert.NotEmpty(BomService.Build(ind));

        var aut = env.Demos.Create("DEMO-AUT-002");
        var io = DND.Electrical.PLC.PlcService.IoList(aut);
        Assert.Contains(io, r => r.Kind == "DI" && r.Tag == "Part_Present" && r.SchematicLocation.Length > 0);
        Assert.Contains(io, r => r.Kind == "AI" && r.Tag == "PT201_Pressure");
        Assert.Contains(io, r => r.Kind == "AO" && r.Tag == "Pump_Speed_SP");
        var g = ConnectivityGraph.Build(aut);
        Assert.Contains(g.Potentials, pot => pot.InterruptionNames.Contains("SPEED_REF") && pot.Pins.Count >= 3);

        var pwr = env.Demos.Create("DEMO-PWR-003");
        Assert.Equal(6, pwr.Loads.Count);
        Assert.Contains(pwr.Calculations, c => c.CalculationType == "short-circuit");
        Assert.Contains(pwr.Calculations, c => c.CalculationType == "transformer-sizing");
        Assert.Equal(6, pwr.Calculations.Count(c => c.CalculationType == "cable-sizing"));

        var cab = env.Demos.Create("DEMO-CAB-004");
        Assert.Empty(DND.Electrical.Panel.CabinetLayoutService.Check(cab.Cabinets[0]));
        Assert.True(cab.Cabinets[0].Items.Count(i => i.Kind == DND.Electrical.Domain.CabinetItemKind.DinRail) == 4);

        var bld = env.Demos.Create("DEMO-BLD-005");
        Assert.Equal(8, bld.Loads.Count);
        Assert.Contains(bld.Calculations, c => c.CalculationType == "load-balancing");
        Assert.Equal(8, bld.Cables.Count);
    }

    [Fact]
    public void Handover_package_contains_all_formats()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var results = env.Docs.HandoverPackage(p, env.PathFor("handover"));
        Assert.Contains(results, r => r.Kind == "pdf" && r.Path.EndsWith("_Drawings.pdf"));
        Assert.Contains(results, r => r.Kind == "xlsx");
        Assert.Contains(results, r => r.Kind == "zip");
        Assert.Equal(p.Sheets.Count, results.Count(r => r.Kind == "dxf"));
        foreach (var r in results) Assert.True(r.Size > 0, r.Path);

        var pdf = File.ReadAllBytes(results.First(r => r.Path.EndsWith("_Drawings.pdf")).Path);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var xlsx = File.ReadAllBytes(results.First(r => r.Kind == "xlsx").Path);
        Assert.Equal((byte)'P', xlsx[0]);
        Assert.Equal((byte)'K', xlsx[1]);
        var dxf = File.ReadAllText(results.First(r => r.Kind == "dxf").Path);
        Assert.Contains("AC1009", dxf);
        Assert.EndsWith("EOF\n", dxf);
        var bomCsv = CsvWriter.Parse(File.ReadAllText(results.First(r => r.Path.EndsWith("_bom.csv")).Path));
        Assert.True(bomCsv.Count > 5);
    }

    [Fact]
    public void Reports_are_generated_in_french()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-BLD-005");
        var path = env.PathFor("rapport.pdf");
        env.Docs.ReportsPdf(p, path, new[] { "bom", "loads" }, new ReportOptions { Language = "fr" });
        Assert.True(new FileInfo(path).Length > 2000);
        var g = env.Schematic.Binder.BindWithConnectivity(p);
        Assert.Equal("Nomenclature (liste de matériel)", new ProjectReports(p, g, new ReportOptions { Language = "fr" }).Build("bom").Title);
    }

    [Fact]
    public void Svg_and_png_exports()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var sheet = p.Sheets[1];
        var svg = env.Docs.SheetSvg(p, sheet, env.PathFor("s.svg"));
        Assert.Contains("<svg", File.ReadAllText(svg.Path));
        var png = env.Docs.SheetPng(p, sheet, env.PathFor("s.png"), 3);
        var bytes = File.ReadAllBytes(png.Path);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }
}
