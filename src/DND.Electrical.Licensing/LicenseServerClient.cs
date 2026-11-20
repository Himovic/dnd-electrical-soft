using System.Net.Http.Json;

namespace DND.Electrical.Licensing;

public sealed record ActivationResponse(SignedLicense License, string Message);
public sealed record LeaseResponse(SignedEnvelope Lease, string Message);
public sealed record ApiError(string Error);

/// <summary>Client for the D&amp;D licensing server (activation, refresh, deactivation, floating seats).</summary>
public sealed class LicenseServerClient(HttpClient http)
{
    private async Task<T> Post<T>(string path, object body, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync(path, body, LicenseJson.Options, ct);
        if (!r.IsSuccessStatusCode)
        {
            var err = await SafeError(r, ct);
            throw new LicenseException($"Licensing server: {err}");
        }
        return await r.Content.ReadFromJsonAsync<T>(LicenseJson.Options, ct) ?? throw new LicenseException("Licensing server returned an empty response.");
    }

    private static async Task<string> SafeError(HttpResponseMessage r, CancellationToken ct)
    {
        try { return (await r.Content.ReadFromJsonAsync<ApiError>(LicenseJson.Options, ct))?.Error ?? r.ReasonPhrase ?? "error"; }
        catch (System.Text.Json.JsonException) { return $"{(int)r.StatusCode} {r.ReasonPhrase}"; }
    }

    public Task<ActivationResponse> ActivateAsync(string licenseKey, string deviceId, string deviceName, string user, CancellationToken ct = default) =>
        Post<ActivationResponse>("api/v1/activate", new { licenseKey, deviceId, deviceName, user }, ct);

    public Task<ActivationResponse> RefreshAsync(string licenseId, string deviceId, CancellationToken ct = default) =>
        Post<ActivationResponse>("api/v1/refresh", new { licenseId, deviceId }, ct);

    public Task<ActivationResponse> DeactivateAsync(string licenseId, string deviceId, CancellationToken ct = default) =>
        Post<ActivationResponse>("api/v1/deactivate", new { licenseId, deviceId }, ct);

    public Task<LeaseResponse> CheckoutAsync(string licenseId, string deviceId, string user, CancellationToken ct = default) =>
        Post<LeaseResponse>("api/v1/floating/checkout", new { licenseId, deviceId, user }, ct);

    public Task<LeaseResponse> HeartbeatAsync(string leaseId, CancellationToken ct = default) =>
        Post<LeaseResponse>("api/v1/floating/heartbeat", new { leaseId }, ct);

    public Task<LeaseResponse> ReleaseAsync(string leaseId, CancellationToken ct = default) =>
        Post<LeaseResponse>("api/v1/floating/release", new { leaseId }, ct);

    public async Task<SignedEnvelope> RevocationsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<SignedEnvelope>("api/v1/revocations", LicenseJson.Options, ct) ?? throw new LicenseException("No revocation list.");
}

/// <summary>Issues licences (server side only: requires the private key).</summary>
public static class LicenseIssuer
{
    public static string NewLicenseId() => "LIC-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
}
