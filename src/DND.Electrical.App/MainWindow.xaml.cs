using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DND.Electrical.App.Views;
using DND.Electrical.Presentation.ViewModels;
using DND.Electrical.Validation;

namespace DND.Electrical.App;

/// <summary>Main shell window. Code-behind only forwards UI gestures to the view model.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkTheme)) App.ApplyTheme(vm.IsDarkTheme, vm.Services.Settings.AccentColor);
        };
        vm.Validation.Issues.CollectionChanged += (_, _) => { if (vm.Validation.Issues.Count > 0) BottomTabs.SelectedIndex = 1; };
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_vm.Session is not { IsDirty: true }) return;
        e.Cancel = true;
        if (await _vm.ConfirmCloseAsync())
        {
            _vm.Services.Projects.Close();
            Closing -= OnClosing;
            Close();
        }
    }

    private void ExplorerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _vm.ActivateExplorerNode(ExplorerTree.SelectedItem as ExplorerNode);

    private void Symbols_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _vm.InsertSymbolCommand.Execute(null);
        Keyboard.Focus(Documents);
    }

    private void SearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SearchResult r }) _vm.OpenSearchResultCommand.Execute(r);
    }

    private void FocusSearch(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
        SearchTab.IsSelected = true;
    }

    private void OpenCommandPalette(object sender, RoutedEventArgs e)
    {
        var palette = new CommandPaletteViewModel(_vm.PaletteCommands());
        var w = new CommandPaletteWindow(palette) { Owner = this };
        w.ShowDialog();
    }

    private void Canvas_OperationCompleted(object? sender, EventArgs e)
    {
        _vm.NotifyEditorOperation();
        _vm.CommandLine.Prompt = _vm.Editor?.Prompt ?? string.Empty;
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.CommandLine.ExecuteCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.Editor?.CancelTool(); e.Handled = true; }
    }

    private static void Commit(object sender)
    {
        if (sender is FrameworkElement { DataContext: PropertyItem p }) p.Commit();
    }

    private void Property_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit(sender);
    private void Property_DropDownClosed(object sender, EventArgs e) => Commit(sender);

    private void Property_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Commit(sender);
        e.Handled = true;
    }

    private void Layer_Changed(object sender, RoutedEventArgs e) => _vm.Editor?.RequestRedraw();

    private void ValidationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _vm.NavigateToIssueCommand.Execute(ValidationGrid.SelectedItem as ValidationIssue);
    private void ValidationGoTo_Click(object sender, RoutedEventArgs e) => _vm.NavigateToIssueCommand.Execute(ValidationGrid.SelectedItem as ValidationIssue);

    private void ValidationFix_Click(object sender, RoutedEventArgs e)
    {
        if (ValidationGrid.SelectedItem is ValidationIssue { FixId: { } fix }) _vm.ApplyFix(fix);
        else MessageBox.Show(this, "No automatic correction is available for this message.", Title);
    }
}
