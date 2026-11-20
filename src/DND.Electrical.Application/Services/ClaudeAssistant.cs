using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Validation;

namespace DND.Electrical.Application.Services;

/// <summary>
/// Assistant backed by Claude through the official Anthropic SDK. Answers come back as structured JSON
/// (answer + proposals); a proposal can only reference a correction the application already implements
/// (number wires, synchronise terminals, renumber devices) and is executed by the UI after user confirmation.
/// A project summary is sent only when the user has enabled data sharing.
/// </summary>
public sealed class ClaudeAssistantProvider(Func<string?> apiKey, Func<UserSettings> settings, Func<ElectricalProject, ConnectivityGraph> graphFactory, HttpClient? httpClient = null) : IAssistantProvider
{
    public const string SecretName = "anthropic-api-key";
    private static readonly string[] KnownActions = { BuiltInRules.FixNumberWires, BuiltInRules.FixSyncTerminals, BuiltInRules.FixRenumberDevices };

    public string Name => $"Claude ({settings().AssistantModel})";
    /// <summary>Encrypted local store holding the API key (set from the assistant settings).</summary>
    public ISecretStore? KeyStore { get; init; }
    public string Model => settings().AssistantModel;
    public bool SharesProjectData => settings().AssistantShareProjectData;

    private const string Instructions =
        "You assist electrical and automation engineers using the D&D Engineering Suite. Answer concisely and technically. " +
        "Never claim certification or standards compliance; recommend verification by a qualified engineer for design decisions. " +
        "Do not invent manufacturer data or calculation results that are not in the provided project summary. " +
        "You may propose corrections the application can perform: 'number-wires' (number unnumbered wires), " +
        "'sync-terminals' (create missing terminal strip definitions), 'renumber-devices' (renumber unlocked device tags). " +
        "Use action 'none' for advice the engineer must carry out manually. The engineer confirms every proposal before it runs.";

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            answer = new { type = "string" },
            proposals = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        detail = new { type = "string" },
                        action = new { type = "string", @enum = new[] { "none", BuiltInRules.FixNumberWires, BuiltInRules.FixSyncTerminals, BuiltInRules.FixRenumberDevices } },
                    },
                    required = new[] { "title", "detail", "action" },
                    additionalProperties = false,
                },
            },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "answer", "proposals" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    public async Task<AssistantAnswer> AskAsync(string question, ElectricalProject? project, CancellationToken ct = default)
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
            return new AssistantAnswer("Claude is selected as assistant provider but no API key is configured (Settings → Assistant). Nothing was sent.", Array.Empty<AssistantSuggestion>(), Array.Empty<string>());
        var s = settings();
        var prompt = new StringBuilder(question.Trim());
        prompt.AppendLine().AppendLine();
        if (s.AssistantShareProjectData && project is not null) prompt.Append(Summary(project));
        else prompt.Append("(Project data is not shared with the assistant; answer from general engineering knowledge.)");

        var client = httpClient is null ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = key, HttpClient = httpClient };
        var response = await client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = s.AssistantModel,
            MaxTokens = 16000,
            // A policy decline is re-served by an appropriate fallback model inside the same call.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            System = Instructions,
            Messages = [new BetaUserMessageParam { Content = prompt.ToString() }],
            OutputConfig = new BetaOutputConfig { Format = new BetaJsonOutputFormat { Schema = Schema } },
        }, ct).ConfigureAwait(false);

        var sources = new List<string> { $"Claude ({s.AssistantModel})", s.AssistantShareProjectData && project is not null ? "project summary shared" : "no project data shared" };
        if (response.StopReason == "refusal")
            return new AssistantAnswer("The request was declined by the AI provider. Rephrase the question or use the local assistant.", Array.Empty<AssistantSuggestion>(), sources);
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        if (response.StopReason == "max_tokens" || text.Length == 0)
            return new AssistantAnswer("The AI response was incomplete; no proposal was accepted.", Array.Empty<AssistantSuggestion>(), sources);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var answer = doc.RootElement.GetProperty("answer").GetString() ?? string.Empty;
            var proposals = new List<AssistantSuggestion>();
            foreach (var p in doc.RootElement.GetProperty("proposals").EnumerateArray())
            {
                var action = p.GetProperty("action").GetString();
                proposals.Add(new AssistantSuggestion(p.GetProperty("title").GetString() ?? "", p.GetProperty("detail").GetString() ?? "",
                    KnownActions.Contains(action) ? action : null, RequiresConfirmation: true));
            }
            return new AssistantAnswer(answer, proposals, sources);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new AssistantAnswer("The AI response could not be interpreted; no proposal was accepted.", Array.Empty<AssistantSuggestion>(), sources);
        }
    }

    /// <summary>Compact project summary (no customer data): counts, devices, validation findings.</summary>
    public string Summary(ElectricalProject p)
    {
        var g = graphFactory(p);
        var report = new ValidationEngine().Run(p, g);
        var sb = new StringBuilder();
        sb.AppendLine($"Project summary: industry {p.Info.Industry}, {p.Settings.NominalVoltage:0} V {p.Settings.EarthingSystem}, standard profile {p.Settings.StandardProfileId}.");
        sb.AppendLine($"{p.Sheets.Count} sheets, {p.Devices.Count} devices, {g.Wires.Count} wires, {p.Cables.Count} cables, {p.Motors.Count} motors, {p.Instruments.Count} instruments, {p.Loads.Count} loads.");
        sb.AppendLine("Devices: " + string.Join(", ", p.Devices.Take(150).Select(d => string.IsNullOrEmpty(d.Function) ? d.Tag : $"{d.Tag} ({d.Function})")));
        sb.AppendLine($"Validation: {report.Summary}");
        foreach (var i in report.Issues.Where(i => i.Severity != Severity.Information).Take(30)) sb.AppendLine($"- [{i.Severity}] {i.RuleId} {i.Message}");
        return sb.ToString();
    }
}

/// <summary>Routes questions to the provider selected in the user settings (local knowledge or Claude).</summary>
public sealed class AssistantRouter(IAssistantProvider local, ClaudeAssistantProvider claude, Func<UserSettings> settings) : IAssistantProvider
{
    public ClaudeAssistantProvider Claude => claude;
    public IAssistantProvider Current => settings().AssistantProvider == "claude" ? claude : local;
    public string Name => Current.Name;
    public Task<AssistantAnswer> AskAsync(string question, ElectricalProject? project, CancellationToken ct = default) => Current.AskAsync(question, project, ct);
}

/// <summary>Audit trail of AI interactions and of the decisions taken on their proposals.</summary>
public static class AiAudit
{
    public static AiInteractionRecord Record(ElectricalProject p, IAssistantProvider provider, string question, AssistantAnswer answer, string user)
    {
        var current = provider is AssistantRouter r ? r.Current : provider;
        var rec = new AiInteractionRecord
        {
            User = user, Provider = current.Name, Model = current is ClaudeAssistantProvider c ? c.Model : "local",
            ProjectDataShared = current is ClaudeAssistantProvider cl && cl.SharesProjectData,
            Question = question, AnswerExcerpt = answer.Text.Length > 500 ? answer.Text[..500] + "…" : answer.Text,
            Proposals = string.Join("\n", answer.Suggestions.Select(s => s.Title)),
        };
        p.AiInteractions.Add(rec);
        return rec;
    }

    public static void Decide(ElectricalProject p, AiInteractionRecord rec, AssistantSuggestion s, bool accepted, string user)
    {
        rec.Decisions += (rec.Decisions.Length > 0 ? "\n" : "") + $"{s.Title} → {(accepted ? "Accepted" : "Rejected")}";
        p.Audit(accepted ? "AI proposal accepted" : "AI proposal rejected", $"{rec.Provider}: {s.Title}", user);
    }
}
