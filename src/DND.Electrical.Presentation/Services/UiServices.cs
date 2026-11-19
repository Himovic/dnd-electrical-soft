namespace DND.Electrical.Presentation.Services;

public enum SaveChangesAnswer { Save, Discard, Cancel }

/// <summary>UI interactions requested by view models; implemented by the WPF shell (and by fakes in tests).</summary>
public interface IDialogService
{
    void ShowInfo(string title, string message);
    void ShowError(string title, string message);
    bool Confirm(string title, string message);
    SaveChangesAnswer AskSaveChanges(string projectName);
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string defaultName);
    string? PromptText(string title, string label, string defaultValue = "");

    /// <summary>Shows a modal window for the view model (resolved by naming convention in the shell). Returns the dialog result.</summary>
    bool? ShowDialog(object viewModel);

    /// <summary>Shows a non-modal tool window for the view model.</summary>
    void Show(object viewModel);
}

/// <summary>Lightweight application event bus (tutorials, contextual help, telemetry-free).</summary>
public sealed class AppEventBus
{
    public event EventHandler<string>? Published;
    public void Publish(string appEvent) => Published?.Invoke(this, appEvent);
}

public static class FileFilters
{
    public const string Project = "D&D project (*.dndx)|*.dndx";
    public const string Archive = "D&D project archive (*.dndpkg)|*.dndpkg";
    public const string Pdf = "PDF document (*.pdf)|*.pdf";
    public const string Xlsx = "Excel workbook (*.xlsx)|*.xlsx";
    public const string Csv = "CSV file (*.csv)|*.csv";
    public const string Dxf = "DXF drawing (*.dxf)|*.dxf";
    public const string Svg = "SVG image (*.svg)|*.svg";
    public const string Png = "PNG image (*.png)|*.png";
    public const string Json = "JSON (*.json)|*.json";
    public const string Xml = "XML (*.xml)|*.xml";
    public const string Catalog = "Catalogue (*.csv;*.xlsx)|*.csv;*.xlsx";
    public const string SymbolLibrary = "Symbol library (*.dndsym.json)|*.dndsym.json";
    public const string Image = "Images (*.png;*.jpg)|*.png;*.jpg;*.jpeg";
    public const string Any = "All files (*.*)|*.*";
}
