using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DND.Electrical.Shared;

namespace DND.Electrical.App.Views;

/// <summary>Modal dialog hosting a view model through its DataTemplate.</summary>
public sealed class DialogWindow : Window
{
    public DialogWindow()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("BaseWindow");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MinWidth = 420;
        MaxHeight = 900;
        SetBinding(TitleProperty, "Title");
        Content = new ContentControl { Margin = new Thickness(16) };
        ((ContentControl)Content).SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding());
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
    }
}

/// <summary>Non-modal tool window (help center, calculations, engineering data, assistant, symbol editor).</summary>
public sealed class ToolWindow : Window
{
    public ToolWindow()
    {
        Style = (Style)System.Windows.Application.Current.FindResource("BaseWindow");
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetBinding(TitleProperty, "Title");
        var cc = new ContentControl();
        cc.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding());
        Content = cc;
    }
}

public sealed class PromptWindow : Window
{
    private readonly TextBox _box;

    public PromptWindow(string title, string label, string value)
    {
        Style = (Style)System.Windows.Application.Current.FindResource("BaseWindow");
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = value };
        panel.Children.Add(_box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)FindResource("AccentButton"), MinWidth = 80 };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("FlatButton"), MinWidth = 80 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public string Value => _box.Text;
}

/// <summary>Splash screen with product identity.</summary>
public sealed class SplashWindow : Window
{
    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = 520;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(23, 43, 77)), CornerRadius = new CornerRadius(10), Padding = new Thickness(28) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/dnd-logo-256.png")), Width = 120, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(logo);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = ProductInfo.ProductName, Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "Version " + ProductInfo.VersionString, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), Margin = new Thickness(0, 6, 0, 18) });
        text.Children.Add(new TextBlock { Text = ProductInfo.PoweredBy, Foreground = Brushes.White, FontSize = 13 });
        text.Children.Add(new TextBlock { Text = ProductInfo.Copyright, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "Loading engineering libraries…", Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)), FontSize = 11, Margin = new Thickness(0, 22, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        border.Child = grid;
        Content = border;
    }
}
