using System.Globalization;

namespace DND.Electrical.CAD.Model;

/// <summary>RGB colour used by CAD entities and layers.</summary>
public readonly record struct CadColor(byte R, byte G, byte B)
{
    public static readonly CadColor Black = new(0, 0, 0);
    public static readonly CadColor White = new(255, 255, 255);
    public static readonly CadColor Red = new(220, 38, 38);
    public static readonly CadColor Green = new(22, 163, 74);
    public static readonly CadColor Blue = new(37, 99, 235);
    public static readonly CadColor Navy = new(23, 43, 77);
    public static readonly CadColor Grey = new(100, 116, 139);
    public static readonly CadColor Orange = new(234, 88, 12);
    public static readonly CadColor Brown = new(120, 72, 32);
    public static readonly CadColor Yellow = new(202, 138, 4);
    public static readonly CadColor Magenta = new(192, 38, 211);
    public static readonly CadColor Cyan = new(8, 145, 178);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static CadColor FromHex(string hex)
    {
        if (!TryParse(hex, out var c)) throw new FormatException($"Invalid colour '{hex}'.");
        return c;
    }

    public static bool TryParse(string? hex, out CadColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new CadColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public override string ToString() => ToHex();

    // --- AutoCAD Color Index (ACI) mapping for DXF interchange (standard colours 1-9 + greys) ---
    private static readonly (int aci, CadColor color)[] AciTable =
    {
        (1, new CadColor(255, 0, 0)), (2, new CadColor(255, 255, 0)), (3, new CadColor(0, 255, 0)),
        (4, new CadColor(0, 255, 255)), (5, new CadColor(0, 0, 255)), (6, new CadColor(255, 0, 255)),
        (7, new CadColor(0, 0, 0)), (8, new CadColor(128, 128, 128)), (9, new CadColor(192, 192, 192)),
        (30, new CadColor(255, 127, 0)), (250, new CadColor(51, 51, 51)), (252, new CadColor(130, 130, 130)),
    };

    /// <summary>Nearest ACI index (used as fallback alongside true colour group code 420).</summary>
    public int ToAci()
    {
        var best = 7;
        var bestD = double.MaxValue;
        foreach (var (aci, c) in AciTable)
        {
            double dr = R - c.R, dg = G - c.G, db = B - c.B;
            var d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = aci; }
        }
        return best;
    }

    public static CadColor FromAci(int aci)
    {
        foreach (var (a, c) in AciTable) if (a == aci) return c;
        return Black;
    }

    public int ToTrueColor() => (R << 16) | (G << 8) | B;
    public static CadColor FromTrueColor(int v) => new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
}
