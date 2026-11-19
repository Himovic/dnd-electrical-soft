using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;
using DND.Electrical.Documentation;
using MdBlock = DND.Electrical.Documentation.Block;
using MdInline = DND.Electrical.Documentation.Inline;
using DND.Electrical.Rendering;
using DND.Electrical.Reports;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;

namespace DND.Electrical.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch { bool x => x, null => false, string s => s.Length > 0, _ => true };
        return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Severity.Error => Brushes.Firebrick,
        Severity.Warning => Brushes.DarkOrange,
        _ => Brushes.SteelBlue,
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Renders a symbol definition into a small bitmap (cached) for the symbol browser.</summary>
public sealed class SymbolPreviewConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SymbolDefinition s) return null;
        var key = $"{s.Id}:{s.Version}";
        if (Cache.TryGetValue(key, out var bmp)) return bmp;
        var d = new CadDrawing();
        foreach (var g in s.Graphics) d.Add(g.Clone());
        foreach (var p in s.Pins) d.Add(new CAD.Entities.CircleEntity(p.Position, 0.6) { Filled = true, Color = CadColor.Blue });
        var area = d.GetExtents().Inflate(2);
        var size = Math.Max(area.Width, area.Height);
        area = Rect2.FromCenter(area.Center, size / 2, size / 2);
        var png = DrawingExporter.ToPng(d, area, 64 / size * 2, transparent: true);
        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = new MemoryStream(png);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        Cache[key] = img;
        return img;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Report table → DataView for DataGrid display.</summary>
public sealed class ReportTableConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ReportTable t) return null;
        var dt = new DataTable(t.Title);
        var names = new List<string>();
        foreach (var c in t.Columns)
        {
            var name = c.Header;
            var i = 2;
            while (names.Contains(name)) name = $"{c.Header} {i++}";
            names.Add(name);
            dt.Columns.Add(name.Replace(".", "·").Replace("[", "(").Replace("]", ")").Replace("/", "∕"));
        }
        foreach (var r in t.Rows) dt.Rows.Add(r.Cast<object>().ToArray());
        return dt.DefaultView;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Markdown blocks → FlowDocument for the help center.</summary>
public sealed class MarkdownToFlowDocumentConverter : IValueConverter
{
    public static Func<string, string?>? AssetResolver { get; set; }
    public static Action<string>? LinkHandler { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(24), FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 22 };
        if (value is not IReadOnlyList<MdBlock> blocks) return doc;
        foreach (var b in blocks)
        {
            switch (b)
            {
                case HeadingBlock h:
                    var hp = new Paragraph { FontSize = h.Level switch { 1 => 26, 2 => 20, _ => 16 }, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, h.Level == 1 ? 0 : 14, 0, 6) };
                    AddInlines(hp.Inlines, h.Content);
                    doc.Blocks.Add(hp);
                    break;
                case ParagraphBlock p:
                    var para = new Paragraph();
                    AddInlines(para.Inlines, p.Content);
                    doc.Blocks.Add(para);
                    break;
                case ListBlock l:
                    var list = new List { MarkerStyle = l.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
                    foreach (var item in l.Items)
                    {
                        var ip = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                        AddInlines(ip.Inlines, item);
                        list.ListItems.Add(new ListItem(ip));
                    }
                    doc.Blocks.Add(list);
                    break;
                case CodeBlock c:
                    doc.Blocks.Add(new Paragraph(new Run(c.Code)) { FontFamily = new FontFamily("Consolas"), FontSize = 13, Background = new SolidColorBrush(Color.FromArgb(30, 100, 116, 139)), Padding = new Thickness(10) });
                    break;
                case QuoteBlock q:
                    var qp = new Paragraph { BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 4, 4, 4), FontStyle = FontStyles.Italic };
                    AddInlines(qp.Inlines, q.Content);
                    doc.Blocks.Add(qp);
                    break;
                case TableBlock t:
                    var table = new Table { CellSpacing = 0, BorderBrush = Brushes.LightSlateGray, BorderThickness = new Thickness(0.5) };
                    var group = new TableRowGroup();
                    table.RowGroups.Add(group);
                    foreach (var _ in t.Header) table.Columns.Add(new TableColumn());
                    group.Rows.Add(Row(t.Header, true));
                    foreach (var r in t.Rows) group.Rows.Add(Row(r, false));
                    doc.Blocks.Add(table);
                    break;
                case ImageBlock img when AssetResolver?.Invoke(img.Source) is { } path:
                    doc.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Image { Source = new BitmapImage(new Uri(path)), MaxHeight = 360, Stretch = Stretch.Uniform }));
                    break;
                case RuleBlock:
                    doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1) });
                    break;
            }
        }
        return doc;
    }

    private static TableRow Row(IEnumerable<string> cells, bool header)
    {
        var row = new TableRow();
        foreach (var c in cells)
            row.Cells.Add(new TableCell(new Paragraph(new Run(c)) { Margin = new Thickness(6, 3, 6, 3), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal })
            { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0.5, 0.5) });
        return row;
    }

    private static void AddInlines(InlineCollection target, IReadOnlyList<MdInline> inlines)
    {
        foreach (var i in inlines)
        {
            System.Windows.Documents.Inline run = i.Kind switch
            {
                InlineKind.Bold => new Bold(new Run(i.Text)),
                InlineKind.Italic => new Italic(new Run(i.Text)),
                InlineKind.Code => new Run(i.Text) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromArgb(30, 100, 116, 139)) },
                InlineKind.Link => MakeLink(i),
                _ => new Run(i.Text),
            };
            target.Add(run);
        }
    }

    private static Hyperlink MakeLink(MdInline i)
    {
        var link = new Hyperlink(new Run(i.Text));
        link.Click += (_, _) =>
        {
            if (i.Url is null) return;
            if (i.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(i.Url) { UseShellExecute = true });
            else if (AssetResolver?.Invoke(i.Url) is { } asset)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(asset) { UseShellExecute = true });
            else LinkHandler?.Invoke(i.Url);
        };
        return link;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
