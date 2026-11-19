using DND.Electrical.Calculations;
using DND.Electrical.Documentation;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Symbols;
using DND.Electrical.Validation;

namespace DND.Electrical.Application.Services;

public sealed record AssistantSuggestion(string Title, string Detail, string? FixId, bool RequiresConfirmation = true);

public sealed record AssistantAnswer(string Text, IReadOnlyList<AssistantSuggestion> Suggestions, IReadOnlyList<string> Sources);

/// <summary>Pluggable assistant back-end (local knowledge or a configured AI provider).</summary>
public interface IAssistantProvider
{
    string Name { get; }
    Task<AssistantAnswer> AskAsync(string question, ElectricalProject? project, CancellationToken ct = default);
}

/// <summary>
/// Offline assistant answering from local knowledge: symbol library, help center, calculation profiles,
/// validation results and project statistics. It never modifies drawings: suggestions carry a fix id that the
/// UI executes only after user confirmation.
/// </summary>
public sealed class LocalAssistantProvider(SymbolLibraryService symbols, HelpLibrary? help, Func<ElectricalProject, ConnectivityGraph> graphFactory) : IAssistantProvider
{
    public string Name => "Local knowledge (offline)";

    public Task<AssistantAnswer> AskAsync(string question, ElectricalProject? project, CancellationToken ct = default)
    {
        var q = question.Trim();
        var lower = q.ToLowerInvariant();
        var sources = new List<string>();
        var suggestions = new List<AssistantSuggestion>();
        string text;

        if (project is not null && (lower.Contains("error") || lower.Contains("check") || lower.Contains("problem") || lower.Contains("erreur") || lower.Contains("valid")))
        {
            var report = new ValidationEngine().Run(project, graphFactory(project));
            text = report.Issues.Count == 0
                ? "Validation found no issues."
                : $"Validation found {report.Summary}. The most important:\n" + string.Join("\n", report.Issues.Take(8).Select(i => $"• [{i.Severity}] {i.Message} {i.Location} – {i.Suggestion}"));
            foreach (var fix in report.Issues.Where(i => i.FixId is not null).Select(i => i.FixId!).Distinct())
                suggestions.Add(new AssistantSuggestion(fix switch
                {
                    BuiltInRules.FixNumberWires => "Number wires automatically",
                    BuiltInRules.FixSyncTerminals => "Synchronise terminal strips",
                    BuiltInRules.FixRenumberDevices => "Renumber devices",
                    _ => fix,
                }, "Applies the correction after your confirmation; it can be undone.", fix));
            sources.Add("Validation engine");
        }
        else if (project is not null && (lower.Contains("summary") || lower.Contains("summar") || lower.Contains("résumé") || lower.Contains("overview")))
        {
            var g = graphFactory(project);
            text = $"Project {project.Info.Number} – {project.Info.Name}: {project.Sheets.Count} sheets, {project.Devices.Count} devices, {g.Wires.Count} wires in {g.Potentials.Count} potentials, " +
                   $"{project.Cables.Count} cables, {project.PlcStations.Sum(s => s.Channels.Count())} PLC channels ({project.PlcStations.Sum(s => s.Channels.Count(c => !c.IsSpare))} used), " +
                   $"{project.Motors.Count} motors ({project.Motors.Sum(m => m.PowerKw):0.#} kW), {project.Calculations.Count} stored calculations.";
            sources.Add("Project data");
        }
        else if (FindSymbol(lower) is { } sym)
        {
            text = $"{sym.Name} ({sym.Id}): {sym.Description}\nRole: {sym.Role}{(sym.ContactType != ContactType.None ? $", contact type {sym.ContactType}" : "")}. Default tag prefix: {sym.DefaultTagPrefix}. " +
                   $"Pins: {string.Join(", ", sym.Pins.Select(p => p.Label + (p.Description.Length > 0 ? $" ({p.Description})" : "")))}." +
                   (sym.InternalConnections.Count > 0 ? " Pins " + string.Join(" / ", sym.InternalConnections.Select(g => string.Join("=", g))) + " are internally connected." : "");
            sources.Add($"Symbol library: {sym.Id}");
        }
        else if (lower.Contains("voltage drop") || lower.Contains("chute de tension") || lower.Contains("cable siz") || lower.Contains("short") || lower.Contains("court-circuit"))
        {
            var profile = StandardProfile.Get(project?.Settings.StandardProfileId ?? "IEC");
            text = "Calculation methods used by the suite:\n" + string.Join("\n", StandardProfile.All.SelectMany(p => p.Clauses).Where(c => c.Status != ImplementationStatus.NotImplemented)
                .Where(c => lower.Contains("short") || lower.Contains("court") ? c.Reference.Contains("60909") : !c.Reference.Contains("60909"))
                .Select(c => $"• {c.Reference}: {c.Topic} ({c.Status}) – {c.Notes}")) + $"\nActive profile: {profile.Name}. Results are preliminary and must be verified.";
            sources.Add("Calculation standard profiles");
        }
        else
        {
            var hits = help?.Search(q, 3) ?? Array.Empty<HelpSearchHit>();
            text = hits.Count > 0
                ? string.Join("\n\n", hits.Select(h => $"{h.Article.Title}: {h.Snippet}"))
                : "I could not find an answer in the local knowledge base. Try the Help Center (F1) or rephrase the question.";
            sources.AddRange(hits.Select(h => "Help: " + h.Article.Title));
        }
        return Task.FromResult(new AssistantAnswer(text, suggestions, sources));
    }

    private SymbolDefinition? FindSymbol(string lower)
    {
        if (!(lower.Contains("symbol") || lower.Contains("what is") || lower.Contains("explain") || lower.Contains("qu'est"))) return null;
        return symbols.AllSymbols
            .Select(s => (s, score: Score(s, lower)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.s)
            .FirstOrDefault();
    }

    private static int Score(SymbolDefinition s, string q) =>
                // Longer (more specific) keyword matches weigh more: "emergency stop" beats "stop".
        (q.Contains(s.Name.ToLowerInvariant()) ? 40 : 0) + s.Keywords.Where(k => q.Contains(k.ToLowerInvariant())).Sum(k => k.Length) + (q.Contains(s.Id.ToLowerInvariant()) ? 40 : 0);
}
