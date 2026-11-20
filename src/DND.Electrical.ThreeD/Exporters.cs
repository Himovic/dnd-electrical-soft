using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DND.Electrical.ThreeD;

/// <summary>
/// glTF 2.0 writer (.gltf with embedded buffer or binary .glb). The scene root converts the engineering convention
/// (Z up, millimetres) to glTF (Y up, metres). Each object is a node with its engineering identity in <c>extras</c>.
/// </summary>
public static class GltfExporter
{
    public static void Write(Scene3D scene, Stream output, bool binary, IReadOnlyDictionary<Guid, Vec3>? positionOverride = null)
    {
        var bin = new MemoryStream();
        var bw = new BinaryWriter(bin);
        var accessors = new JsonArray(); var views = new JsonArray(); var meshes = new JsonArray(); var nodes = new JsonArray(); var materials = new JsonArray();
        var materialIndex = new Dictionary<(string, double), int>();
        var children = new JsonArray();

        int View(long offset, long length, int? target)
        {
            var v = new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = length };
            if (target is { } tg) v["target"] = tg;
            views.Add(v);
            return views.Count - 1;
        }
        void Align() { while (bin.Length % 4 != 0) bw.Write((byte)0); }

        int Material(string color, double opacity)
        {
            var key = (color, Math.Round(opacity, 3));
            if (materialIndex.TryGetValue(key, out var idx)) return idx;
            var (r, g, b) = ParseColor(color);
            var m = new JsonObject
            {
                ["name"] = $"{color} {opacity:0.##}",
                ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = new JsonArray(r, g, b, Math.Clamp(opacity, 0, 1)), ["metallicFactor"] = 0.1, ["roughnessFactor"] = 0.7 },
                ["doubleSided"] = opacity < 1,
            };
            if (opacity < 1) m["alphaMode"] = "BLEND";
            materials.Add(m);
            materialIndex[key] = materials.Count - 1;
            return materials.Count - 1;
        }

        foreach (var o in scene.Objects.Concat(scene.TraySegments()).Where(o => o.Visible))
        {
            var mesh = o.LocalMesh();
            if (mesh.TriangleCount == 0) continue;
            Align();
            var posOffset = bin.Length;
            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            foreach (var p in mesh.Positions)
            {
                var f = new[] { (float)p.X, (float)p.Y, (float)p.Z };
                for (var k = 0; k < 3; k++) { bw.Write(f[k]); min[k] = Math.Min(min[k], f[k]); max[k] = Math.Max(max[k], f[k]); }
            }
            var posView = View(posOffset, bin.Length - posOffset, 34962);
            var nOffset = bin.Length;
            foreach (var n in mesh.Normals) { var u = n.Normalized(); bw.Write((float)u.X); bw.Write((float)u.Y); bw.Write((float)u.Z); }
            var nView = View(nOffset, bin.Length - nOffset, 34962);
            var iOffset = bin.Length;
            foreach (var i in mesh.Indices) bw.Write((uint)i);
            var iView = View(iOffset, bin.Length - iOffset, 34963);

            accessors.Add(new JsonObject { ["bufferView"] = posView, ["componentType"] = 5126, ["count"] = mesh.Positions.Count, ["type"] = "VEC3", ["min"] = new JsonArray(min[0], min[1], min[2]), ["max"] = new JsonArray(max[0], max[1], max[2]) });
            var posAcc = accessors.Count - 1;
            accessors.Add(new JsonObject { ["bufferView"] = nView, ["componentType"] = 5126, ["count"] = mesh.Normals.Count, ["type"] = "VEC3" });
            var nAcc = accessors.Count - 1;
            accessors.Add(new JsonObject { ["bufferView"] = iView, ["componentType"] = 5125, ["count"] = mesh.Indices.Count, ["type"] = "SCALAR" });
            var iAcc = accessors.Count - 1;

            meshes.Add(new JsonObject
            {
                ["name"] = o.Name,
                ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = posAcc, ["NORMAL"] = nAcc }, ["indices"] = iAcc, ["material"] = Material(o.Color, o.Opacity), ["mode"] = 4 }),
            });
            var pos = positionOverride is not null && positionOverride.TryGetValue(o.Id, out var ov) ? ov : o.Position;
            var q = o.Rotation.ToQuaternion();
            var node = new JsonObject
            {
                ["name"] = string.IsNullOrEmpty(o.Tag) ? o.Name : $"{o.Tag} {o.Name}",
                ["mesh"] = meshes.Count - 1,
                ["translation"] = new JsonArray(pos.X, pos.Y, pos.Z),
                ["extras"] = new JsonObject
                {
                    ["dndObjectId"] = o.ObjectId?.ToString(), ["dndSceneObjectId"] = o.Id.ToString(), ["tag"] = o.Tag, ["kind"] = o.Kind.ToString(), ["source"] = o.Source,
                },
            };
            if (Math.Abs(q.w - 1) > 1e-12) node["rotation"] = new JsonArray(q.x, q.y, q.z, q.w);
            nodes.Add(node);
            children.Add(nodes.Count - 1);
        }

        // Root: Z-up millimetres → Y-up metres (rotate −90° about X, scale 0.001).
        var s = Math.Sqrt(0.5);
        nodes.Add(new JsonObject { ["name"] = scene.Name, ["children"] = children, ["rotation"] = new JsonArray(-s, 0.0, 0.0, s), ["scale"] = new JsonArray(0.001, 0.001, 0.001) });
        var root = nodes.Count - 1;
        Align();
        var bytes = bin.ToArray();

        var buffer = new JsonObject { ["byteLength"] = bytes.Length };
        if (!binary) buffer["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = $"{DND.Electrical.Shared.ProductInfo.ShortName} {DND.Electrical.Shared.ProductInfo.Version}", ["copyright"] = DND.Electrical.Shared.ProductInfo.Copyright },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["name"] = scene.Name, ["nodes"] = new JsonArray(root) }),
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JsonArray(buffer),
        };
        if (meshes.Count == 0) { gltf.Remove("meshes"); gltf.Remove("accessors"); gltf.Remove("bufferViews"); gltf.Remove("buffers"); gltf.Remove("materials"); }
        var json = Encoding.UTF8.GetBytes(gltf.ToJsonString(new JsonSerializerOptions { WriteIndented = !binary }));
        if (!binary) { output.Write(json); return; }

        var jsonPadded = Pad(json, 0x20);
        var binPadded = Pad(bytes, 0);
        var hasBin = meshes.Count > 0;
        var total = 12 + 8 + jsonPadded.Length + (hasBin ? 8 + binPadded.Length : 0);
        var w = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)total);
        w.Write((uint)jsonPadded.Length); w.Write(0x4E4F534Au); w.Write(jsonPadded);
        if (hasBin) { w.Write((uint)binPadded.Length); w.Write(0x004E4942u); w.Write(binPadded); }
        w.Flush();
    }

    private static byte[] Pad(byte[] data, byte fill)
    {
        var len = (data.Length + 3) & ~3;
        if (len == data.Length) return data;
        var r = new byte[len]; Array.Copy(data, r, data.Length);
        for (var i = data.Length; i < len; i++) r[i] = fill;
        return r;
    }

    public static (double r, double g, double b) ParseColor(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return (0.6, 0.6, 0.6);
        // sRGB → linear (glTF baseColorFactor is linear).
        static double Lin(int c) { var s = c / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return (Math.Round(Lin((v >> 16) & 255), 4), Math.Round(Lin((v >> 8) & 255), 4), Math.Round(Lin(v & 255), 4));
    }
}

/// <summary>Binary STL writer (world coordinates, millimetres).</summary>
public static class StlExporter
{
    public static void Write(Scene3D scene, Stream output)
    {
        var tris = scene.Objects.Concat(scene.TraySegments()).Where(o => o.Visible).SelectMany(o => o.WorldMesh().Triangles()).ToList();
        var w = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        var header = new byte[80];
        Encoding.ASCII.GetBytes("D&D Engineering Suite STL (mm)").CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)tris.Count);
        foreach (var (a, b, c) in tris)
        {
            var n = (b - a).Cross(c - a).Normalized();
            foreach (var v in new[] { n, a, b, c }) { w.Write((float)v.X); w.Write((float)v.Y); w.Write((float)v.Z); }
            w.Write((ushort)0);
        }
        w.Flush();
    }
}
