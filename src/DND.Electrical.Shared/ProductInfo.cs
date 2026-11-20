using System.Reflection;

namespace DND.Electrical.Shared;

/// <summary>
/// Official product identity. Every user-facing surface (splash, About, reports, PDFs,
/// installer, manuals) must read the identity from here so that it stays consistent.
/// </summary>
public static class ProductInfo
{
    public const string ProductName = "D&D Electrical & Automation Engineering Suite";
    public const string ShortName = "D&D Engineering Suite";
    public const string ProductId = "DND-EAES";
    public const string Company = "D&D | Data&Development";
    public const string Tagline = "One Platform. Multiple Engineering Disciplines. Unlimited Possibilities.";
    public const string Positioning = "A unified engineering platform for electrical design, industrial automation, power systems, infrastructure, 3D engineering, BIM, and digital transformation.";
    public const string Motto = "One engineering object. Multiple engineering representations. One connected lifecycle.";
    public const string Author = "D&D | Data&Development";
    public const string Developer = "D&D | Data&Development";
    public const string Copyright = "© 2026 D&D | Data&Development. All rights reserved.";
    public const string PoweredBy = "Powered by D&D | Data&Development";
    public const string ProjectFileExtension = ".dndx";
    public const string ProjectArchiveExtension = ".dndpkg";
    public const string SymbolLibraryExtension = ".dndsym.json";

    /// <summary>Current file format version of project databases (schema generation).</summary>
    public const int ProjectFormatVersion = 1;

    public static Version Version =>
        typeof(ProductInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string VersionString
    {
        get
        {
            var info = typeof(ProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info)) return Version.ToString(3);
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    public static string FullTitle => $"{ProductName} {VersionString}";

    /// <summary>Text block used in generated documents and the About window.</summary>
    public static string AttributionBlock =>
        $"{ProductName}\nDeveloped by {Developer}\n{Copyright}";
}
