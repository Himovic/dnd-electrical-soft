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
        Assert.Contains("D&D Electrical Engineering Suite", h.Main.Title);
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
        var calc = new CalculationsViewModel(h.Main.Session!.Project, () => { });
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
