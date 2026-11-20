using DND.Electrical.CAD.Editing;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Documentation;
using DND.Electrical.Domain;
using DND.Electrical.Presentation.ViewModels;
using DND.Electrical.Schematic.Editing;
using DND.Electrical.Schematic.Entities;

namespace DND.Electrical.UIAutomationTests;

/// <summary>
/// End-to-end UI workflows exercised through the view models used by the WPF views
/// (pixel-level WPF automation requires Windows; see documentation/DeveloperGuide/Testing.md).
/// </summary>
public class WorkflowTests
{
    [Fact]
    public void Startup_shows_start_page_with_demos()
    {
        using var h = new UiHarness();
        Assert.True(h.Main.ShowStartPage);
        Assert.False(h.Main.IsProjectOpen);
        Assert.Equal(5, h.Main.DemoProjects.Count);
        Assert.Contains("D&D Electrical & Automation Engineering Suite", h.Main.Title);
        Assert.Equal("Powered by D&D | Data&Development", h.Main.PoweredBy);
        Assert.False(h.Main.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_project_insert_symbols_wire_edit_save_and_reopen()
    {
        using var h = new UiHarness();
        h.Dialogs.ConfirmAnswer = false; // decline tutorials
        h.Dialogs.OnDialog = vm =>
        {
            if (vm is NewProjectViewModel np) { np.Number = "UI-001"; np.Name = "UI workflow"; np.SelectedTemplate = np.Templates.First(t => t.Id == "industrial"); }
            return true;
        };
        await h.Main.NewProjectCommand.ExecuteAsync(null);
        Assert.True(h.Main.IsProjectOpen);
        Assert.True(h.Main.SaveCommand.CanExecute(null));
        var doc = h.Main.ActiveDocument!;
        Assert.Equal("Power distribution", doc.Sheet.Title);

        // Navigation: explorer contains the sheets.
        var sheetsFolder = h.Main.Explorer.Roots[0].Children[0];
        Assert.Contains("Sheets", sheetsFolder.Title);

        // Insert two lamps through the symbol browser and connect them with the wire tool.
        var ed = (SchematicEditor)doc.Editor;
        ed.Viewport.SetSize(1600, 1200);
        ed.Viewport.SetView(4, new Point2(0, 1200));
        h.Main.Symbols.SearchText = "lamp";
        h.Main.Symbols.Selected = h.Main.Symbols.Results.First(s => s.Id == "IEC.LAMP");
        h.Main.InsertSymbolCommand.Execute(null);
        ed.PointerDown(ed.Viewport.ToScreen(new Point2(200, 200)), PointerButton.Left);
        ed.PointerDown(ed.Viewport.ToScreen(new Point2(200, 150)), PointerButton.Left);
        h.Main.SetToolCommand.Execute("wire");
        ed.PointerDown(ed.Viewport.ToScreen(new Point2(200, 190)), PointerButton.Left);
        ed.PointerDown(ed.Viewport.ToScreen(new Point2(200, 160)), PointerButton.Left);
        Assert.Single(ed.Drawing.Entities.OfType<WireEntity>());
        Assert.True(h.Main.Session!.IsDirty);

        // Property editing: rename the selected lamp.
        h.Main.SetToolCommand.Execute("select");
        var lamp = ed.Drawing.Entities.OfType<SymbolInstanceEntity>().First(i => i.SymbolId == "IEC.LAMP");
        ed.Selection.Set(new[] { lamp.Id });
        h.Main.UpdateProperties();
        var tagRow = h.Main.Properties.First(p => p.Name == "Tag");
        tagRow.Value = "-H10";
        tagRow.Commit();
        Assert.NotNull(h.Main.Session.Project.FindDeviceByTag("-H10"));
        var fn = h.Main.Properties.First(p => p.Name == "Function");
        fn.Value = "Ready lamp";
        fn.Commit();
        Assert.Equal("Ready lamp", h.Main.Session.Project.FindDeviceByTag("-H10")!.Function);
        h.Main.UndoCommand.Execute(null);
        Assert.Equal(string.Empty, h.Main.Session.Project.FindDeviceByTag("-H10")!.Function);

        // Numbering, validation, save, close, reopen.
        h.Main.NumberWiresCommand.Execute(null);
        h.Main.Validate();
        Assert.Contains("error", h.Main.Validation.Summary);
        var path = h.PathFor("ui.dndx");
        h.Dialogs.SavePaths.Enqueue(path);
        await h.Main.SaveAsCommand.ExecuteAsync(null);
        Assert.True(File.Exists(path));
        Assert.False(h.Main.Session.IsDirty);
        await h.Main.CloseProjectCommand.ExecuteAsync(null);
        Assert.False(h.Main.IsProjectOpen);
        Assert.Contains(path, h.Main.RecentProjects);
        await h.Main.OpenPathCommand.ExecuteAsync(path);
        Assert.NotNull(h.Main.Session!.Project.FindDeviceByTag("-H10"));
        Assert.Single(h.Main.Session.Project.Sheets.SelectMany(s => s.Drawing.Entities.OfType<WireEntity>()));
    }

    [Fact]
    public async Task Unsaved_changes_prompt_can_cancel_close()
    {
        using var h = new UiHarness();
        h.Dialogs.ConfirmAnswer = false;
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        h.Main.Session!.MarkDirty();
        h.Dialogs.SaveChanges = DND.Electrical.Presentation.Services.SaveChangesAnswer.Cancel;
        await h.Main.CloseProjectCommand.ExecuteAsync(null);
        Assert.True(h.Main.IsProjectOpen);
        Assert.Contains("ask-save", h.Dialogs.Log);
    }

    [Fact]
    public async Task Demo_open_validation_navigation_and_search()
    {
        using var h = new UiHarness();
        h.Dialogs.ConfirmAnswer = false;
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-AUT-002");
        Assert.Equal("DEMO-AUT-002", h.Main.Session!.Project.Info.Number);
        Assert.NotNull(h.Main.ActiveDocument);

        // Break something, validate, navigate to the issue.
        var sheet = h.Main.Session.Project.Sheets.First(s => s.Title == "PLC digital outputs");
        var wire = sheet.Drawing.Entities.OfType<WireEntity>().First();
        wire.End = WireEnd.Open();
        h.Main.Validate();
        var issue = h.Main.Validation.Issues.First(i => i.RuleId == "VAL004");
        h.Main.NavigateToIssueCommand.Execute(issue);
        Assert.Equal(sheet, h.Main.ActiveDocument!.Sheet);
        Assert.Contains(wire.Id, h.Main.ActiveDocument.Editor.Selection.Ids);

        h.Main.SearchQuery = "Part_Present";
        Assert.Contains(h.Main.SearchResults, r => r.Kind == "PLC");
        h.Main.SearchQuery = "-K1";
        var dev = h.Main.SearchResults.First(r => r.Kind == "Device");
        h.Main.OpenSearchResultCommand.Execute(dev);
        Assert.NotEmpty(h.Main.ActiveDocument!.Editor.Selection.Ids);
    }

    [Fact]
    public void Help_center_navigation_search_and_admin()
    {
        using var h = new UiHarness();
        var help = new HelpCenterViewModel(h.Services, null);
        Assert.Equal("01-introduction", help.Article!.Id);
        Assert.NotEmpty(help.Blocks);
        Assert.True(help.Categories.Count >= 6);
        help.Query = "wire numbering";
        Assert.NotEmpty(help.SearchResults);
        help.OpenCommand.Execute("09-automatic-numbering");
        Assert.Equal("Automatic numbering", help.Article!.Title);
        Assert.True(help.IsAdmin);
        help.BeginEditCommand.Execute(true);
        help.EditTitle = "Company wiring colours";
        help.EditBody = "# Company wiring colours\n\nControl wiring is dark blue.";
        help.SaveEditCommand.Execute(null);
        Assert.Equal("Company wiring colours", help.Article!.Title);
        Assert.Contains(help.Categories, c => c.Name == "Company");
        Assert.True(help.Shortcuts.Count > 10);
    }

    [Fact]
    public async Task Tutorial_progresses_with_real_actions()
    {
        using var h = new UiHarness();
        h.Dialogs.OnDialog = vm => { if (vm is NewProjectViewModel np) { np.Name = "Tutorial"; } return true; };
        h.Dialogs.ConfirmAnswer = true; // accept tutorials
        await h.Main.NewProjectCommand.ExecuteAsync(null);
        Assert.Equal("Getting started", h.Main.TutorialTitle);
        h.Main.TutorialCancelCommand.Execute(null);
        h.Main.CloseDocumentCommand.Execute(null);
        h.Main.OpenSheet(h.Main.Session!.Project.Sheets[1]);
        Assert.Equal("Your first schematic", h.Main.TutorialTitle);
        h.Dialogs.Prompts.Enqueue("Tutorial sheet");
        h.Main.NewSheetCommand.Execute(null);
        Assert.Equal("Insert a circuit breaker", h.Main.TutorialStep!.Title);
    }

    [Fact]
    public void Dialog_view_models_validate_input()
    {
        var np = new NewProjectViewModel("me") { Name = "" };
        np.OkCommand.Execute(null);
        Assert.Null(np.DialogResult);
        Assert.Contains("name is required", np.Error);
        np.Name = "OK";
        np.OkCommand.Execute(null);
        Assert.True(np.DialogResult);

        var rep = new ReportExportViewModel("en") { Format = "csv" };
        rep.OkCommand.Execute(null);
        Assert.Contains("exactly one", rep.Error);
        Assert.Contains("D&D | Data&Development", new AboutViewModel().Author);
    }

    [Fact]
    public async Task Calculations_and_engineering_data_windows()
    {
        using var h = new UiHarness();
        h.Dialogs.ConfirmAnswer = false;
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-PWR-003");
        var calc = new CalculationsViewModel(h.Main.Session!.Project, () => { }, h.Licensing);
        calc.Selected = calc.Calculators.First(c => c.Id == "vd");
        calc.CalculateCommand.Execute(null);
        Assert.Equal("Pass", calc.Status);
        Assert.Contains("Annex G", calc.Report);
        var before = h.Main.Session.Project.Calculations.Count;
        calc.StoreCommand.Execute(null);
        Assert.Equal(before + 1, h.Main.Session.Project.Calculations.Count);
        calc.Selected.Fields.First(f => f.Key == "l").Value = "abc";
        calc.CalculateCommand.Execute(null);
        Assert.NotEmpty(calc.Error);

        var data = new EngineeringDataViewModel(h.Services, h.Main.Session.Project, () => { });
        Assert.NotNull(data.Table);
        Assert.True(data.Table!.Rows.Count > 5);
        data.SelectedList = data.Lists.First(l => l.Id == "cables");
        Assert.Equal(6, data.Table!.Rows.Count);
    }

    [Fact]
    public async Task Plc_channel_can_be_linked_from_properties()
    {
        using var h = new UiHarness();
        h.Dialogs.ConfirmAnswer = false;
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-AUT-002");
        var p = h.Main.Session!.Project;
        var sheet = p.Sheets.First(s => s.Title == "PLC digital inputs");
        h.Main.OpenSheet(sheet);
        var ed = (SchematicEditor)h.Main.ActiveDocument!.Editor;
        var di = ed.InsertSymbol(h.Services.Symbols.Find("IEC.PLC.DI")!, new Point2(300, 140), deviceTag: "-A1");
        ed.Selection.Set(new[] { di.Id });
        h.Main.UpdateProperties();
        var row = h.Main.Properties.First(r => r.Name == "Channel");
        Assert.Contains("-A1 %I0.6", row.Options!);
        row.Value = "-A1 %I0.6";
        row.Commit();
        Assert.Equal("%I0.6", p.FindPlcChannel(di.PlcChannelId!.Value)!.Address);
        Assert.Equal("%I0.6", di.Display["ADDRESS"]);
    }

    [Fact]
    public void Symbol_editor_creates_custom_symbol()
    {
        using var h = new UiHarness();
        var vm = new SymbolEditorViewModel(h.Services.Symbols, h.Services.Permissions, h.Services.Symbols.Find("IEC.LAMP"));
        Assert.StartsWith("CUSTOM.", vm.SymbolId);
        vm.SymbolId = "ACME.LAMP.BLUE";
        vm.Name = "Blue lamp";
        Assert.True(vm.Save(), vm.Message);
        Assert.NotNull(h.Services.Symbols.Find("ACME.LAMP.BLUE"));
        Assert.Equal(2, h.Services.Symbols.Find("ACME.LAMP.BLUE")!.Pins.Count);
        vm.SymbolId = "IEC.LAMP";
        Assert.False(vm.Save());
    }

    [Fact]
    public void Command_palette_filters_and_runs_commands()
    {
        using var h = new UiHarness();
        var palette = new CommandPaletteViewModel(h.Main.PaletteCommands());
        palette.Query = "about";
        Assert.Single(palette.Results);
        palette.RunCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is AboutViewModel);
    }
}

public class LicensingWorkflowTests
{
    [Fact]
    public async Task Without_elec_licence_projects_open_read_only_and_modules_show_upgrade_page()
    {
        using var h = new UiHarness();
        // Replace the full test licence with one that only covers DOCS.
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.DOCS);
        var services = h.Services with { Licensing = limited };
        var main = new MainViewModel(services);
        h.Dialogs.ConfirmAnswer = false;
        await main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        Assert.True(main.IsProjectOpen);
        var ed = main.ActiveDocument!.Editor;
        Assert.True(ed.IsReadOnly);
        var count = ed.Drawing.Entities.Count;
        main.SetToolCommand.Execute("wire");
        Assert.IsType<DND.Electrical.CAD.Tools.SelectTool>(ed.ActiveTool);
        ed.SelectAll();
        ed.DeleteSelection();
        Assert.Equal(count, ed.Drawing.Entities.Count);

        main.NumberWiresCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "ELEC");
        main.OpenAssistantCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "AI");

        // Calculations: POWER calculators are refused even when the window is reached.
        var calc = new CalculationsViewModel(main.Session!.Project, () => { }, limited);
        calc.Selected = calc.Calculators.First(c => c.Id == "sc");
        calc.CalculateCommand.Execute(null);
        Assert.Contains("POWER", calc.Error);
    }

    [Fact]
    public void Documentation_service_enforces_docs_module()
    {
        using var h = new UiHarness();
        var noDocs = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "nodocs"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        var docs = new DND.Electrical.Application.Exports.DocumentationService(h.Services.Schematic, noDocs);
        var project = h.Services.Demos.Create("DEMO-IND-001");
        Assert.Throws<DND.Electrical.Licensing.LicenseException>(() => docs.DrawingSetPdf(project, h.PathFor("x.pdf")));
        Assert.False(File.Exists(h.PathFor("x.pdf")));
    }

    [Fact]
    public void Licensing_page_shows_status_and_modules()
    {
        using var h = new UiHarness();
        var vm = new LicensingViewModel(h.Licensing, h.Services.Settings, h.Dialogs, () => { }, h.Services.Paths);
        Assert.Equal("Valid", vm.State);
        Assert.Equal(12, vm.Modules.Count);
        Assert.All(vm.Modules, m => Assert.True(m.Licensed));
        Assert.Contains(vm.Details, d => d.Label == "Licence ID" && d.Value == "LIC-TEST-SUITE");
    }
}

public class ProjectManagementWorkflowTests
{
    [Fact]
    public async Task Project_management_window_tracks_lifecycle_and_resolves_conflicts_on_confirmation()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        h.Main.OpenProjectManagementCommand.Execute(null);
        var pm = Assert.IsType<ProjectManagementViewModel>(h.Dialogs.Shown.Last());
        var p = h.Main.Session!.Project;

        pm.Phase = DND.Electrical.Domain.LifecyclePhase.Commissioning;
        Assert.Equal(DND.Electrical.Domain.LifecyclePhase.Commissioning, p.Info.Phase);
        Assert.True(h.Main.Session.IsDirty);

        h.Dialogs.Prompts.Enqueue("Approver One");
        pm.AddMemberCommand.Execute(null);
        p.Members[0].Role = DND.Electrical.Domain.ProjectRole.Approver;
        h.Dialogs.Prompts.Enqueue("Approver One");
        pm.RequestApprovalCommand.Execute(null);
        var approval = Assert.Single(pm.Approvals);
        // The current user requested it, and is not a member with an approver role -> refused, nothing changes.
        pm.SelectedApproval = approval;
        h.Dialogs.Prompts.Enqueue("ok");
        pm.ApproveCommand.Execute(null);
        Assert.Equal(DND.Electrical.Domain.ApprovalStatus.Pending, approval.Status);
        Assert.Contains(h.Dialogs.Log, l => l.StartsWith("error:"));

        var motor = p.Motors.First(m => m.DeviceId is not null);
        pm.SelectedDevice = p.FindDevice(motor.DeviceId!.Value);
        Assert.Contains(pm.Representations, r => r.Kind == "Motor");
        motor.Tag += "-OLD";
        pm.DetectConflictsCommand.Execute(null);
        pm.SelectedConflict = pm.Conflicts.Single(c => c.Field == "Tag");

        h.Dialogs.ConfirmAnswer = false;          // user declines: nothing is synchronised
        h.Dialogs.Prompts.Enqueue("SchematicSymbol");
        pm.SynchronizeCommand.Execute(null);
        Assert.EndsWith("-OLD", motor.Tag);

        h.Dialogs.ConfirmAnswer = true;
        h.Dialogs.Prompts.Enqueue("SchematicSymbol");
        pm.SynchronizeCommand.Execute(null);
        Assert.Equal(pm.SelectedDevice!.Tag, motor.Tag);
        Assert.DoesNotContain(pm.Conflicts, c => c.Field == "Tag");
    }

    [Fact]
    public async Task Project_management_requires_project_module()
    {
        using var h = new UiHarness();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        var main = new MainViewModel(h.Services with { Licensing = limited });
        await main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        main.OpenProjectManagementCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "PROJECT");
        Assert.DoesNotContain(h.Dialogs.Shown, v => v is ProjectManagementViewModel);
    }
}
