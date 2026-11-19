using System.ComponentModel;
using System.Windows;
using DND.Electrical.App.Converters;
using DND.Electrical.App.Views;
using DND.Electrical.Presentation.Services;
using DND.Electrical.Presentation.ViewModels;
using DND.Electrical.Shared;
using Microsoft.Win32;

namespace DND.Electrical.App.Services;

/// <summary>WPF implementation of dialogs. View models are displayed through DataTemplates (Views/ViewTemplates.xaml).</summary>
public sealed class WpfDialogService : IDialogService
{
    private readonly Dictionary<Type, Window> _toolWindows = new();

    private static Window? Owner => System.Windows.Application.Current?.MainWindow is { IsVisible: true } w ? w : null;

    private static MessageBoxResult Box(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } o ? MessageBox.Show(o, message, title, buttons, image) : MessageBox.Show(message, title, buttons, image);

    public void ShowInfo(string title, string message) => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string title, string message) => Box(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string message) => Box(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public SaveChangesAnswer AskSaveChanges(string projectName) =>
        Box($"Save changes to \"{projectName}\"?", ProductInfo.ProductName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => SaveChangesAnswer.Save,
            MessageBoxResult.No => SaveChangesAnswer.Discard,
            _ => SaveChangesAnswer.Cancel,
        };

    public string? OpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter + "|All files (*.*)|*.*", CheckFileExists = true };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var d = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? PromptText(string title, string label, string defaultValue = "")
    {
        var w = new PromptWindow(title, label, defaultValue) { Owner = Owner };
        return w.ShowDialog() == true ? w.Value : null;
    }

    public bool? ShowDialog(object viewModel)
    {
        var w = new DialogWindow { DataContext = viewModel, Owner = Owner };
        if (viewModel is DialogViewModel dvm)
        {
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName == nameof(DialogViewModel.DialogResult) && dvm.DialogResult is { } r) w.DialogResult = r;
            };
            dvm.PropertyChanged += handler;
            try { return w.ShowDialog(); }
            finally { dvm.PropertyChanged -= handler; }
        }
        return w.ShowDialog();
    }

    public void Show(object viewModel)
    {
        if (viewModel is EntityActivatedHint) return; // the main window focuses the properties panel itself
        if (viewModel is HelpCenterViewModel help)
        {
            MarkdownToFlowDocumentConverter.AssetResolver = help.ResolveAsset;
            MarkdownToFlowDocumentConverter.LinkHandler = id => help.Open(id);
        }
        // One window per tool type; re-showing replaces its content.
        if (_toolWindows.TryGetValue(viewModel.GetType(), out var existing) && existing.IsLoaded)
        {
            existing.DataContext = viewModel;
            existing.Activate();
            return;
        }
        var w = new ToolWindow { DataContext = viewModel, Owner = Owner };
        w.Closed += (_, _) => _toolWindows.Remove(viewModel.GetType());
        _toolWindows[viewModel.GetType()] = w;
        w.Show();
    }
}
