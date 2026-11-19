using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DND.Electrical.Presentation.ViewModels;

namespace DND.Electrical.App.Views;

/// <summary>Command palette (Ctrl+Shift+P): type to filter, Enter to run.</summary>
public sealed class CommandPaletteWindow : Window
{
    public CommandPaletteWindow(CommandPaletteViewModel vm)
    {
        DataContext = vm;
        Style = (Style)FindResource("BaseWindow");
        Title = "Command palette";
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var box = new TextBox { Margin = new Thickness(10), FontSize = 15 };
        box.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(vm.Query)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        var list = new ListBox { Margin = new Thickness(10, 0, 10, 10) };
        list.SetBinding(ItemsControl.ItemsSourceProperty, nameof(vm.Results));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, nameof(vm.Selected));
        list.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><DockPanel><TextBlock DockPanel.Dock='Right' Text='{Binding Shortcut}' Opacity='0.6'/><TextBlock><Run Text='{Binding Category, Mode=OneWay}' Foreground='#2563EB'/><Run Text='  '/><Run Text='{Binding Name, Mode=OneWay}'/></TextBlock></DockPanel></DataTemplate>");
        var panel = new DockPanel();
        DockPanel.SetDock(box, Dock.Top);
        panel.Children.Add(box);
        panel.Children.Add(list);
        Content = panel;
        void Run() { Close(); vm.RunCommand.Execute(vm.Selected); }
        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Enter: Run(); e.Handled = true; break;
                case Key.Down when list.SelectedIndex < list.Items.Count - 1: list.SelectedIndex++; e.Handled = true; break;
                case Key.Up when list.SelectedIndex > 0: list.SelectedIndex--; e.Handled = true; break;
            }
        };
        list.MouseDoubleClick += (_, _) => Run();
        Loaded += (_, _) => box.Focus();
    }
}
