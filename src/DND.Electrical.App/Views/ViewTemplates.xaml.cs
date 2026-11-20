using System.Windows.Controls;
using System.Windows.Input;
using DND.Electrical.CAD.Editing;
using DND.Electrical.Domain;
using DND.Electrical.Presentation.ViewModels;

namespace DND.Electrical.App.Views;

/// <summary>Code-behind for the view templates (small UI glue only; logic stays in view models).</summary>
public partial class ViewTemplates
{
    private static readonly CadCommandProcessor SymbolCommands = new();

    public ViewTemplates() => InitializeComponent();

    private void StoredCalculation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { Tag: CalculationsViewModel vm, SelectedItem: CalculationRecord record }) vm.ShowStoredCommand.Execute(record);
    }

    /// <summary>Marks the project modified after an in-place edit of a project-management list.</summary>
    private void ProjectGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit && sender is System.Windows.FrameworkElement { DataContext: DND.Electrical.Presentation.ViewModels.ProjectManagementViewModel vm })
            vm.Changed();
    }

    private void AutomationGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit && sender is System.Windows.FrameworkElement { DataContext: AutomationViewModel vm }) vm.Changed();
    }

    private void TwinGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit && sender is System.Windows.FrameworkElement { DataContext: TwinViewModel vm }) vm.Changed();
    }

    private void SymbolCommand_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { Tag: CadEditor editor } box) return;
        var result = SymbolCommands.Execute(editor, box.Text);
        box.Text = string.Empty;
        box.ToolTip = result ?? editor.Prompt;
        e.Handled = true;
    }
}
