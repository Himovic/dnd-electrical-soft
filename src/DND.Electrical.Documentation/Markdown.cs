using System.Text;
using System.Text.RegularExpressions;

namespace DND.Electrical.Documentation;

public enum InlineKind { Text, Bold, Italic, Code, Link }

public sealed record Inline(InlineKind Kind, string Text, string? Url = null);

public abstract record Block;
public sealed record HeadingBlock(int Level, IReadOnlyList<Inline> Content) : Block;
public sealed record ParagraphBlock(IReadOnlyList<Inline> Content) : Block;
public sealed record ListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<Inline>> Items) : Block;
public sealed record CodeBlock(string Language, string Code) : Block;
public sealed record QuoteBlock(IReadOnlyList<Inline> Content) : Block;
public sealed record ImageBlock(string Alt, string Source) : Block;
public sealed record TableBlock(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) : Block;
public sealed record RuleBlock : Block;

/// <summary>
/// Small, dependency-free Markdown parser covering the subset used by the help center (CommonMark-like):
/// headings, paragraphs, lists, fenced code, block quotes, images, pipe tables, bold/italic/code/links.
/// </summary>
public static partial class Markdown
{
    [GeneratedRegex(@"(\*\*(?<b>.+?)\*\*)|(`(?<c>[^`]+)`)|(\[(?<lt>[^\]]+)\]\((?<lu>[^)]+)\))|(\*(?<i>[^*]+)\*)|(_(?<i2>[^_]+)_)")]
    private static partial Regex InlinePattern();

    [GeneratedRegex(@"^!\[(?<alt>[^\]]*)\]\((?<src>[^)]+)\)\s*$")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"^(\d+)[.)]\s+(.*)$")]
    private static partial Regex OrderedItem();

    public static IReadOnlyList<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var para = new StringBuilder();

        void FlushPara()
        {
            if (para.Length == 0) return;
            blocks.Add(new ParagraphBlock(ParseInline(para.ToString().Trim())));
            para.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var t = line.Trim();
            if (t.Length == 0) { FlushPara(); continue; }
            if (t.StartsWith("```"))
            {
                FlushPara();
                var lang = t[3..].Trim();
                var code = new StringBuilder();
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```")) code.AppendLine(lines[i]);
                blocks.Add(new CodeBlock(lang, code.ToString().TrimEnd()));
                continue;
            }
            if (t.StartsWith('#'))
            {
                FlushPara();
                var level = t.TakeWhile(c => c == '#').Count();
                blocks.Add(new HeadingBlock(Math.Min(level, 6), ParseInline(t[level..].Trim())));
                continue;
            }
            if (t is "---" or "***")
            {
                FlushPara();
                blocks.Add(new RuleBlock());
                continue;
            }
            if (ImagePattern().Match(t) is { Success: true } im)
            {
                FlushPara();
                blocks.Add(new ImageBlock(im.Groups["alt"].Value, im.Groups["src"].Value));
                continue;
            }
            if (t.StartsWith('>'))
            {
                FlushPara();
                var q = new StringBuilder(t.TrimStart('>').Trim());
                while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith('>')) q.Append(' ').Append(lines[++i].Trim().TrimStart('>').Trim());
                blocks.Add(new QuoteBlock(ParseInline(q.ToString())));
                continue;
            }
            if (t.StartsWith('|'))
            {
                FlushPara();
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                    if (!cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'))) rows.Add(cells);
                    i++;
                }
                i--;
                if (rows.Count > 0) blocks.Add(new TableBlock(rows[0], rows.Skip(1).Select(r => (IReadOnlyList<string>)r).ToList()));
                continue;
            }
            var isBullet = t.StartsWith("- ") || t.StartsWith("* ");
            var ordered = OrderedItem().Match(t);
            if (isBullet || ordered.Success)
            {
                FlushPara();
                var items = new List<IReadOnlyList<Inline>>();
                while (i < lines.Length)
                {
                    var lt = lines[i].Trim();
                    if (isBullet && (lt.StartsWith("- ") || lt.StartsWith("* "))) items.Add(ParseInline(lt[2..].Trim()));
                    else if (!isBullet && OrderedItem().Match(lt) is { Success: true } om) items.Add(ParseInline(om.Groups[2].Value));
                    else break;
                    i++;
                }
                i--;
                blocks.Add(new ListBlock(!isBullet, items));
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(t);
        }
        FlushPara();
        return blocks;
    }

    public static IReadOnlyList<Inline> ParseInline(string text)
    {
        var result = new List<Inline>();
        var pos = 0;
        foreach (Match m in InlinePattern().Matches(text))
        {
            if (m.Index > pos) result.Add(new Inline(InlineKind.Text, text[pos..m.Index]));
            if (m.Groups["b"].Success) result.Add(new Inline(InlineKind.Bold, m.Groups["b"].Value));
            else if (m.Groups["c"].Success) result.Add(new Inline(InlineKind.Code, m.Groups["c"].Value));
            else if (m.Groups["lt"].Success) result.Add(new Inline(InlineKind.Link, m.Groups["lt"].Value, m.Groups["lu"].Value));
            else result.Add(new Inline(InlineKind.Italic, m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) result.Add(new Inline(InlineKind.Text, text[pos..]));
        return result;
    }

    /// <summary>Plain text (for search indexing and tooltips).</summary>
    public static string ToPlainText(string markdown) =>
        string.Join(" ", Parse(markdown).Select(b => b switch
        {
            HeadingBlock h => Join(h.Content),
            ParagraphBlock p => Join(p.Content),
            ListBlock l => string.Join(" ", l.Items.Select(Join)),
            QuoteBlock q => Join(q.Content),
            TableBlock t => string.Join(" ", t.Header.Concat(t.Rows.SelectMany(r => r))),
            CodeBlock c => c.Code,
            _ => string.Empty,
        }));

    private static string Join(IReadOnlyList<Inline> inl) => string.Concat(inl.Select(i => i.Text));
}
