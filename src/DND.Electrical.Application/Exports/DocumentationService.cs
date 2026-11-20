using System.IO.Compression;
using DND.Electrical.CAD.Dxf;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Domain;
using DND.Electrical.Panel;
using DND.Electrical.Rendering;
using DND.Electrical.Reports;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using DND.Electrical.Application.Services;
using DND.Electrical.Application.Projects;

namespace DND.Electrical.Application.Exports;

public sealed record ExportResult(string Path, string Kind, long Size);

/// <summary>
/// Generates engineering documentation from live project data: drawing sets, reports (PDF/XLSX/CSV),
/// drawing exchange files (DXF/SVG/PNG) and complete handover packages.
/// </summary>
public sealed class DocumentationService(SchematicService schematic, DND.Electrical.Licensing.IEntitlements entitlements)
{
    private void Demand() => entitlements.Demand(DND.Electrical.Licensing.EngineeringModule.DOCS);

    private ConnectivityGraph Prepare(ElectricalProject project) => schematic.Binder.BindWithConnectivity(project);

    private static IImageProvider Images(ElectricalProject project, ReportOptions? options)
    {
        var provider = new ImageProvider();
        var logo = options?.CompanyLogoPng ?? (project.Info.CompanyLogoAttachmentId is { } id ? project.Attachments.FirstOrDefault(a => a.Id == id)?.Content : null);
        if (logo is { Length: > 0 }) provider.Register(ImageProvider.CompanyLogoKey, () => logo);
        return provider;
    }

    public ExportResult DrawingSetPdf(ElectricalProject project, string path, ReportOptions? options = null, IEnumerable<Sheet>? sheets = null)
    {
        Demand();
        options ??= new ReportOptions();
        Prepare(project);
        var composer = new PdfDocumentComposer(project, options, Images(project, options));
        if (options.IncludeCover) composer.AddCover(L.T("drawingset", options.Language));
        composer.AddSheets(sheets ?? project.OrderedSheets);
        composer.Save(path);
        return Result(path, "pdf");
    }

    /// <summary>One PDF containing the selected reports (all when <paramref name="reportIds"/> is empty).</summary>
    public ExportResult ReportsPdf(ElectricalProject project, string path, IEnumerable<string>? reportIds = null, ReportOptions? options = null)
    {
        Demand();
        options ??= new ReportOptions();
        var graph = Prepare(project);
        var reports = new ProjectReports(project, graph, options);
        var composer = new PdfDocumentComposer(project, options, Images(project, options));
        var ids = (reportIds ?? Array.Empty<string>()).ToList();
        if (ids.Count == 0) ids = ProjectReports.Catalogue(options.Language).Select(c => c.id).ToList();
        if (options.IncludeCover) composer.AddCover(ids.Count == 1 ? L.T(ids[0], options.Language) : L.T("handover", options.Language));
        foreach (var id in ids)
        {
            composer.AddTable(reports.Build(id));
            if (id == "cabinet")
                foreach (var cab in project.Cabinets)
                    composer.AddDrawing(CabinetLayoutService.BuildDrawing(project, cab), $"{L.T("cabinet", options.Language)} {cab.Tag}");
            if (id == "calculations")
                foreach (var c in project.Calculations)
                    composer.AddText(c.Title, CalculationService.ReportText(c));
        }
        composer.Save(path);
        return Result(path, "pdf");
    }

    public ExportResult ReportsXlsx(ElectricalProject project, string path, IEnumerable<string>? reportIds = null, ReportOptions? options = null)
    {
        Demand();
        options ??= new ReportOptions();
        var graph = Prepare(project);
        var reports = new ProjectReports(project, graph, options);
        var ids = (reportIds ?? Array.Empty<string>()).ToList();
        if (ids.Count == 0) ids = ProjectReports.Catalogue(options.Language).Select(c => c.id).ToList();
        XlsxReportWriter.Write(ids.Select(reports.Build).ToList(), path, project, options);
        return Result(path, "xlsx");
    }

    public ExportResult ReportCsv(ElectricalProject project, string reportId, string path, ReportOptions? options = null)
    {
        Demand();
        var graph = Prepare(project);
        CsvWriter.Write(new ProjectReports(project, graph, options).Build(reportId), path);
        return Result(path, "csv");
    }

    public ExportResult SheetDxf(ElectricalProject project, Sheet sheet, string path)
    {
        Demand();
        Prepare(project);
        File.WriteAllText(path, DxfWriter.Write(sheet.Drawing, FieldResolver.CreateResolver(project, sheet)));
        return Result(path, "dxf");
    }

    public ExportResult SheetSvg(ElectricalProject project, Sheet sheet, string path)
    {
        Demand();
        Prepare(project);
        using (var fs = File.Create(path))
            DrawingExporter.ToSvg(sheet.Drawing, new Rect2(0, 0, sheet.Width, sheet.Height), fs, FieldResolver.CreateResolver(project, sheet), Images(project, null));
        return Result(path, "svg");
    }

    public ExportResult SheetPng(ElectricalProject project, Sheet sheet, string path, double pixelsPerMm = 6)
    {
        Demand();
        Prepare(project);
        File.WriteAllBytes(path, DrawingExporter.ToPng(sheet.Drawing, new Rect2(0, 0, sheet.Width, sheet.Height), pixelsPerMm, FieldResolver.CreateResolver(project, sheet), Images(project, null)));
        return Result(path, "png");
    }

    /// <summary>
    /// Complete handover documentation: drawing set PDF, report book PDF, XLSX workbook, CSV lists,
    /// DXF per sheet, engineering XML and a project JSON – plus a ZIP of everything.
    /// </summary>
    public IReadOnlyList<ExportResult> HandoverPackage(ElectricalProject project, string folder, ReportOptions? options = null, bool zip = true)
    {
        Demand();
        options ??= new ReportOptions();
        Directory.CreateDirectory(folder);
        var baseName = Safe($"{project.Info.Number}_{project.Info.CurrentRevision}");
        var results = new List<ExportResult>
        {
            DrawingSetPdf(project, Path.Combine(folder, $"{baseName}_Drawings.pdf"), options),
            ReportsPdf(project, Path.Combine(folder, $"{baseName}_Reports.pdf"), null, options),
            ReportsXlsx(project, Path.Combine(folder, $"{baseName}_Lists.xlsx"), null, options),
        };
        var csvDir = Path.Combine(folder, "CSV");
        Directory.CreateDirectory(csvDir);
        foreach (var id in new[] { "bom", "wires", "cables", "cablecores", "terminals", "plcio", "equipment", "loads" })
            results.Add(ReportCsv(project, id, Path.Combine(csvDir, $"{baseName}_{id}.csv"), options));
        var dxfDir = Path.Combine(folder, "DXF");
        Directory.CreateDirectory(dxfDir);
        foreach (var s in project.OrderedSheets)
            results.Add(SheetDxf(project, s, Path.Combine(dxfDir, $"{baseName}_{s.Number:00}_{Safe(s.Title)}.dxf")));
        var xmlPath = Path.Combine(folder, $"{baseName}_Engineering.xml");
        ProjectInterchange.ToEngineeringXml(project).Save(xmlPath);
        results.Add(Result(xmlPath, "xml"));
        var jsonPath = Path.Combine(folder, $"{baseName}_Project.json");
        File.WriteAllText(jsonPath, ProjectInterchange.ToJson(project));
        results.Add(Result(jsonPath, "json"));
        var readme = Path.Combine(folder, "README.txt");
        File.WriteAllText(readme, $"{project.Info.Number} – {project.Info.Name}\nRevision {project.Info.CurrentRevision}\nGenerated {DateTime.Now:yyyy-MM-dd HH:mm}\n\n{ProductInfo.AttributionBlock}\n{ProductInfo.PoweredBy}\n" +
            (project.Info.IsDemo ? "\nDEMONSTRATION PROJECT – FICTIONAL DATA – NOT FOR CONSTRUCTION\n" : string.Empty));
        results.Add(Result(readme, "txt"));
        if (zip)
        {
            var zipPath = folder.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            results.Add(Result(zipPath, "zip"));
        }
        return results;
    }

    private static ExportResult Result(string path, string kind) => new(path, kind, new FileInfo(path).Length);

    private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
