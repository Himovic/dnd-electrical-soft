using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DND.Electrical.CAD.Entities;
using DND.Electrical.CAD.Model;
using DND.Electrical.Core.Geometry;

namespace DND.Electrical.CAD.Serialization;

/// <summary>Central JSON configuration for CAD data (compact points, polymorphic entities).</summary>
public static class CadJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions(indented: false);
    public static readonly JsonSerializerOptions IndentedOptions = CreateOptions(indented: true);

    public static JsonSerializerOptions CreateOptions(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new Point2JsonConverter());
        o.Converters.Add(new CadColorJsonConverter());
        o.Converters.Add(new JsonStringEnumConverter());
        o.Converters.Add(new CadEntityJsonConverter());
        return o;
    }

    public static string SerializeDrawing(CadDrawing drawing, bool indented = false) =>
        JsonSerializer.Serialize(drawing, indented ? IndentedOptions : Options);

    public static CadDrawing DeserializeDrawing(string json) =>
        JsonSerializer.Deserialize<CadDrawing>(json, Options) ?? new CadDrawing();

    public static string SerializeEntities(IEnumerable<CadEntity> entities) =>
        JsonSerializer.Serialize(entities.ToList(), Options);

    public static List<CadEntity> DeserializeEntities(string json) =>
        JsonSerializer.Deserialize<List<CadEntity>>(json, Options) ?? new List<CadEntity>();
}

public sealed class Point2JsonConverter : JsonConverter<Point2>
{
    public override Point2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read(); var x = reader.GetDouble();
            reader.Read(); var y = reader.GetDouble();
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Point must be [x,y].");
            return new Point2(x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            double x = 0, y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "X", StringComparison.OrdinalIgnoreCase)) x = reader.GetDouble();
                else if (string.Equals(name, "Y", StringComparison.OrdinalIgnoreCase)) y = reader.GetDouble();
            }
            return new Point2(x, y);
        }
        throw new JsonException("Invalid point.");
    }

    public override void Write(Utf8JsonWriter writer, Point2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 6));
        writer.WriteNumberValue(Math.Round(value.Y, 6));
        writer.WriteEndArray();
    }
}

public sealed class CadColorJsonConverter : JsonConverter<CadColor>
{
    public override CadColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        CadColor.FromHex(reader.GetString() ?? "#000000");

    public override void Write(Utf8JsonWriter writer, CadColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}

/// <summary>Polymorphic converter writing a <c>$type</c> discriminator from <see cref="CadEntityTypeRegistry"/>.</summary>
public sealed class CadEntityJsonConverter : JsonConverter<CadEntity>
{
    private const string TypeProperty = "$type";

    public override CadEntity? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (!doc.RootElement.TryGetProperty(TypeProperty, out var t))
            throw new JsonException("Entity without $type discriminator.");
        var name = t.GetString() ?? string.Empty;
        var type = CadEntityTypeRegistry.Resolve(name)
                   ?? throw new JsonException($"Unknown entity type '{name}'. Is the module that defines it loaded?");
        return (CadEntity?)doc.RootElement.Deserialize(type, options);
    }

    public override void Write(Utf8JsonWriter writer, CadEntity value, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), options) as JsonObject
                   ?? throw new JsonException("Entity did not serialize to an object.");
        writer.WriteStartObject();
        writer.WriteString(TypeProperty, CadEntityTypeRegistry.GetName(value.GetType()));
        foreach (var kv in node)
        {
            writer.WritePropertyName(kv.Key);
            if (kv.Value is null) writer.WriteNullValue();
            else kv.Value.WriteTo(writer, options);
        }
        writer.WriteEndObject();
    }
}
