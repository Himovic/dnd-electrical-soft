using System.Net;
using System.Text;
using System.Text.Json;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Application.Services;

namespace DND.Electrical.IntegrationTests;

/// <summary>Captures requests and returns a canned Messages API response (no network).</summary>
internal sealed class FakeClaudeHandler(Func<JsonElement, string> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage request, JsonElement body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        Requests.Add((request, json));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(json), Encoding.UTF8, "application/json") };
    }

    public static string Message(string text, string stopReason = "end_turn") => JsonSerializer.Serialize(new
    {
        id = "msg_test", type = "message", role = "assistant", model = "claude-opus-5-5",
        content = new object[] { new { type = "text", text } },
        stop_reason = stopReason, stop_sequence = (string?)null,
        usage = new { input_tokens = 100, output_tokens = 50 },
    });
}

public class AiAssistantTests
{
    private static (ClaudeAssistantProvider provider, FakeClaudeHandler handler, UserSettings settings) Create(TestEnvironment env, Func<JsonElement, string> respond, string? key = "test-key")
    {
        var settings = new UserSettings { AssistantProvider = "claude" };
        var handler = new FakeClaudeHandler(respond);
        var provider = new ClaudeAssistantProvider(() => key, () => settings, p => env.Schematic.Binder.BindWithConnectivity(p), new HttpClient(handler));
        return (provider, handler, settings);
    }

    private const string Reply = """{"answer":"Wire numbers are missing on 3 wires.","proposals":[{"title":"Number wires","detail":"Apply the project numbering format.","action":"number-wires"},{"title":"Check cable rating","detail":"Verify -W3 against the load.","action":"none"},{"title":"Delete sheet","detail":"x","action":"delete-everything"}]}""";

    [Fact]
    public async Task Request_uses_the_configured_model_structured_output_and_fallbacks_without_project_data_by_default()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var (provider, handler, _) = Create(env, _ => FakeClaudeHandler.Message(Reply));
        var answer = await provider.AskAsync("Are wires numbered?", p);

        var (req, body) = Assert.Single(handler.Requests);
        Assert.EndsWith("/v1/messages", req.RequestUri!.AbsolutePath);
        // The SDK chooses the credential header for the API version it targets (x-api-key or Authorization: Bearer).
        var auth = req.Headers.TryGetValues("x-api-key", out var k) ? k.Single() : req.Headers.Authorization?.Parameter;
        Assert.Equal("test-key", auth);
        Assert.Contains("server-side-fallback-2026-07-01", string.Join(",", req.Headers.GetValues("anthropic-beta")));
        Assert.Equal("claude-opus-5-5", body.GetProperty("model").GetString());
        Assert.Equal("default", body.GetProperty("fallbacks").GetString());
        Assert.Equal("json_schema", body.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        var userText = body.GetProperty("messages")[0].GetProperty("content").ToString();
        Assert.Contains("not shared", userText);
        Assert.DoesNotContain("-M1", userText);

        Assert.Equal("Wire numbers are missing on 3 wires.", answer.Text);
        Assert.Equal(3, answer.Suggestions.Count);
        Assert.Equal("number-wires", answer.Suggestions[0].FixId);
        Assert.Null(answer.Suggestions[1].FixId);
        Assert.Null(answer.Suggestions[2].FixId); // unknown actions are never executable
        Assert.All(answer.Suggestions, s => Assert.True(s.RequiresConfirmation));
    }

    [Fact]
    public async Task Project_summary_is_sent_only_when_sharing_is_enabled()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var (provider, handler, settings) = Create(env, _ => FakeClaudeHandler.Message(Reply));
        settings.AssistantShareProjectData = true;
        settings.AssistantModel = "claude-sonnet-5-5";
        var answer = await provider.AskAsync("Check my project", p);
        var body = handler.Requests.Single().body;
        Assert.Equal("claude-sonnet-5-5", body.GetProperty("model").GetString());
        var text = body.GetProperty("messages")[0].GetProperty("content").ToString();
        Assert.Contains("Project summary", text);
        Assert.Contains("Validation:", text);
        Assert.DoesNotContain(p.Info.CustomerName, text);
        Assert.Contains("project summary shared", answer.Sources);
    }

    [Fact]
    public async Task Refusals_incomplete_and_malformed_responses_yield_no_proposals()
    {
        using var env = new TestEnvironment();
        foreach (var (reply, expected) in new[]
        {
            (FakeClaudeHandler.Message("", "refusal"), "declined"),
            (FakeClaudeHandler.Message("{\"answer\":\"cut", "max_tokens"), "incomplete"),
            (FakeClaudeHandler.Message("not json"), "could not be interpreted"),
        })
        {
            var (provider, _, _) = Create(env, _ => reply);
            var a = await provider.AskAsync("q", null);
            Assert.Contains(expected, a.Text);
            Assert.Empty(a.Suggestions);
        }
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_sent()
    {
        using var env = new TestEnvironment();
        var (provider, handler, _) = Create(env, _ => FakeClaudeHandler.Message(Reply), key: null);
        var a = await provider.AskAsync("q", env.Demos.Create("DEMO-IND-001"));
        Assert.Contains("no API key", a.Text);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Router_follows_settings_and_audit_records_decisions()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var (claude, handler, settings) = Create(env, _ => FakeClaudeHandler.Message(Reply));
        var local = new LocalAssistantProvider(TestEnvironment.Library, null, x => env.Schematic.Binder.BindWithConnectivity(x));
        var router = new AssistantRouter(local, claude, () => settings);
        settings.AssistantProvider = "local";
        Assert.Equal(local.Name, router.Name);
        await router.AskAsync("check the project", p);
        Assert.Empty(handler.Requests);

        settings.AssistantProvider = "claude";
        var a = await router.AskAsync("check", p);
        var rec = AiAudit.Record(p, router, "check", a, "tester");
        Assert.Equal("claude-opus-5-5", rec.Model);
        Assert.False(rec.ProjectDataShared);
        AiAudit.Decide(p, rec, a.Suggestions[0], true, "tester");
        AiAudit.Decide(p, rec, a.Suggestions[1], false, "tester");
        Assert.Contains("Number wires → Accepted", rec.Decisions);
        Assert.Contains(p.AuditEvents, e => e.Action == "AI proposal rejected");

        var path = env.PathFor("ai.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);
        Assert.Equal(rec.Decisions, Assert.Single(l.AiInteractions).Decisions);
    }
}
