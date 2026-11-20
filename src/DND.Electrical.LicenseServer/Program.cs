using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using DND.Electrical.Licensing;
using DND.Electrical.LicenseServer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration.GetSection("Licensing");
builder.Services.AddDbContext<LicensingDb>(o => o.UseSqlite(cfg["Database"] ?? "Data Source=dnd-licensing.db"));
builder.Services.AddSingleton(_ => SigningKey.Load(cfg["SigningKeyPath"], cfg.GetValue<bool>("GenerateKeyIfMissing")));
builder.Services.AddScoped<LicensingService>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("client", l => { l.PermitLimit = 60; l.Window = TimeSpan.FromMinutes(1); });
    o.AddFixedWindowLimiter("login", l => { l.PermitLimit = 10; l.Window = TimeSpan.FromMinutes(1); });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LicensingDb>();
    db.Database.EnsureCreated();
    if (!db.Admins.Any())
    {
        var pwd = cfg["BootstrapAdminPassword"];
        if (string.IsNullOrWhiteSpace(pwd) || pwd.Length < 12)
            throw new InvalidOperationException("Configure Licensing:BootstrapAdminPassword (at least 12 characters) to create the first administrator.");
        db.Admins.Add(new AdminUser { UserName = cfg["BootstrapAdminUser"] ?? "admin", PasswordHash = KeyGen.HashPassword(pwd) });
        db.SaveChanges();
    }
}
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new ApiError("Internal server error."));
}));

static IResult Fail(string message, int code = 400) => Results.Json(new ApiError(message), statusCode: code);

// ------------------------------------------------------------------ client API (v1)
var v1 = app.MapGroup("/api/v1").RequireRateLimiting("client");
v1.MapGet("/health", (SigningKey k) => Results.Ok(new { status = "ok", keyId = k.KeyId, product = DND.Electrical.Shared.ProductInfo.ProductName }));
v1.MapPost("/activate", async (ActivateBody b, LicensingService s) => await s.Activate(b) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
v1.MapPost("/refresh", async (RefreshBody b, LicensingService s) => await s.Refresh(b.LicenseId, b.DeviceId) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
v1.MapPost("/deactivate", async (RefreshBody b, LicensingService s) => await s.Deactivate(b.LicenseId, b.DeviceId, "device") is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
v1.MapPost("/floating/checkout", async (CheckoutBody b, LicensingService s) => await s.Checkout(b) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!, 409));
v1.MapPost("/floating/heartbeat", async (LeaseBody b, LicensingService s) => await s.Heartbeat(b.LeaseId) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!, 409));
v1.MapPost("/floating/release", async (LeaseBody b, LicensingService s) => await s.Release(b.LeaseId) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
v1.MapGet("/revocations", async (LicensingService s) => Results.Ok(await s.Revocations()));

// ------------------------------------------------------------------ admin API
app.MapPost("/api/admin/login", async (LoginBody b, LicensingDb db) =>
{
    var admin = await db.Admins.FindAsync(b.User);
    if (admin is null || !KeyGen.VerifyPassword(b.Password, admin.PasswordHash)) return Fail("Invalid credentials.", 401);
    var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    db.Sessions.Add(new AdminSession { TokenHash = KeyGen.Sha256(token), UserName = admin.UserName, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) });
    db.Audit.Add(new AuditEntry { Actor = admin.UserName, Action = "Login" });
    await db.SaveChangesAsync();
    return Results.Ok(new { token, expiresInHours = 8 });
}).RequireRateLimiting("login");

var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Fail("Authentication required.", 401);
    var db = ctx.HttpContext.RequestServices.GetRequiredService<LicensingDb>();
    var hash = KeyGen.Sha256(header["Bearer ".Length..].Trim());
    var session = await db.Sessions.FindAsync(hash);
    if (session is null || session.ExpiresAt < DateTimeOffset.UtcNow) return Fail("Session expired.", 401);
    ctx.HttpContext.Items["admin"] = session.UserName;
    return await next(ctx);
});
string Actor(HttpContext c) => c.Items["admin"] as string ?? "admin";

admin.MapGet("/modules", () => ModuleCatalog.All.Select(m => new { m.Code, m.Name, m.Purpose }));
admin.MapGet("/customers", async (LicensingDb db) => await db.Customers.OrderBy(c => c.Name).ToListAsync());
admin.MapPost("/customers", async (CustomerBody b, LicensingDb db, HttpContext c) =>
{
    if (string.IsNullOrWhiteSpace(b.Name)) return Fail("Customer name is required.");
    var cust = new Customer { Name = b.Name.Trim(), Email = b.Email ?? "", Country = b.Country ?? "" };
    db.Customers.Add(cust);
    var org = new Organization { CustomerId = cust.Id, Name = string.IsNullOrWhiteSpace(b.Organization) ? cust.Name : b.Organization.Trim() };
    db.Organizations.Add(org);
    db.Audit.Add(new AuditEntry { Actor = Actor(c), Action = "Customer created", Details = $"{cust.Id} {cust.Name}" });
    await db.SaveChangesAsync();
    return Results.Ok(new { customer = cust, organization = org });
});
admin.MapGet("/organizations", async (LicensingDb db) => await db.Organizations.OrderBy(o => o.Name).ToListAsync());
admin.MapPost("/organizations", async (OrganizationBody b, LicensingDb db, HttpContext c) =>
{
    if (await db.Customers.FindAsync(b.CustomerId) is null) return Fail("Unknown customer.");
    var org = new Organization { CustomerId = b.CustomerId, Name = b.Name };
    db.Organizations.Add(org);
    db.Audit.Add(new AuditEntry { Actor = Actor(c), Action = "Organization created", Details = $"{org.Id} {org.Name}" });
    await db.SaveChangesAsync();
    return Results.Ok(org);
});
admin.MapGet("/licenses", async (LicensingDb db) => await db.Licenses.OrderByDescending(l => l.Created).ToListAsync());
admin.MapPost("/licenses", async (CreateLicenseBody b, LicensingService s, HttpContext c) => await s.Create(b, Actor(c)) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
admin.MapPost("/licenses/{id}/renew", async (string id, RenewBody b, LicensingService s, HttpContext c) => await s.Update(id, Actor(c), "Renewed", l => { l.ValidUntil = b.ValidUntil; l.Status = LicenseStatus.Active; }) ? Results.Ok() : Fail("Unknown licence.", 404));
admin.MapPost("/licenses/{id}/extend", async (string id, ExtendBody b, LicensingService s, HttpContext c) => await s.Update(id, Actor(c), $"Extended {b.Days} days", l => l.ValidUntil = (l.ValidUntil ?? DateTimeOffset.UtcNow).AddDays(b.Days)) ? Results.Ok() : Fail("Unknown licence.", 404));
admin.MapPost("/licenses/{id}/revoke", async (string id, LicensingService s, HttpContext c) => await s.Update(id, Actor(c), "Revoked", l => l.Status = LicenseStatus.Revoked) ? Results.Ok() : Fail("Unknown licence.", 404));
admin.MapPost("/licenses/{id}/suspend", async (string id, LicensingService s, HttpContext c) => await s.Update(id, Actor(c), "Suspended", l => l.Status = LicenseStatus.Suspended) ? Results.Ok() : Fail("Unknown licence.", 404));
admin.MapPost("/licenses/{id}/reactivate", async (string id, LicensingService s, HttpContext c) => await s.Update(id, Actor(c), "Reactivated", l => l.Status = LicenseStatus.Active) ? Results.Ok() : Fail("Unknown licence.", 404));
admin.MapPost("/licenses/{id}/modules", async (string id, ModulesBody b, LicensingService s, HttpContext c) =>
{
    foreach (var m in b.Modules) if (!ModuleCatalog.TryParse(m, out _)) return Fail($"Unknown module {m}.");
    return await s.Update(id, Actor(c), "Modules changed: " + string.Join(",", b.Modules), l => l.Modules = string.Join(",", b.Modules.Select(x => x.ToUpperInvariant()))) ? Results.Ok() : Fail("Unknown licence.", 404);
});
admin.MapGet("/licenses/{id}/activations", async (string id, LicensingDb db) => await db.Activations.Where(a => a.LicenseId == id).OrderByDescending(a => a.ActivatedAt).ToListAsync());
admin.MapPost("/activations/{id:guid}/deactivate", async (Guid id, LicensingDb db, LicensingService s, HttpContext c) =>
{
    var a = await db.Activations.FindAsync(id);
    if (a is null) return Fail("Unknown activation.", 404);
    await s.Deactivate(a.LicenseId, a.DeviceId, Actor(c));
    return Results.Ok();
});
admin.MapPost("/offline-activation", async (ActivationRequest req, LicensingService s, HttpContext c) => await s.OfflineActivate(req, Actor(c)) is var (r, e) && e is null ? Results.Ok(r) : Fail(e!));
admin.MapGet("/usage", async (LicensingDb db) =>
{
    var now = DateTimeOffset.UtcNow;
    var lic = await db.Licenses.ToListAsync();
    var acts = await db.Activations.Where(a => a.DeactivatedAt == null).ToListAsync();
    var leases = (await db.Leases.Where(l => !l.Released).ToListAsync()).Where(l => l.ExpiresAt > now).ToList();
    return Results.Ok(lic.Select(l => new
    {
        l.Id, l.OrganizationName, l.Subscription, l.Status, l.ValidUntil, l.Modules,
        Devices = $"{acts.Count(a => a.LicenseId == l.Id)}/{l.MaxDevices}",
        Floating = l.Seats == SeatModel.Floating ? $"{leases.Count(x => x.LicenseId == l.Id)}/{l.MaxConcurrent}" : "",
    }));
});
admin.MapGet("/audit", async (LicensingDb db) => (await db.Audit.ToListAsync()).OrderByDescending(a => a.At).Take(500));
admin.MapGet("/reports/licenses.csv", async (LicensingDb db) =>
{
    var sb = new StringBuilder("LicenseId,Customer,Organization,Subscription,Seats,Modules,ValidFrom,ValidUntil,MaxDevices,MaxConcurrent,Status\n");
    foreach (var l in await db.Licenses.ToListAsync())
        sb.AppendLine(string.Join(",", new[] { l.Id, l.CustomerId, l.OrganizationName, l.Subscription.ToString(), l.Seats.ToString(), l.Modules.Replace(",", " "), l.ValidFrom.ToString("u"), l.ValidUntil?.ToString("u") ?? "perpetual", l.MaxDevices.ToString(), l.MaxConcurrent.ToString(), l.Status.ToString() }
            .Select(v => v.Contains(',') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v)));
    return Results.Text(sb.ToString(), "text/csv");
});

app.Run();

public sealed record ActivateBody(string LicenseKey, string DeviceId, string DeviceName, string User);
public sealed record RefreshBody(string LicenseId, string DeviceId);
public sealed record CheckoutBody(string LicenseId, string DeviceId, string User);
public sealed record LeaseBody(string LeaseId);
public sealed record LoginBody(string User, string Password);
public sealed record CustomerBody(string Name, string? Email, string? Country, string? Organization);
public sealed record OrganizationBody(string CustomerId, string Name);
public sealed record RenewBody(DateTimeOffset ValidUntil);
public sealed record ExtendBody(int Days);
public sealed record ModulesBody(List<string> Modules);
public sealed record CreateLicenseBody(string CustomerId, string? OrganizationId, string Owner, SubscriptionType Subscription, SeatModel Seats, List<string> Modules,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, int MaxUsers, int MaxDevices, int MaxConcurrent, bool OfflineAllowed, int GracePeriodDays, int RevalidationDays, string? Notes);

public partial class Program;
