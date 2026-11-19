using DND.Electrical.Application.Services;
using DND.Electrical.Documentation;
using DND.Electrical.Domain;
using DND.Electrical.Plugins;

namespace DND.Electrical.IntegrationTests;

public class ServicesTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DND-Electrical-Engineering-Suite.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Shipped_user_manual_loads_and_is_searchable()
    {
        var lib = new HelpLibrary(Path.Combine(RepoRoot(), "documentation", "UserManual"));
        Assert.True(lib.Articles.Count >= 24);
        Assert.Contains("Getting started", lib.Categories);
        var hits = lib.Search("voltage drop");
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Article.Id == "25-calculation-methodology");
        foreach (var t in TutorialEngine.BuiltIn.SelectMany(t => t.Steps).Where(s => s.HelpArticle is not null))
            Assert.NotNull(lib.Get(t.HelpArticle!));
    }

    [Fact]
    public void Documentation_administration_overrides_and_publishes()
    {
        using var env = new TestEnvironment();
        var lib = new HelpLibrary(Path.Combine(RepoRoot(), "documentation", "UserManual"), env.Paths.Documentation);
        var a = lib.Get("26-faq")!;
        a.Body += "\n\n**Q: Company rule?** Always validate before issue.";
        lib.Save(a);
        Assert.True(lib.Get("26-faq")!.IsCustom);
        Assert.NotEmpty(lib.Search("Always validate"));
        lib.Save(new HelpArticle { Id = "company-standards", Title = "Company standards", Category = "Company", Body = "# Company standards\nUse -K for relays." });
        Assert.Contains("Company", lib.Categories);
        var img = Path.Combine(env.Folder, "pic.png");
        File.WriteAllBytes(img, new byte[] { 0x89, 0x50 });
        var reference = lib.AddAsset(img);
        Assert.NotNull(lib.ResolveAsset(reference));
        Assert.Null(lib.ResolveAsset("../../etc/passwd"));
        Assert.Throws<ArgumentException>(() => lib.AddAsset(Path.Combine(env.Folder, "x.exe")));
        lib.Publish("admin");
        Assert.Single(lib.PublishedVersions());
        lib.DeleteCustom("26-faq");
        Assert.False(lib.Get("26-faq")!.IsCustom);
    }

    [Fact]
    public void Markdown_parser_handles_supported_blocks()
    {
        var blocks = Markdown.Parse("# Title\n\nSome **bold** and `code` with [link](x).\n\n- a\n- b\n\n1. one\n2. two\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```\ncode\n```\n> note\n");
        Assert.IsType<HeadingBlock>(blocks[0]);
        var para = Assert.IsType<ParagraphBlock>(blocks[1]);
        Assert.Contains(para.Content, i => i.Kind == InlineKind.Bold && i.Text == "bold");
        Assert.Contains(para.Content, i => i.Kind == InlineKind.Link && i.Url == "x");
        Assert.Contains(blocks, b => b is ListBlock { Ordered: false, Items.Count: 2 });
        Assert.Contains(blocks, b => b is ListBlock { Ordered: true });
        Assert.Contains(blocks, b => b is TableBlock { Rows.Count: 1 });
        Assert.Contains(blocks, b => b is CodeBlock);
        Assert.Contains(blocks, b => b is QuoteBlock);
    }

    [Fact]
    public void Tutorial_advances_on_application_events()
    {
        var engine = new TutorialEngine();
        var t = engine.OfferFor(AppEvents.SchematicEditorOpened, new HashSet<string>());
        Assert.NotNull(t);
        engine.Start(t!);
        Assert.False(engine.OnEvent(AppEvents.WireDrawn)); // not the current step
        Assert.True(engine.OnEvent(AppEvents.SheetCreated));
        Assert.True(engine.OnEvent(AppEvents.SymbolInserted_("Protection")));
        Assert.False(engine.OnEvent(AppEvents.SymbolInserted_("Motors")));
        Assert.True(engine.OnEvent(AppEvents.SymbolInserted_("Switching")));
        TutorialDefinition? done = null;
        engine.Completed += (_, d) => done = d;
        foreach (var e in new[] { AppEvents.WireDrawn, AppEvents.WiresNumbered, AppEvents.ProjectValidated, AppEvents.PdfExported }) engine.OnEvent(e);
        Assert.Equal("first-schematic", done?.Id);
        Assert.Null(engine.OfferFor(AppEvents.SchematicEditorOpened, new HashSet<string> { "first-schematic" }));
    }

    [Fact]
    public void Plugin_loading_requires_matching_hash_and_trust()
    {
        using var env = new TestEnvironment();
        var sampleDll = Path.Combine(RepoRoot(), "samples", "DND.Electrical.Plugins.Sample", "bin", "Debug", "net10.0", "DND.Electrical.Plugins.Sample.dll");
        Assert.True(File.Exists(sampleDll), sampleDll);
        var dir = Path.Combine(env.Paths.Plugins, "load-summary");
        Directory.CreateDirectory(dir);
        File.Copy(sampleDll, Path.Combine(dir, "Sample.dll"));
        var hash = PluginManager.ComputeSha256(Path.Combine(dir, "Sample.dll"));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), $$"""
            { "id": "dnd.sample.loadsummary", "name": "Load summary", "version": "1.0.0", "description": "Sample", "author": "D&D",
              "minHostVersion": "0.1.0", "maxHostVersion": null, "entryAssembly": "Sample.dll",
              "entryType": "DND.Electrical.Plugins.Sample.LoadSummaryPlugin", "permissions": "ProjectRead, UiCommands", "sha256": "{{hash}}" }
            """);
        var project = env.Demos.Create("DEMO-CAB-004");
        var logs = new List<string>();
        var mgr = new PluginManager(env.Paths.Plugins, () => project, logs.Add);

        Assert.Equal(PluginState.Untrusted, mgr.DiscoverAndLoad(new Version(0, 4, 0)).Single().State);
        mgr.TrustedHashes.Add(hash);
        Assert.Equal(PluginState.Incompatible, mgr.DiscoverAndLoad(new Version(0, 0, 5)).Single().State);
        var info = mgr.DiscoverAndLoad(new Version(0, 4, 0)).Single();
        Assert.Equal(PluginState.Loaded, info.State);
        var cmd = Assert.Single(mgr.Commands);
        Assert.Equal("dnd.sample.loadsummary.motor-summary", cmd.Id);
        cmd.Execute();
        Assert.Contains(logs, l => l.Contains("3 motors"));
        mgr.UnloadAll();

        File.AppendAllText(Path.Combine(dir, "Sample.dll"), "tampered");
        Assert.Equal(PluginState.Invalid, mgr.DiscoverAndLoad(new Version(0, 4, 0)).Single().State);
    }

    [Fact]
    public void Catalogue_import_from_csv_and_xlsx()
    {
        using var env = new TestEnvironment();
        var p = new ElectricalProject();
        var csv = env.PathFor("cat.csv");
        File.WriteAllText(csv, "Manufacturer;PartNumber;Description;UnitPrice;Width;Height\nACME;A-100;Breaker 16A;12.50;18;90\nACME;;bad;1;;\nACME;A-200;Contactor;abc;45;80\n");
        var r = CatalogImportService.Import(p, csv);
        Assert.Equal(1, r.Added);
        Assert.Equal(2, r.Errors.Count);
        Assert.Equal(12.50m, p.Catalog[0].UnitPrice);
        Assert.False(p.Catalog[0].IsDemoData);

        var xlsx = env.PathFor("cat.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Products");
            ws.Cell(1, 1).Value = "Manufacturer"; ws.Cell(1, 2).Value = "Part Number"; ws.Cell(1, 3).Value = "Description"; ws.Cell(1, 4).Value = "UnitPrice";
            ws.Cell(2, 1).Value = "ACME"; ws.Cell(2, 2).Value = "A-100"; ws.Cell(2, 3).Value = "Breaker 16A updated"; ws.Cell(2, 4).Value = 13;
            ws.Cell(3, 1).Value = "ACME"; ws.Cell(3, 2).Value = "A-300"; ws.Cell(3, 3).Value = "Lamp"; ws.Cell(3, 4).Value = 4.2;
            wb.SaveAs(xlsx);
        }
        var r2 = CatalogImportService.Import(p, xlsx);
        Assert.Equal(1, r2.Added);
        Assert.Equal(1, r2.Updated);
        Assert.Equal("Breaker 16A updated", p.FindProduct("ACME", "A-100")!.Description);
    }

    [Fact]
    public async Task Local_assistant_answers_from_project_and_libraries()
    {
        using var env = new TestEnvironment();
        var help = new HelpLibrary(Path.Combine(RepoRoot(), "documentation", "UserManual"));
        var assistant = new LocalAssistantProvider(TestEnvironment.Library, help, p => env.Schematic.Binder.BindWithConnectivity(p));
        var p = env.Demos.Create("DEMO-IND-001");
        var sym = await assistant.AskAsync("Explain the emergency stop symbol", p);
        Assert.Contains("IEC.ESTOP", sym.Text);
        var summary = await assistant.AskAsync("Give me a project summary", p);
        Assert.Contains("DEMO-IND-001", summary.Text);
        foreach (var w in p.Sheets.SelectMany(s => s.Drawing.Entities.OfType<DND.Electrical.Schematic.Entities.WireEntity>())) w.WireNumber = "";
        var check = await assistant.AskAsync("check my drawing for errors", p);
        Assert.Contains(check.Suggestions, s => s.FixId == "number-wires" && s.RequiresConfirmation);
        Assert.All(p.Sheets.SelectMany(s => s.Drawing.Entities.OfType<DND.Electrical.Schematic.Entities.WireEntity>()), w => Assert.Equal("", w.WireNumber)); // never modifies
        var helpAnswer = await assistant.AskAsync("how do backups work", null);
        Assert.NotEmpty(helpAnswer.Sources);
    }

    [Fact]
    public void Permissions_by_role()
    {
        var viewer = new PermissionService(UserRole.Viewer);
        Assert.False(viewer.Has(AppPermission.EditProjects));
        Assert.Throws<UnauthorizedAccessException>(() => viewer.Demand(AppPermission.ManageDocumentation));
        Assert.True(new PermissionService(UserRole.Administrator).Has(AppPermission.ManagePlugins));
        Assert.False(new PermissionService(UserRole.Engineer).Has(AppPermission.ManagePlugins));
    }
}
