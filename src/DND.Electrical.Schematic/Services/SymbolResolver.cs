using DND.Electrical.Domain;
using DND.Electrical.Symbols;

namespace DND.Electrical.Schematic.Services;

/// <summary>Resolves symbol definitions: project cache first, then the symbol library catalogue.</summary>
public sealed class SymbolResolver(SymbolLibraryService libraries)
{
    public SymbolLibraryService Libraries { get; } = libraries;

    public SymbolDefinition? Resolve(ElectricalProject project, string symbolId)
    {
        var cached = project.GetCachedSymbol(symbolId);
        if (cached is not null) return cached;
        var def = Libraries.Find(symbolId);
        if (def is not null) project.CacheSymbol(def, Libraries.FindLibraryOf(symbolId)?.Id ?? string.Empty);
        return def;
    }

    /// <summary>Updates project-cached symbols to newer library versions. Returns the ids updated.</summary>
    public IReadOnlyList<string> UpdateCachedSymbols(ElectricalProject project)
    {
        var updated = new List<string>();
        foreach (var ps in project.Symbols.ToList())
        {
            var lib = Libraries.Find(ps.SymbolId);
            if (lib is not null && lib.Version > ps.Version)
            {
                project.CacheSymbol(lib, Libraries.FindLibraryOf(lib.Id)?.Id ?? string.Empty);
                updated.Add(ps.SymbolId);
            }
        }
        return updated;
    }
}
