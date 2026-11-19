using System.Text.Json;
using DND.Electrical.Calculations;
using DND.Electrical.Domain;

namespace DND.Electrical.Application.Services;

/// <summary>Stores calculation results in the project with complete traceability (inputs, steps, checks, report).</summary>
public static class CalculationService
{
    private sealed record Stored(
        Dictionary<string, string> Outputs,
        List<CalculationStep> Steps,
        List<string> Checks,
        List<string> Assumptions,
        List<string> Warnings,
        string Method,
        string Report);

    public static CalculationRecord Store(ElectricalProject project, CalculationResult result, string? title = null, Guid? linkedObject = null, string? author = null)
    {
        var record = new CalculationRecord
        {
            CalculationType = result.CalculationType,
            Title = title ?? result.Title,
            Author = author ?? Environment.UserName,
            StandardProfileId = result.StandardProfile,
            InputsJson = JsonSerializer.Serialize(result.Inputs.ToDictionary(i => i.Name, i => i.Value.ToString())),
            ResultJson = JsonSerializer.Serialize(new Stored(
                result.Outputs.ToDictionary(o => o.Name, o => o.Value.ToString()), result.Steps, result.Checks, result.Assumptions, result.Warnings,
                result.Method, result.ToReportText())),
            Summary = result.Summary,
            Status = result.Status switch
            {
                ResultStatus.Pass => CalculationStatus.Pass,
                ResultStatus.Warning => CalculationStatus.Warning,
                ResultStatus.Fail => CalculationStatus.Fail,
                _ => CalculationStatus.Informational,
            },
            LinkedObjectId = linkedObject,
        };
        project.Calculations.Add(record);
        project.Audit("Calculation", $"{record.Title}: {record.Summary}", author);
        return record;
    }

    /// <summary>The full human-readable calculation report stored with the record.</summary>
    public static string ReportText(CalculationRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<Stored>(record.ResultJson)?.Report ?? record.Summary;
        }
        catch (JsonException) { return record.Summary; }
    }

    public static IReadOnlyDictionary<string, string> Inputs(CalculationRecord record)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(record.InputsJson) ?? new(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }
}
