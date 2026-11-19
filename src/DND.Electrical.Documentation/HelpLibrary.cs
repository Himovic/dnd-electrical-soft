using System.Text;
using System.Text.Json;
using DND.Electrical.Shared;

namespace DND.Electrical.Documentation;

/// <summary>A help article stored as Markdown with a small front-matter header.</summary>
public sealed class HelpArticle
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public int Order { get; set; } = 100;
    public string Version { get; set; } = ProductInfo.VersionString;
    public List<string> Keywords { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public string? SourcePath { get; set; }

    /// <summary>True when the article comes from the administrator folder (overrides the shipped one).</summary>
    public bool IsCustom { get; set; }

    public static HelpArticle Parse(string text, string id, string? path = null)
    {
        var a = new HelpArticle { Id = id, SourcePath = path };
        var body = text.Replace("\r\n", "\n");
        if (body.StartsWith("---\n"))
        {
            var end = body.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end > 0)
            {
                foreach (var line in body[4..end].Split('\n'))
                {
                    var c = line.IndexOf(':');
                    if (c <= 0) continue;
                    var key = line[..c].Trim().ToLowerInvariant();
                    var value = line[(c + 1)..].Trim();
                    switch (key)
                    {
                        case "title": a.Title = value; break;
                        case "category": a.Category = value; break;
                        case "order": a.Order = int.TryParse(value, out var o) ? o : 100; break;
                        case "version": a.Version = value; break;
                        case "keywords": a.Keywords = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                    }
                }
                body = body[(end + 4)..].TrimStart('\n');
            }
        }
        a.Body = body;
        if (string.IsNullOrEmpty(a.Title))
            a.Title = Markdown.Parse(body).OfType<HeadingBlock>().FirstOrDefault() is { } h ? string.Concat(h.Content.Select(i => i.Text)) : id;
        return a;
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: {Title}");
        sb.AppendLine($"category: {Category}");
        sb.AppendLine($"order: {Order}");
        sb.AppendLine($"version: {Version}");
        sb.AppendLine($"keywords: {string.Join(", ", Keywords)}");
        sb.AppendLine("---");
        sb.Append(Body);
        return sb.ToString();
    }
}

public sealed record HelpSearchHit(HelpArticle Article, double Score, string Snippet);

public sealed record DocumentationVersion(string Version, DateTimeOffset Published, string PublishedBy, int Articles);

/// <summary>
/// Help center content: shipped articles (read-only folder) plus administrator-managed articles that
/// override or extend them. Content lives outside the application binaries and is versioned.
/// </summary>
public sealed class HelpLibrary
{
    private readonly string _builtInFolder;
    private readonly string? _adminFolder;
    private readonly Dictionary<string, HelpArticle> _articles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _plain = new(StringComparer.OrdinalIgnoreCase);

    public HelpLibrary(string builtInFolder, string? adminFolder = null)
    {
        _builtInFolder = builtInFolder;
        _adminFolder = adminFolder;
        Reload();
    }

    public IReadOnlyCollection<HelpArticle> Articles => _articles.Values;
    public string? AdminFolder => _adminFolder;

    public IReadOnlyList<string> Categories => _articles.Values.GroupBy(a => a.Category)
        .OrderBy(g => g.Min(a => a.Order)).Select(g => g.Key).ToList();

    public void Reload()
    {
        _articles.Clear();
        _plain.Clear();
        Load(_builtInFolder, custom: false);
        if (_adminFolder is not null) Load(_adminFolder, custom: true);
    }

    private void Load(string folder, bool custom)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            var a = HelpArticle.Parse(File.ReadAllText(file), id, file);
            a.IsCustom = custom;
            _articles[id] = a;
            _plain[id] = Markdown.ToPlainText(a.Body);
        }
    }

    public HelpArticle? Get(string id) => _articles.GetValueOrDefault(id);

    public IReadOnlyList<HelpArticle> InCategory(string category) =>
        _articles.Values.Where(a => a.Category == category).OrderBy(a => a.Order).ThenBy(a => a.Title).ToList();

    /// <summary>Ranked full-text search (title, keywords and body; all terms must match).</summary>
    public IReadOnlyList<HelpSearchHit> Search(string query, int max = 20)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.ToLowerInvariant()).ToArray();
        if (terms.Length == 0) return Array.Empty<HelpSearchHit>();
        var hits = new List<HelpSearchHit>();
        foreach (var a in _articles.Values)
        {
            var plain = _plain[a.Id];
            var lowerPlain = plain.ToLowerInvariant();
            var title = a.Title.ToLowerInvariant();
            var kw = string.Join(" ", a.Keywords).ToLowerInvariant();
            double score = 0;
            var all = true;
            foreach (var t in terms)
            {
                var s = (title.Contains(t) ? 10 : 0) + (kw.Contains(t) ? 5 : 0) + Math.Min(5, Count(lowerPlain, t));
                if (s == 0) { all = false; break; }
                score += s;
            }
            if (!all) continue;
            var idx = lowerPlain.IndexOf(terms[0], StringComparison.Ordinal);
            var start = Math.Max(0, idx - 60);
            var snippet = (start > 0 ? "…" : "") + plain.Substring(start, Math.Min(160, plain.Length - start)).Trim() + "…";
            hits.Add(new HelpSearchHit(a, score, snippet));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Article.Title).Take(max).ToList();
    }

    private static int Count(string text, string term)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(term, i, StringComparison.Ordinal)) >= 0) { n++; i += term.Length; }
        return n;
    }

    // ------------------------------------------------------------------ administration

    private string RequireAdmin() => _adminFolder ?? throw new InvalidOperationException("No documentation administration folder configured.");

    /// <summary>Creates or updates an article in the administrator folder (shipped files are never modified).</summary>
    public HelpArticle Save(HelpArticle article)
    {
        var folder = RequireAdmin();
        if (string.IsNullOrWhiteSpace(article.Id) || article.Id.Any(c => Path.GetInvalidFileNameChars().Contains(c) || c == '.'))
            throw new ArgumentException("Article id must be a simple file name without dots.");
        Directory.CreateDirectory(folder);
        article.Version = ProductInfo.VersionString;
        var path = Path.Combine(folder, article.Id + ".md");
        File.WriteAllText(path, article.Serialize());
        Reload();
        return _articles[article.Id];
    }

    public void DeleteCustom(string id)
    {
        var path = Path.Combine(RequireAdmin(), id + ".md");
        if (File.Exists(path)) File.Delete(path);
        Reload();
    }

    /// <summary>Copies an image or PDF into the documentation assets and returns the relative reference to use in Markdown.</summary>
    public string AddAsset(string sourceFile)
    {
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".pdf" or ".mp4" or ".webm"))
            throw new ArgumentException("Only images, PDF manuals and videos (mp4/webm) can be attached.");
        var dir = Path.Combine(RequireAdmin(), "assets");
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(sourceFile);
        File.Copy(sourceFile, Path.Combine(dir, name), overwrite: true);
        return "assets/" + name;
    }

    public string? ResolveAsset(string reference)
    {
        foreach (var root in new[] { _adminFolder, _builtInFolder })
        {
            if (root is null) continue;
            var full = Path.GetFullPath(Path.Combine(root, reference));
            if (full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) && File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Records a published documentation version associated with the software version.</summary>
    public DocumentationVersion Publish(string publishedBy)
    {
        var folder = RequireAdmin();
        Directory.CreateDirectory(folder);
        var manifestPath = Path.Combine(folder, "versions.json");
        var list = File.Exists(manifestPath) ? JsonSerializer.Deserialize<List<DocumentationVersion>>(File.ReadAllText(manifestPath)) ?? new() : new();
        var v = new DocumentationVersion(ProductInfo.VersionString, DateTimeOffset.Now, publishedBy, _articles.Count);
        list.Add(v);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        return v;
    }

    public IReadOnlyList<DocumentationVersion> PublishedVersions()
    {
        var manifestPath = _adminFolder is null ? null : Path.Combine(_adminFolder, "versions.json");
        return manifestPath is not null && File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<List<DocumentationVersion>>(File.ReadAllText(manifestPath)) ?? new()
            : new List<DocumentationVersion>();
    }
}
