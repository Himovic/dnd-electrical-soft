using DND.Electrical.Application.Services;
using System.Globalization;
using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Exports;
using DND.Electrical.Application.Projects;
using DND.Electrical.Calculations;
using DND.Electrical.Infrastructure.Persistence;
using DND.Electrical.Reports;
using DND.Electrical.Schematic.Services;
using DND.Electrical.Shared;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;

namespace DND.Electrical.Cli;

/// <summary>
/// Headless command-line interface: demo generation, validation, documentation export, calculations.
/// Runs on Windows, Linux and macOS (the desktop application is Windows-only).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { Help(); return 0; }
        if (args[0] is "--version" or "about")
        {
            Console.WriteLine(ProductInfo.FullTitle);
            Console.WriteLine(ProductInfo.AttributionBlock);
            Console.WriteLine(ProductInfo.PoweredBy);
            return 0;
        }
        var library = new SymbolLibraryService(Opt(args, "--symbols"));
        var resolver = new SymbolResolver(library);
        var schematic = new SchematicService(resolver, new SchematicBinder(resolver));
        var store = new SqliteProjectStore();
        var licensing = new DND.Electrical.Licensing.LicenseManager(
            new DND.Electrical.Licensing.LicenseStore(Opt(args, "--license-folder") ?? new DND.Electrical.Application.Abstractions.AppPaths().Licensing),
            DND.Electrical.Licensing.TrustedKeys.Product());
        var docs = new DocumentationService(schematic, licensing);
        void Need(DND.Electrical.Licensing.EngineeringModule m) => licensing.Demand(m);
        try
        {
            switch (args[0])
            {
                case "license":
                    return License(args, licensing);
                case "demo":
                    return await Demo(args, schematic, store);
                case "info":
                    {
                        var p = await store.LoadAsync(args[1]);
                        var g = schematic.Binder.BindWithConnectivity(p);
                        Console.WriteLine($"{p.Info.Number} – {p.Info.Name} (rev {p.Info.CurrentRevision}, {p.Info.Status}){(p.Info.IsDemo ? " [DEMO]" : "")}");
                        Console.WriteLine($"Sheets {p.Sheets.Count} · devices {p.Devices.Count} · wires {g.Wires.Count} · potentials {g.Potentials.Count} · cables {p.Cables.Count} · PLC channels {p.PlcStations.Sum(s => s.Channels.Count())} · calculations {p.Calculations.Count}");
                        foreach (var s in p.OrderedSheets) Console.WriteLine($"  {s.Number,3}  {s.Kind,-10} {s.Title}");
                        return 0;
                    }
                case "validate":
                    {
                        Need(DND.Electrical.Licensing.EngineeringModule.ELEC);
                        var p = await store.LoadAsync(args[1]);
                        var g = schematic.Binder.BindWithConnectivity(p);
                        var report = new ValidationEngine().Run(p, g);
                        foreach (var i in report.Issues)
                            Console.WriteLine($"{i.Severity,-11} {i.RuleId} {i.Location,-7} {i.Message}{(i.Suggestion.Length > 0 ? "  → " + i.Suggestion : "")}");
                        Console.WriteLine(report.Summary);
                        return report.HasErrors ? 1 : 0;
                    }
                case "number-wires":
                    {
                        Need(DND.Electrical.Licensing.EngineeringModule.ELEC);
                        var p = await store.LoadAsync(args[1]);
                        var g = schematic.Binder.BindWithConnectivity(p);
                        var r = WireNumberingService.Apply(p, g, new WireNumberingOptions { RenumberExisting = args.Contains("--renumber") });
                        foreach (var w in r.Warnings) Console.WriteLine("warning: " + w);
                        await store.SaveAsync(p, args[1]);
                        Console.WriteLine($"{r.Numbered} wire(s) numbered, {r.Unchanged} unchanged.");
                        return 0;
                    }
                case "export":
                    return await Export(args, store, docs);
                case "archive":
                    await ProjectArchive.ExportAsync(store, await store.LoadAsync(args[1]), args[2]);
                    Console.WriteLine($"Archive written: {args[2]}");
                    return 0;
                case "import":
                    {
                        var m = await ProjectArchive.ImportAsync(args[1], args[2]);
                        Console.WriteLine($"Imported {m.Number} – {m.Name} to {args[2]}");
                        return 0;
                    }
                case "symbols":
                    if (args.Length > 2 && args[1] == "export")
                    {
                        File.WriteAllText(args[2], BuiltInSymbolLibrary.Create().ToJson());
                        Console.WriteLine($"Built-in library written to {args[2]}");
                        return 0;
                    }
                    foreach (var s in library.Search(Opt(args, "--search")))
                        Console.WriteLine($"{s.Id,-22} {s.Category,-15} {s.Role,-15} {s.Name}");
                    return 0;
                case "power":
                    return await Power(args, store, new PowerStudioService(licensing));
                case "model3d":
                    return await Model3D(args, store, new ThreeDService(licensing));
                case "calc":
                    Need(args.ElementAtOrDefault(1) is "short-circuit" or "transformer" or "motor" ? DND.Electrical.Licensing.EngineeringModule.POWER : DND.Electrical.Licensing.EngineeringModule.ELEC);
                    return Calc(args);
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    Help();
                    return 2;
            }
        }
        catch (DND.Electrical.Licensing.LicenseException ex)
        {
            Console.Error.WriteLine($"licence: {ex.Message}\nRun 'dnd-electrical license status' or 'dnd-electrical license evaluation'.");
            return 4;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 3;
        }
    }

    private static int License(string[] args, DND.Electrical.Licensing.LicenseManager m)
    {
        switch (args.ElementAtOrDefault(1) ?? "status")
        {
            case "install":
                m.Install(DND.Electrical.Licensing.SignedLicense.FromJson(File.ReadAllText(args[2])));
                break;
            case "evaluation":
                m.Install(DND.Electrical.Licensing.SignedLicense.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Licensing", "evaluation.dndlic"))));
                break;
            case "request":
                var path = args.ElementAtOrDefault(3) ?? "activation-request.json";
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(m.CreateActivationRequest(args[2], Environment.UserName), DND.Electrical.Licensing.LicenseJson.Indented));
                Console.WriteLine($"Activation request written to {path}");
                return 0;
            case "remove":
                m.Uninstall();
                break;
        }
        var e = m.Current;
        Console.WriteLine($"State   : {e.State}\nMessage : {e.Message}\nDevice  : {m.DeviceId}");
        if (e.Claims is { } c) Console.WriteLine($"Licence : {c.LicenseId} ({c.Subscription}, {c.OrganizationName})\nModules : {string.Join(", ", m.LicensedModules.Select(DND.Electrical.Licensing.ModuleCatalog.Code))}");
        return e.AllowsFeatures ? 0 : 4;
    }

    private static async Task<int> Demo(string[] args, SchematicService schematic, SqliteProjectStore store)
    {
        var factory = new DemoProjectFactory(schematic);
        if (args.Length < 2 || args[1] == "list")
        {
            foreach (var d in DemoProjectFactory.Catalogue) Console.WriteLine($"{d.Number}  {d.Name} – {d.Description}");
            return 0;
        }
        var outDir = Opt(args, "--out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        var numbers = args[1] == "all" ? DemoProjectFactory.Catalogue.Select(c => c.Number) : new[] { args[1] };
        foreach (var n in numbers)
        {
            var p = factory.Create(n);
            var path = Path.Combine(outDir, n + ProductInfo.ProjectFileExtension);
            await store.SaveAsync(p, path);
            Console.WriteLine($"{n} → {path} ({p.Sheets.Count} sheets, {p.Devices.Count} devices)");
        }
        return 0;
    }

    private static async Task<int> Export(string[] args, SqliteProjectStore store, DocumentationService docs)
    {
        var p = await store.LoadAsync(args[1]);
        var format = Opt(args, "--format") ?? "pdf";
        // json/xml interchange is part of project data portability (always available); other formats need DOCS.
        var output = Opt(args, "--out") ?? Path.ChangeExtension(args[1], format);
        var options = new ReportOptions { Language = Opt(args, "--lang") ?? "en" };
        var reports = Opt(args, "--reports")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var sheetArg = Opt(args, "--sheet");
        var sheet = sheetArg is null ? p.OrderedSheets.First() : p.Sheets.First(s => s.Number == int.Parse(sheetArg, CultureInfo.InvariantCulture));
        IReadOnlyList<ExportResult> results = format switch
        {
            "pdf" or "drawings" => new[] { docs.DrawingSetPdf(p, output, options) },
            "reports" => new[] { docs.ReportsPdf(p, output, reports, options) },
            "xlsx" => new[] { docs.ReportsXlsx(p, output, reports, options) },
            "csv" => new[] { docs.ReportCsv(p, reports?.FirstOrDefault() ?? "bom", output, options) },
            "dxf" => new[] { docs.SheetDxf(p, sheet, output) },
            "svg" => new[] { docs.SheetSvg(p, sheet, output) },
            "png" => new[] { docs.SheetPng(p, sheet, output) },
            "json" => Write(output, ProjectInterchange.ToJson(p), "json"),
            "xml" => Write(output, ProjectInterchange.ToEngineeringXml(p).ToString(), "xml"),
            "handover" => docs.HandoverPackage(p, output, options),
            _ => throw new ArgumentException($"Unknown format '{format}'."),
        };
        foreach (var r in results) Console.WriteLine($"{r.Kind,-5} {r.Size,10:N0} B  {r.Path}");
        return 0;
    }

    /// <summary>
    /// power &lt;network.json | project.dndx | sample:ieee14 | sample:demo&gt; [--study lf|sc|prot|harm|all] [--out report.txt] [--tcc chart.svg] [--save-network file.json]
    /// </summary>
    private static async Task<int> Power(string[] args, SqliteProjectStore store, PowerStudioService svc)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: power <network.json|project.dndx|sample:ieee14|sample:demo> [--study lf|sc|prot|harm|all]");
        var src = args[1];
        DND.Electrical.Power.PowerNetwork n = src switch
        {
            "sample:ieee14" => DND.Electrical.Power.SampleNetworks.Ieee14(),
            "sample:demo" => DND.Electrical.Power.SampleNetworks.DemoIndustrial(),
            _ when src.EndsWith(ProductInfo.ProjectFileExtension, StringComparison.OrdinalIgnoreCase) =>
                (await store.LoadAsync(src)).PowerNetworks.FirstOrDefault() is { } doc ? PowerStudioService.Load(doc) : throw new ArgumentException("The project contains no PowerStudio network."),
            _ => DND.Electrical.Power.PowerNetwork.FromJson(File.ReadAllText(src)),
        };
        if (Opt(args, "--save-network") is { } save) { File.WriteAllText(save, n.ToJson()); Console.WriteLine($"Network written: {save}"); }
        var study = Opt(args, "--study") ?? "all";
        var report = new System.Text.StringBuilder();
        var exit = 0;
        DND.Electrical.Power.ShortCircuitResult? sc = null;
        if (study is "lf" or "all")
        {
            var lf = svc.LoadFlow(n);
            report.AppendLine(PowerStudioService.LoadFlowReport(n, lf));
            if (!lf.Converged) exit = 1;
        }
        if (study is "sc" or "prot" or "all")
        {
            sc = svc.ShortCircuit(n);
            if (study != "prot") report.AppendLine(PowerStudioService.ShortCircuitReport(n, sc));
        }
        if (study is "prot" or "all" && n.Protection.Count > 0)
        {
            report.AppendLine("D&D PowerStudio – Protection coordination");
            foreach (var f in svc.Coordination(n, sc!)) report.AppendLine((f.Selective ? "  ✔ " : "  ✖ ") + f.Message);
            report.AppendLine();
            if (Opt(args, "--tcc") is { } tcc) { File.WriteAllText(tcc, DND.Electrical.Power.ProtectionStudy.TccSvg(n, sc)); Console.WriteLine($"Time-current chart written: {tcc}"); }
        }
        if (study is "harm" or "all")
        {
            var h = svc.Harmonics(n);
            report.AppendLine("D&D PowerStudio – Harmonic frequency scan");
            foreach (var r in h.Resonances) report.AppendLine(FormattableString.Invariant($"  Resonance at bus {r.Bus}: h = {r.Order:0.0}, |Z| = {r.ZOhm:0.####} Ω"));
            foreach (var d in h.Distortion) report.AppendLine(FormattableString.Invariant($"  Bus {d.Bus}: THDv = {d.ThdPercent:0.##} %"));
            report.AppendLine();
        }
        if (Opt(args, "--out") is { } output) { File.WriteAllText(output, report.ToString()); Console.WriteLine($"Report written: {output}"); }
        else Console.Write(report.ToString());
        return exit;
    }

    /// <summary>model3d &lt;project&gt; [--cabinet TAG] [--out file.glb|.gltf|.stl] [--save]: generates the 3D cabinet model, checks clashes and exports.</summary>
    private static async Task<int> Model3D(string[] args, SqliteProjectStore store, ThreeDService svc)
    {
        var p = await store.LoadAsync(args[1]);
        var tag = Opt(args, "--cabinet");
        var cab = tag is null ? p.Cabinets.FirstOrDefault() : p.Cabinets.FirstOrDefault(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (cab is null) throw new ArgumentException(tag is null ? "The project has no cabinet layout." : $"Cabinet '{tag}' not found.");
        var scene = svc.GenerateCabinet(p, cab);
        Console.WriteLine($"{cab.Tag}: {scene.Objects.Count} 3D objects generated from the 2D layout ({cab.Width}×{cab.Height}×{cab.Depth} mm).");
        var clashes = svc.Clashes(p, scene);
        foreach (var c in clashes) Console.WriteLine($"{c.Kind,-9} {c.Description}");
        Console.WriteLine($"{clashes.Count(c => c.Kind == DND.Electrical.ThreeD.ClashKind.Hard)} hard clash(es), {clashes.Count(c => c.Kind == DND.Electrical.ThreeD.ClashKind.Clearance)} clearance violation(s).");
        if (Opt(args, "--out") is { } output)
        {
            svc.Export(scene, output);
            Console.WriteLine($"3D model written: {output} ({new FileInfo(output).Length:N0} B)");
        }
        if (args.Contains("--save")) { await store.SaveAsync(p, args[1]); Console.WriteLine("3D layout stored in the project."); }
        return clashes.Any(c => c.Kind == DND.Electrical.ThreeD.ClashKind.Hard) ? 1 : 0;
    }

    private static IReadOnlyList<ExportResult> Write(string path, string content, string kind)
    {
        File.WriteAllText(path, content);
        return new[] { new ExportResult(path, kind, new FileInfo(path).Length) };
    }

    private static int Calc(string[] args)
    {
        double D(string name, double def) => Opt(args, name) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : def;
        var system = D("--phases", 3) >= 3 ? SupplySystem.ThreePhase : SupplySystem.SinglePhase;
        CalculationResult r = args.ElementAtOrDefault(1) switch
        {
            "current" => PowerCalculator.CurrentFromPower(system, D("--power", 11), D("--voltage", 400), D("--pf", 0.85), D("--eff", 1)),
            "power" => PowerCalculator.PowerFromCurrent(system, D("--voltage", 400), D("--current", 10), D("--pf", 0.85)),
            "voltage-drop" => VoltageDropCalculator.Calculate(new VoltageDropInput(system, D("--voltage", 400), D("--current", 16), D("--length", 30), D("--section", 2.5), PowerFactor: D("--pf", 0.85), LimitPercent: D("--limit", 5))),
            "cable" => CableSizingCalculator.Calculate(new CableSizingInput(system, D("--voltage", 400), D("--current", 16), D("--length", 30),
                Enum.Parse<InstallationMethod>(Opt(args, "--method") ?? "C", true), Enum.Parse<CableInsulation>(Opt(args, "--insulation") ?? "PVC", true),
                D("--ambient", 30), (int)D("--grouped", 1), D("--pf", 0.85), D("--limit", 5))),
            "short-circuit" => ShortCircuitCalculator.Calculate(new ShortCircuitInput(D("--ssc", 500), D("--kva", 630), D("--uk", 6), D("--pk", 6.5), D("--voltage", 400), D("--length", 0), D("--section", 240))),
            "transformer" => TransformerSizingCalculator.Calculate(D("--load", 400), D("--ks", 0.8), D("--pf", 0.9), D("--reserve", 20)),
            "motor" => MotorStartingCalculator.Calculate(D("--power", 11), D("--voltage", 400), D("--pf", 0.85), D("--eff", 0.9), D("--ratio", 7), Enum.Parse<StartingMethod>(Opt(args, "--start") ?? "DirectOnLine", true)),
            _ => throw new ArgumentException("calc: current | power | voltage-drop | cable | short-circuit | transformer | motor"),
        };
        Console.WriteLine(r.ToReportText());
        return r.Status == ResultStatus.Fail ? 1 : 0;
    }

    private static string? Opt(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Help()
    {
        Console.WriteLine($"""
            {ProductInfo.FullTitle} – command line
            {ProductInfo.PoweredBy}

            Usage:
              dnd-electrical demo list | <NUMBER> | all [--out <folder>]
              dnd-electrical info <project.dndx>
              dnd-electrical validate <project.dndx>                  (exit code 1 when errors)
              dnd-electrical number-wires <project.dndx> [--renumber]
              dnd-electrical export <project.dndx> --format pdf|reports|xlsx|csv|dxf|svg|png|json|xml|handover
                                   [--out <path>] [--sheet <n>] [--reports bom,wires,...] [--lang en|fr]
              dnd-electrical archive <project.dndx> <package.dndpkg>
              dnd-electrical import <package.dndpkg> <project.dndx>
              dnd-electrical power <network.json|project.dndx|sample:ieee14|sample:demo> [--study lf|sc|prot|harm|all] [--out <report.txt>] [--tcc <chart.svg>]
              dnd-electrical model3d <project.dndx> [--cabinet <tag>] [--out <file.glb|.gltf|.stl>] [--save]
              dnd-electrical symbols [--search <text>] | symbols export <file.json>
              dnd-electrical calc current|power|voltage-drop|cable|short-circuit|transformer|motor [options]
              dnd-electrical license status|evaluation|install <file.dndlic>|request <licenceId> [file]|remove
              dnd-electrical about
            """);
    }
}
