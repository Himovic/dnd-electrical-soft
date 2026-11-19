using System.Text.RegularExpressions;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Schematic.Services;

/// <summary>Replaces <c>{FIELD}</c> placeholders in title blocks and texts with project/sheet data.</summary>
public static partial class FieldResolver
{
    [GeneratedRegex(@"\{([A-Z0-9_.]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex FieldPattern();

    public static IReadOnlyDictionary<string, string> BuildFields(ElectricalProject project, Sheet? sheet)
    {
        var i = project.Info;
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PROJECT.NAME"] = i.Name,
            ["PROJECT.NUMBER"] = i.Number,
            ["PROJECT.DESCRIPTION"] = i.Description,
            ["PROJECT.REFERENCE"] = i.Reference,
            ["PROJECT.SITE"] = i.Site,
            ["PROJECT.STATUS"] = i.Status.ToString(),
            ["PROJECT.REVISION"] = i.CurrentRevision,
            ["CUSTOMER.NAME"] = i.CustomerName,
            ["CUSTOMER.ADDRESS"] = i.CustomerAddress,
            ["COMPANY.NAME"] = i.CompanyName,
            ["COMPANY.ADDRESS"] = i.CompanyAddress,
            ["ENGINEER"] = i.Engineer,
            ["CHECKED"] = i.CheckedBy,
            ["APPROVED"] = i.ApprovedBy,
            ["DATE"] = i.Modified.ToString(project.Settings.DateFormat),
            ["PRODUCT"] = ProductInfo.ProductName,
            ["POWEREDBY"] = ProductInfo.PoweredBy,
            ["COPYRIGHT"] = ProductInfo.Copyright,
            ["SHEET.COUNT"] = project.Sheets.Count.ToString(),
            ["DEMO.BANNER"] = i.IsDemo ? "DEMONSTRATION PROJECT – FICTIONAL DATA – NOT FOR CONSTRUCTION" : string.Empty,
        };
        if (sheet is not null)
        {
            f["SHEET.NUMBER"] = sheet.Number.ToString();
            f["SHEET.TITLE"] = sheet.Title;
            f["SHEET.DESCRIPTION"] = sheet.Description;
            f["SHEET.REVISION"] = sheet.Revision;
            f["SHEET.FUNCTION"] = sheet.Function;
            f["SHEET.LOCATION"] = sheet.Location;
            foreach (var kv in sheet.Fields) f["SHEET." + kv.Key.ToUpperInvariant()] = kv.Value;
        }
        return f;
    }

    public static string Resolve(string text, IReadOnlyDictionary<string, string> fields) =>
        text.IndexOf('{') < 0 ? text : FieldPattern().Replace(text, m => fields.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value).Trim();

    public static Func<string, string> CreateResolver(ElectricalProject project, Sheet? sheet)
    {
        var fields = BuildFields(project, sheet);
        return s => Resolve(s, fields);
    }
}
