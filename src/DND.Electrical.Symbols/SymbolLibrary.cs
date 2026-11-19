using System.Text.Json;
using DND.Electrical.CAD.Serialization;

namespace DND.Electrical.Symbols;

public sealed class SymbolLibrary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Standard { get; set; } = "IEC 60617 style";
    public int Version { get; set; } = 1;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Built-in libraries cannot be modified; duplicate symbols into a custom library instead.</summary>
    public bool IsReadOnly { get; set; }

    public List<SymbolDefinition> Symbols { get; set; } = new();

    public SymbolDefinition? Find(string id) =>
        Symbols.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public string ToJson() => JsonSerializer.Serialize(this, CadJson.IndentedOptions);

    public static SymbolLibrary FromJson(string json) =>
        JsonSerializer.Deserialize<SymbolLibrary>(json, CadJson.Options) ?? throw new InvalidDataException("Empty symbol library.");

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id)) errors.Add("Library id is required.");
        foreach (var dup in Symbols.GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Duplicate symbol id '{dup.Key}'.");
        foreach (var s in Symbols)
            foreach (var e in s.Validate()) errors.Add($"{s.Id}: {e}");
        return errors;
    }
}

/// <summary>
/// Loads, caches, searches and persists symbol libraries: the built-in library plus custom JSON
/// libraries in a user folder. Symbol ids are unique across the catalogue; custom libraries are
/// searched first so that company symbols can supersede built-in ones deliberately.
/// </summary>
public sealed class SymbolLibraryService
{
    private readonly List<SymbolLibrary> _libraries = new();
    private readonly Dictionary<string, (SymbolLibrary lib, SymbolDefinition sym)> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _userFolder;

    public SymbolLibraryService(string? userLibraryFolder = null, bool includeBuiltIn = true)
    {
        _userFolder = userLibraryFolder;
        if (includeBuiltIn) AddLibrary(BuiltInSymbolLibrary.Create());
        if (_userFolder is not null && Directory.Exists(_userFolder))
        {
            foreach (var file in Directory.EnumerateFiles(_userFolder, "*.dndsym.json"))
            {
                try { AddLibrary(SymbolLibrary.FromJson(File.ReadAllText(file))); }
                catch (Exception ex) { LoadErrors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
    }

    public List<string> LoadErrors { get; } = new();
    public IReadOnlyList<SymbolLibrary> Libraries => _libraries;
    public IEnumerable<SymbolDefinition> AllSymbols => _libraries.SelectMany(l => l.Symbols);

    public event EventHandler? LibrariesChanged;

    public void AddLibrary(SymbolLibrary library)
    {
        var errors = library.Validate();
        if (errors.Count > 0) throw new InvalidDataException($"Library '{library.Id}' is invalid: {string.Join("; ", errors.Take(5))}");
        _libraries.RemoveAll(l => string.Equals(l.Id, library.Id, StringComparison.OrdinalIgnoreCase));
        if (library.IsReadOnly) _libraries.Add(library); else _libraries.Insert(0, library);
        Reindex();
    }

    private void Reindex()
    {
        _index.Clear();
        // Custom (non read-only) libraries take precedence.
        foreach (var lib in _libraries.OrderBy(l => l.IsReadOnly))
            foreach (var s in lib.Symbols)
                _index.TryAdd(s.Id, (lib, s));
        LibrariesChanged?.Invoke(this, EventArgs.Empty);
    }

    public SymbolDefinition? Find(string id) => _index.TryGetValue(id, out var v) ? v.sym : null;

    public SymbolLibrary? FindLibraryOf(string symbolId) => _index.TryGetValue(symbolId, out var v) ? v.lib : null;

    public IReadOnlyList<SymbolDefinition> Search(string? text, SymbolCategory? category = null)
    {
        var q = (text ?? string.Empty).Trim();
        return _index.Values.Select(v => v.sym)
            .Where(s => category is null || s.Category == category)
            .Where(s => q.Length == 0
                        || s.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || s.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || s.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Category).ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>Creates (or returns) an editable custom library.</summary>
    public SymbolLibrary GetOrCreateCustomLibrary(string id = "custom", string name = "Custom symbols")
    {
        var lib = _libraries.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
        if (lib is not null) return lib;
        lib = new SymbolLibrary { Id = id, Name = name, Author = Environment.UserName, Description = "User-defined symbols" };
        AddLibrary(lib);
        return lib;
    }

    /// <summary>Adds or updates a symbol in a writable library, incrementing its version, and saves it.</summary>
    public SymbolDefinition SaveSymbol(SymbolLibrary library, SymbolDefinition symbol)
    {
        if (library.IsReadOnly) throw new InvalidOperationException($"Library '{library.Name}' is read-only. Duplicate the symbol into a custom library.");
        var errors = symbol.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var existing = library.Find(symbol.Id);
        if (existing is not null)
        {
            symbol.Version = existing.Version + 1;
            library.Symbols[library.Symbols.IndexOf(existing)] = symbol;
        }
        else library.Symbols.Add(symbol);
        library.Version++;
        library.Modified = DateTimeOffset.UtcNow;
        Persist(library);
        Reindex();
        return symbol;
    }

    public void DeleteSymbol(SymbolLibrary library, string symbolId)
    {
        if (library.IsReadOnly) throw new InvalidOperationException("Library is read-only.");
        library.Symbols.RemoveAll(s => string.Equals(s.Id, symbolId, StringComparison.OrdinalIgnoreCase));
        library.Version++;
        Persist(library);
        Reindex();
    }

    /// <summary>Copies a symbol under a new id into a writable library.</summary>
    public SymbolDefinition Duplicate(string sourceId, SymbolLibrary target, string newId, string? newName = null)
    {
        var src = Find(sourceId) ?? throw new KeyNotFoundException($"Symbol '{sourceId}' not found.");
        if (Find(newId) is not null) throw new InvalidOperationException($"Symbol id '{newId}' already exists.");
        var copy = src.Clone();
        copy.Id = newId;
        copy.Name = newName ?? src.Name + " (copy)";
        copy.Version = 1;
        return SaveSymbol(target, copy);
    }

    public void Persist(SymbolLibrary library)
    {
        if (_userFolder is null || library.IsReadOnly) return;
        Directory.CreateDirectory(_userFolder);
        var path = Path.Combine(_userFolder, SafeFileName(library.Id) + ".dndsym.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, library.ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public SymbolLibrary Import(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 50 * 1024 * 1024) throw new InvalidDataException("Symbol library file is too large.");
        var lib = SymbolLibrary.FromJson(File.ReadAllText(path));
        lib.IsReadOnly = false;
        AddLibrary(lib);
        Persist(lib);
        return lib;
    }

    public void Export(SymbolLibrary library, string path) => File.WriteAllText(path, library.ToJson());

    private static string SafeFileName(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
