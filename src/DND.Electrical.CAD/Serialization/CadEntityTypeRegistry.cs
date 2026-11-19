using DND.Electrical.CAD.Entities;

namespace DND.Electrical.CAD.Serialization;

/// <summary>
/// Maps type discriminators to entity CLR types. Modules (e.g. the schematic module) register
/// their own entity types here so that drawings remain open for extension.
/// </summary>
public static class CadEntityTypeRegistry
{
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> ByType = new();
    private static readonly object Gate = new();

    static CadEntityTypeRegistry()
    {
        Register<LineEntity>("Line");
        Register<PolylineEntity>("Polyline");
        Register<CircleEntity>("Circle");
        Register<ArcEntity>("Arc");
        Register<TextEntity>("Text");
        Register<DimensionEntity>("Dimension");
        Register<LeaderEntity>("Leader");
        Register<ImageEntity>("Image");
        Register<GroupEntity>("Group");
    }

    public static void Register<T>(string name) where T : CadEntity, new() => Register(name, typeof(T));

    public static void Register(string name, Type type)
    {
        if (!typeof(CadEntity).IsAssignableFrom(type)) throw new ArgumentException($"{type} is not a CadEntity.");
        lock (Gate)
        {
            if (ByName.TryGetValue(name, out var existing) && existing != type)
                throw new InvalidOperationException($"Entity type name '{name}' already registered for {existing}.");
            ByName[name] = type;
            ByType[type] = name;
        }
    }

    public static Type? Resolve(string name)
    {
        lock (Gate) return ByName.GetValueOrDefault(name);
    }

    public static string GetName(Type type)
    {
        lock (Gate)
            return ByType.TryGetValue(type, out var n) ? n : throw new InvalidOperationException($"Entity type {type} is not registered.");
    }

    public static IReadOnlyCollection<string> RegisteredNames
    {
        get { lock (Gate) return ByName.Keys.ToArray(); }
    }
}
