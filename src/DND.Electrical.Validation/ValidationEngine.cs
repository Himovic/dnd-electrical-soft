using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Services;

namespace DND.Electrical.Validation;

public enum Severity { Error, Warning, Information }

/// <summary>A finding. <see cref="FixId"/> names an optional corrective command that the UI may offer (never applied automatically).</summary>
public sealed record ValidationIssue(
    string RuleId,
    Severity Severity,
    string Message,
    string Suggestion,
    Guid? SheetId = null,
    Guid? EntityId = null,
    string Location = "",
    string ObjectRef = "",
    string? FixId = null);

public sealed class ValidationContext
{
    public ValidationContext(ElectricalProject project, ConnectivityGraph graph)
    {
        Project = project;
        Graph = graph;
    }

    public ElectricalProject Project { get; }
    public ConnectivityGraph Graph { get; }

    public string Loc(Sheet sheet, DND.Electrical.Core.Geometry.Point2 p) => SheetLocation.Of(sheet, p, Project.Settings).ToString();
}

public interface IValidationRule
{
    string Id { get; }
    string Title { get; }
    string Category { get; }
    string Description { get; }
    Severity DefaultSeverity { get; }
    IEnumerable<ValidationIssue> Check(ValidationContext context);
}

public sealed class ValidationReport
{
    public DateTimeOffset RunAt { get; init; } = DateTimeOffset.Now;
    public List<ValidationIssue> Issues { get; } = new();
    public int Errors => Issues.Count(i => i.Severity == Severity.Error);
    public int Warnings => Issues.Count(i => i.Severity == Severity.Warning);
    public int Infos => Issues.Count(i => i.Severity == Severity.Information);
    public bool HasErrors => Errors > 0;
    public string Summary => $"{Errors} error(s), {Warnings} warning(s), {Infos} information message(s)";
}

/// <summary>Runs the configured validation rules over a project. Read-only: never modifies the design.</summary>
public sealed class ValidationEngine
{
    public ValidationEngine(IEnumerable<IValidationRule>? rules = null) => Rules = (rules ?? BuiltInRules.All).ToList();

    public IReadOnlyList<IValidationRule> Rules { get; }

    public ValidationReport Run(ElectricalProject project, ConnectivityGraph graph)
    {
        var ctx = new ValidationContext(project, graph);
        var report = new ValidationReport();
        foreach (var rule in Rules)
        {
            var setting = project.Settings.ValidationRules.GetValueOrDefault(rule.Id);
            if (setting is { Enabled: false }) continue;
            Severity? overrideSeverity = setting?.SeverityOverride is { } s && Enum.TryParse<Severity>(s, true, out var sev) ? sev : null;
            try
            {
                foreach (var issue in rule.Check(ctx))
                    report.Issues.Add(overrideSeverity is { } o ? issue with { Severity = o } : issue);
            }
            catch (Exception ex)
            {
                report.Issues.Add(new ValidationIssue(rule.Id, Severity.Error, $"Rule '{rule.Title}' failed: {ex.Message}", "Report this problem to support."));
            }
        }
        report.Issues.Sort((a, b) => a.Severity != b.Severity ? a.Severity.CompareTo(b.Severity) : string.CompareOrdinal(a.RuleId, b.RuleId));
        return report;
    }

    /// <summary>Stores a summary of the run in the project's validation history.</summary>
    public static void RecordRun(ElectricalProject project, ValidationReport report)
    {
        project.ValidationRuns.Add(new ValidationRunRecord
        {
            Timestamp = report.RunAt, Errors = report.Errors, Warnings = report.Warnings, Infos = report.Infos, Summary = report.Summary,
        });
        if (project.ValidationRuns.Count > 200) project.ValidationRuns.RemoveAt(0);
    }
}
