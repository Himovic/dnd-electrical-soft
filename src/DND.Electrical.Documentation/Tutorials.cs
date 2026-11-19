namespace DND.Electrical.Documentation;

/// <summary>A tutorial step completes when the application raises <see cref="CompletionEvent"/> (or manually when null).</summary>
public sealed record TutorialStep(string Title, string Text, string? CompletionEvent = null, string? HelpArticle = null);

public sealed record TutorialDefinition(string Id, string Title, string TriggerEvent, IReadOnlyList<TutorialStep> Steps);

/// <summary>Application events used by tutorials and contextual help.</summary>
public static class AppEvents
{
    public const string SchematicEditorOpened = "schematic-editor-opened";
    public const string SheetCreated = "sheet-created";
    public const string SymbolInserted = "symbol-inserted";
    public const string WireDrawn = "wire-drawn";
    public const string WiresNumbered = "wires-numbered";
    public const string ProjectValidated = "project-validated";
    public const string PdfExported = "pdf-exported";
    public const string ProjectCreated = "project-created";

    public static string SymbolInserted_(string category) => $"{SymbolInserted}:{category}";
}

/// <summary>Runs one tutorial at a time and advances on matching application events.</summary>
public sealed class TutorialEngine
{
    public static readonly IReadOnlyList<TutorialDefinition> BuiltIn = new[]
    {
        new TutorialDefinition("first-schematic", "Your first schematic", AppEvents.SchematicEditorOpened, new[]
        {
            new TutorialStep("Create a sheet", "Use Project ▸ New sheet (or the + button in the Project Explorer) to add a schematic sheet.", AppEvents.SheetCreated, "06-schematic-drawing"),
            new TutorialStep("Insert a circuit breaker", "Open the Symbol Browser, search for \"breaker\" and double-click \"Circuit breaker 3-pole\". Click on the sheet to place it.", AppEvents.SymbolInserted_("Protection"), "07-symbol-management"),
            new TutorialStep("Insert a contactor", "Search for \"contactor\" and place the main contacts below the breaker. Place the coil on the control part: give it the same tag (e.g. -K1).", AppEvents.SymbolInserted_("Switching"), "10-component-management"),
            new TutorialStep("Connect the components", "Choose Drawing ▸ Wire (W). Click a pin of the breaker, then a pin of the contactor. Wires snap to pins and route orthogonally.", AppEvents.WireDrawn, "08-electrical-connections"),
            new TutorialStep("Assign wire numbers", "Run Engineering ▸ Number wires. Each potential receives a number; named supplies keep their names.", AppEvents.WiresNumbered, "09-automatic-numbering"),
            new TutorialStep("Validate the drawing", "Run Engineering ▸ Validate (F8). Double-click a message to jump to the affected object.", AppEvents.ProjectValidated, "17-validation"),
            new TutorialStep("Export a PDF", "Use Reports ▸ Drawing set PDF to produce the printable drawing set with title blocks.", AppEvents.PdfExported, "18-report-generation"),
        }),
        new TutorialDefinition("getting-started", "Getting started", AppEvents.ProjectCreated, new[]
        {
            new TutorialStep("Explore the project", "The Project Explorer (left) lists sheets, devices, cables, PLCs and cabinets. Double-click a sheet to open it.", null, "03-interface-overview"),
            new TutorialStep("Edit project properties", "Project ▸ Properties sets customer, company and revision data used in every title block.", null, "05-project-management"),
            new TutorialStep("Open the help center", "Press F1 at any time. Hover over buttons for contextual tooltips.", null, "01-introduction"),
        }),
    };

    private int _step;

    public TutorialDefinition? Active { get; private set; }
    public TutorialStep? CurrentStep => Active is null || _step >= Active.Steps.Count ? null : Active.Steps[_step];
    public int StepIndex => _step;
    public bool IsCompleted => Active is not null && _step >= Active.Steps.Count;

    public event EventHandler? Changed;
    public event EventHandler<TutorialDefinition>? Completed;

    /// <summary>Returns a tutorial to offer for <paramref name="appEvent"/>, unless it was already completed.</summary>
    public TutorialDefinition? OfferFor(string appEvent, IReadOnlySet<string> completed) =>
        Active is null ? BuiltIn.FirstOrDefault(t => t.TriggerEvent == appEvent && !completed.Contains(t.Id)) : null;

    public void Start(TutorialDefinition tutorial)
    {
        Active = tutorial;
        _step = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Feeds an application event; advances when it matches the current step (prefix match for categorised events).</summary>
    public bool OnEvent(string appEvent)
    {
        var step = CurrentStep;
        if (step?.CompletionEvent is null) return false;
        var match = appEvent == step.CompletionEvent || (step.CompletionEvent.Contains(':') == false && appEvent.StartsWith(step.CompletionEvent + ":", StringComparison.Ordinal));
        if (!match) return false;
        Next();
        return true;
    }

    public void Next()
    {
        if (Active is null) return;
        _step++;
        Changed?.Invoke(this, EventArgs.Empty);
        if (IsCompleted)
        {
            var done = Active;
            Active = null;
            Completed?.Invoke(this, done);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Cancel()
    {
        Active = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record Shortcut(string Keys, string Action, string Context);

public static class KeyboardShortcuts
{
    public static readonly IReadOnlyList<Shortcut> All = new[]
    {
        new Shortcut("Ctrl+N", "New project", "Application"), new Shortcut("Ctrl+O", "Open project", "Application"),
        new Shortcut("Ctrl+S", "Save", "Application"), new Shortcut("Ctrl+Shift+S", "Save as", "Application"),
        new Shortcut("Ctrl+Shift+P", "Command palette", "Application"), new Shortcut("Ctrl+F", "Search everywhere", "Application"),
        new Shortcut("F1", "Help center", "Application"), new Shortcut("F8", "Validate project", "Engineering"),
        new Shortcut("Ctrl+Z / Ctrl+Y", "Undo / Redo", "Editing"), new Shortcut("Ctrl+C / Ctrl+X / Ctrl+V", "Copy / Cut / Paste", "Editing"),
        new Shortcut("Ctrl+D", "Duplicate selection", "Editing"), new Shortcut("Ctrl+A", "Select all", "Editing"),
        new Shortcut("Delete", "Delete selection", "Editing"), new Shortcut("R", "Rotate selection / symbol 90°", "Editing"),
        new Shortcut("X", "Mirror symbol while inserting", "Insert symbol"), new Shortcut("W", "Wire tool", "Drawing"),
        new Shortcut("L", "Line tool", "Drawing"), new Shortcut("Esc", "Cancel tool / clear selection", "Drawing"),
        new Shortcut("Enter", "Finish polyline / open wire", "Drawing"), new Shortcut("F", "Zoom extents", "View"),
        new Shortcut("Mouse wheel", "Zoom at cursor", "View"), new Shortcut("Middle drag", "Pan", "View"),
        new Shortcut("F7 / F9 / F10", "Grid / snap / ortho", "View"), new Shortcut("Shift (hold)", "Temporarily disable ortho", "Drawing"),
        new Shortcut("Alt (hold)", "Temporarily disable snapping", "Drawing"),
    };
}
