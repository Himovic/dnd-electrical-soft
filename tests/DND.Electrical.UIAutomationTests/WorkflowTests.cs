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
        Assert.Equal(10, h.Main.DemoProjects.Count);
        Assert.Contains(h.Main.DemoProjects, d => d.Number == "DEMO-DC-008");
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

public class ThreeDWorkflowTests
{
    [Fact]
    public async Task Three_d_window_generates_cabinet_selects_from_schematic_and_exports()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-CAB-004");
        var p = h.Main.Session!.Project;
        h.Main.Open3DCommand.Execute(null);
        var vm = Assert.IsType<ThreeDViewModel>(h.Dialogs.Shown.Last());
        Assert.Null(vm.Scene);

        vm.GenerateFromCabinetCommand.Execute(null);
        Assert.NotNull(vm.Scene);
        Assert.Single(p.Layouts3D);
        Assert.Contains("No clashes", vm.Findings.Last());
        Assert.NotEmpty(vm.RenderItems);
        var before = vm.RenderItems.Count;

        // Section clips geometry and produces outline lines.
        vm.SectionAxis = "X"; vm.SectionOffset = 400; vm.SectionEnabled = true;
        Assert.NotEmpty(vm.SectionLines);
        Assert.All(vm.RenderItems, r => Assert.All(r.Mesh.Positions, q => Assert.True(q.X <= 400 + 1e-6)));
        vm.SectionEnabled = false;
        vm.ShowEnclosure = false;
        Assert.Equal(before - 6, vm.RenderItems.Count);

        // Selecting a device in 3D and measuring.
        var item = p.Cabinets[0].Items.First(i => i.DeviceId is not null);
        vm.SelectEngineeringObject(item.DeviceId!.Value);
        Assert.Equal(item.Id, vm.SelectedObject!.Object.Id);
        Assert.Contains(vm.RenderItems, r => r.Selected && r.SceneObjectId == item.Id);
        vm.MeasureTarget = vm.Objects.First(o => o.Object.Kind == DND.Electrical.ThreeD.SceneObjectKind.Door);
        Assert.Contains("clear distance", vm.Measurement);

        // Show in schematic navigates to the device occurrence.
        vm.ShowInSchematicCommand.Execute(null);
        Assert.NotNull(h.Main.ActiveDocument);

        // Re-opening the 3D window with the device selected focuses it.
        h.Main.Open3DCommand.Execute(null);
        var focused = Assert.IsType<ThreeDViewModel>(h.Dialogs.Shown.Last());
        Assert.NotNull(focused.Scene);

        // Moving in 3D with confirmation writes back to the 2D layout.
        var x0 = item.X;
        h.Dialogs.Prompts.Enqueue("10,0,0");
        h.Dialogs.ConfirmAnswer = true;
        vm.MoveSelectedCommand.Execute(null);
        Assert.Equal(x0 + 10, item.X, 6);

        var path = h.PathFor("cab.glb");
        h.Dialogs.SavePaths.Enqueue(path);
        vm.ExportCommand.Execute("glb");
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Fact]
    public async Task Tray_routes_and_equipment_are_added_from_text_input_and_checked()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        h.Main.Open3DCommand.Execute(null);
        var vm = Assert.IsType<ThreeDViewModel>(h.Dialogs.Shown.Last());
        h.Dialogs.Prompts.Enqueue("Pump room");
        vm.NewPlantLayoutCommand.Execute(null);
        Assert.Equal("Pump room", vm.Scene!.Name);
        h.Dialogs.Prompts.Enqueue("TR-01; 300×60; 0,0,2500 / 4000,0,2500");
        vm.AddTrayRouteCommand.Execute(null);
        Assert.Contains(vm.Findings, f => f.StartsWith("TR-01: 4 m"));
        h.Dialogs.Prompts.Enqueue("-M1; 400×300×300; 2000,0,2500");
        vm.AddEquipmentCommand.Execute(null);
        Assert.NotNull(vm.Scene.Objects.Single().ObjectId); // linked to the existing device -M1
        vm.CheckClashesCommand.Execute(null);
        Assert.Contains(vm.Findings, f => f.StartsWith("Clash:"));
        h.Dialogs.Prompts.Enqueue("garbage");
        vm.AddEquipmentCommand.Execute(null);
        Assert.Contains(h.Dialogs.Log, l => l.StartsWith("error:"));
        Assert.Single(vm.Scene.Objects);
    }

    [Fact]
    public async Task Three_d_requires_the_3d_module()
    {
        using var h = new UiHarness();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        var main = new MainViewModel(h.Services with { Licensing = limited });
        await main.OpenDemoCommand.ExecuteAsync("DEMO-CAB-004");
        main.Open3DCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "3D");
        Assert.DoesNotContain(h.Dialogs.Shown, v => v is ThreeDViewModel);
    }
}

public class PowerStudioWorkflowTests
{
    [Fact]
    public async Task PowerStudio_runs_all_studies_on_the_demo_network_and_exports()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-PWR-003");
        h.Main.OpenPowerStudioCommand.Execute(null);
        var vm = Assert.IsType<PowerStudioViewModel>(h.Dialogs.Shown.Last());
        vm.LoadDemoNetworkCommand.Execute(null);
        Assert.Single(h.Main.Session!.Project.PowerNetworks);
        vm.RunLoadFlowCommand.Execute(null);
        Assert.Contains("converged", vm.Status);
        Assert.Equal(5, vm.BusResults.Count);
        vm.RunShortCircuitCommand.Execute(null);
        Assert.Equal(5, vm.FaultResults.Count);
        Assert.Contains("IEC 60909", vm.Report);
        vm.RunCoordinationCommand.Execute(null);
        Assert.NotEmpty(vm.TccCurves);
        Assert.Contains(vm.Findings, f => f.Contains("selectivity tables"));
        vm.RunHarmonicsCommand.Execute(null);
        Assert.Contains(vm.HarmonicLines, l => l.Contains("THDv"));

        var tcc = h.PathFor("tcc.svg");
        h.Dialogs.SavePaths.Enqueue(tcc);
        vm.ExportTccCommand.Execute(null);
        Assert.StartsWith("<svg", File.ReadAllText(tcc));
    }

    [Fact]
    public async Task Edited_data_is_validated_and_stored_before_a_study()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-PWR-003");
        h.Main.OpenPowerStudioCommand.Execute(null);
        var vm = Assert.IsType<PowerStudioViewModel>(h.Dialogs.Shown.Last());
        h.Dialogs.Prompts.Enqueue("My network");
        vm.NewNetworkCommand.Execute(null);
        vm.AddElementCommand.Execute("bus");
        vm.Loads.Add(new DND.Electrical.Power.Load { Id = "X", Bus = "NOPE", PMw = 1 });
        vm.RunLoadFlowCommand.Execute(null);
        Assert.Contains(vm.Findings, f => f.Contains("unknown bus 'NOPE'"));
        vm.SelectedElement = vm.Loads.Single();
        vm.DeleteSelectedCommand.Execute(null);
        vm.Transformers.Add(new DND.Electrical.Power.Transformer { Id = "T1", HvBus = "MV", LvBus = vm.Buses.Last().Id, SnMva = 0.4, UkPercent = 4, PcuKw = 4.6 });
        vm.Loads.Add(new DND.Electrical.Power.Load { Id = "L", Bus = vm.Buses.Last().Id, PMw = 0.2, QMvar = 0.1 });
        vm.RunLoadFlowCommand.Execute(null);
        Assert.Contains("converged", vm.Status);
        var stored = DND.Electrical.Application.Services.PowerStudioService.Load(h.Main.Session!.Project.PowerNetworks.Single());
        Assert.Equal("My network", stored.Name);
        Assert.Single(stored.Transformers);
    }

    [Fact]
    public async Task New_calculators_are_available_and_power_ones_are_gated()
    {
        using var h = new UiHarness();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-PWR-003");
        var calc = new CalculationsViewModel(h.Main.Session!.Project, () => { }, limited);
        calc.Selected = calc.Calculators.First(c => c.Id == "lighting");
        calc.CalculateCommand.Execute(null);
        Assert.Contains("luminaires", calc.Report);
        calc.Selected = calc.Calculators.First(c => c.Id == "pfc");
        calc.CalculateCommand.Execute(null);
        Assert.Empty(calc.Report);
        Assert.NotEmpty(calc.Error);
        var full = new CalculationsViewModel(h.Main.Session.Project, () => { }, h.Licensing);
        full.Selected = full.Calculators.First(c => c.Id == "pfc");
        full.CalculateCommand.Execute(null);
        Assert.Contains("Qc", full.Report);

        h.Main.OpenPowerStudioCommand.Execute(null);
        Assert.IsType<PowerStudioViewModel>(h.Dialogs.Shown.Last());
    }
}

public class AutomationWorkflowTests
{
    [Fact]
    public async Task Automation_window_edits_instruments_and_reviews_cause_and_effect_logic()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-OG-009");
        var p = h.Main.Session!.Project;
        h.Main.OpenAutomationCommand.Execute(null);
        var vm = Assert.IsType<AutomationViewModel>(h.Dialogs.Shown.Last());
        Assert.Equal(9, vm.Instruments.Count);

        // Cause and effect review: ticking the gas detection cause triggers all four effects.
        var gas = vm.MatrixRows.Single(r => r.Cause.Tag.StartsWith("GD-310"));
        gas.Active = true;
        Assert.Equal(4, gas.Cells.Count(c => c.Triggered));
        Assert.Contains("XV-306 Open", vm.Evaluation);
        gas.Active = false;
        Assert.Equal("No effect triggered.", vm.Evaluation);

        // Toggle a mark and add a new cause linked to the selected instrument.
        var lowP = vm.MatrixRows.Single(r => r.Cause.Tag.StartsWith("PT-301"));
        var blowdown = lowP.Cells.Single(c => c.Effect.Tag == "XV-306");
        Assert.Equal("", blowdown.Mark);
        blowdown.ToggleCommand.Execute(null);
        Assert.Equal("X", blowdown.Mark);
        Assert.True(h.Main.Session.IsDirty);

        vm.SelectedInstrument = vm.Instruments.Single(i => i.Tag == "TT-303");
        h.Dialogs.Prompts.Enqueue("0.1,0.2,0.8,0.9");
        h.Dialogs.ConfirmAnswer = true;
        var alarmsBefore = p.Alarms.Count;
        vm.ProposeAlarmsCommand.Execute(null);
        Assert.Equal(alarmsBefore + 2, p.Alarms.Count); // H and HH already exist; L and LL added
        vm.SelectedLoop = vm.Loops.Single(l => l.Tag == "PIC-301");
        vm.GenerateLoopDiagramCommand.Execute(null);
        Assert.Contains(p.Sheets, s => s.Title == "Loop diagram PIC-301");

        // New instrument and I/O assignment through the dialog
        h.Dialogs.Prompts.Enqueue("ft-330");
        vm.AddInstrumentCommand.Execute(null);
        Assert.Equal("FT-330", vm.SelectedInstrument!.Tag);
        Assert.Equal(DND.Electrical.Domain.MeasuredVariable.Flow, vm.SelectedInstrument.Variable);
        vm.SelectedInstrument.RangeMax = 500; vm.SelectedInstrument.Unit = "Nm³/h";
        // No queued answer: the dialog returns its default, i.e. the suggested spare channel.
        vm.AssignIoCommand.Execute(null);
        Assert.NotNull(vm.SelectedInstrument.PlcChannelId);
    }

    [Fact]
    public async Task Automation_requires_the_auto_module()
    {
        using var h = new UiHarness();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        var main = new MainViewModel(h.Services with { Licensing = limited });
        await main.OpenDemoCommand.ExecuteAsync("DEMO-OG-009");
        main.OpenAutomationCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "AUTO");
    }
}

public class TwinWorkflowTests
{
    [Fact]
    public async Task Twin_window_shows_simulated_values_with_banner_alarms_and_trend()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-OG-009");
        var p = h.Main.Session!.Project;
        h.Main.OpenTwinCommand.Execute(null);
        await using var vm = Assert.IsType<TwinViewModel>(h.Dialogs.Shown.Last());
        vm.CreateSimulatedPointsCommand.Execute(null);
        Assert.Equal(p.Instruments.Count(i => i.IsAnalog), vm.Points.Count);
        Assert.All(vm.Points, t => Assert.Equal(DND.Electrical.Domain.TwinSourceKind.Simulator, t.Source));
        vm.SelectedPoint = vm.Points.First();
        await vm.PollOnceAsync();
        await vm.PollOnceAsync();
        Assert.True(vm.ShowsSimulatedData);
        Assert.All(vm.Values, v => Assert.True(v.Simulated));
        Assert.NotEmpty(vm.Trend);
        // The simulated range reaches above the HH limits of some instruments → simulated alarms are labelled.
        Assert.All(vm.ActiveAlarms, a => Assert.EndsWith("[SIMULATED]", a));

        var view = DND.Electrical.Application.Services.EngineeringObjectService.Describe(p, p.Devices.First());
        Assert.NotNull(view);
    }

    [Fact]
    public async Task Twin_requires_the_twin_module()
    {
        using var h = new UiHarness();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(Path.Combine(h.Folder, "limited"), DND.Electrical.Licensing.EngineeringModule.ELEC);
        var main = new MainViewModel(h.Services with { Licensing = limited });
        await main.OpenDemoCommand.ExecuteAsync("DEMO-OG-009");
        main.OpenTwinCommand.Execute(null);
        Assert.Contains(h.Dialogs.Shown, v => v is ModuleLockedViewModel m && m.Code == "TWIN");
    }
}

public class AssistantAuditWorkflowTests
{
    private sealed class MemorySecrets : DND.Electrical.Application.Abstractions.ISecretStore
    {
        private readonly Dictionary<string, string> _d = new();
        public bool IsSupported => true;
        public void Set(string name, string? value) { if (value is null) _d.Remove(name); else _d[name] = value; }
        public string? Get(string name) => _d.GetValueOrDefault(name);
    }

    [Fact]
    public async Task Assistant_interactions_and_decisions_are_audited_in_the_project()
    {
        using var h = new UiHarness();
        await h.Main.OpenDemoCommand.ExecuteAsync("DEMO-IND-001");
        var p = h.Main.Session!.Project;
        // Create an issue the local assistant proposes to fix.
        foreach (var w in p.Sheets.SelectMany(s => s.Drawing.Entities.OfType<DND.Electrical.Schematic.Entities.WireEntity>()).Take(2)) w.WireNumber = string.Empty;
        h.Main.OpenAssistantCommand.Execute(null);
        var vm = Assert.IsType<AssistantViewModel>(h.Dialogs.Shown.Last());
        vm.Question = "Check the project for errors";
        await vm.AskCommand.ExecuteAsync(null);
        var rec = Assert.Single(p.AiInteractions);
        Assert.Equal("local", rec.Model);
        Assert.False(rec.ProjectDataShared);
        Assert.NotEmpty(vm.Suggestions);
        var s = vm.Suggestions.First();
        h.Dialogs.ConfirmAnswer = false; // accept the proposal but decline the final confirmation: nothing runs
        vm.ApplyCommand.Execute(s);
        Assert.Contains("→ Accepted", rec.Decisions);
        Assert.DoesNotContain(s, vm.Suggestions);
        if (vm.Suggestions.FirstOrDefault() is { } other)
        {
            vm.RejectCommand.Execute(other);
            Assert.Contains("→ Rejected", rec.Decisions);
        }
    }

    [Fact]
    public void Assistant_settings_store_the_key_only_in_secure_storage()
    {
        var settings = new DND.Electrical.Application.Abstractions.UserSettings();
        var secrets = new MemorySecrets();
        var vm = new AssistantSettingsViewModel(settings, secrets) { Provider = "claude", Share = true, ApiKey = "sk-test" };
        vm.OkCommand.Execute(null);
        Assert.True(vm.DialogResult);
        vm.Apply();
        Assert.Equal("claude", settings.AssistantProvider);
        Assert.True(settings.AssistantShareProjectData);
        Assert.Equal("sk-test", secrets.Get(DND.Electrical.Application.Services.ClaudeAssistantProvider.SecretName));
        Assert.DoesNotContain("sk-test", System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.Empty(vm.ApiKey);
        vm.ClearKeyCommand.Execute(null);
        Assert.Null(secrets.Get(DND.Electrical.Application.Services.ClaudeAssistantProvider.SecretName));

        var unsupported = new AssistantSettingsViewModel(settings, null) { ApiKey = "sk-x" };
        unsupported.OkCommand.Execute(null);
        Assert.NotEqual(true, unsupported.DialogResult);
        Assert.Contains("cannot be stored securely", unsupported.Error);
    }
}
