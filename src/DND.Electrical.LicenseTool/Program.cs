using System.Globalization;
using DND.Electrical.Licensing;
using DND.Electrical.Shared;

// D&D licence administration tool (offline). Requires the private signing key: run only on the licensing
// workstation/server, never ship the key with the product.
static string? Opt(string[] a, string n) { var i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine($"""
        dnd-license – {ProductInfo.ProductName} licence administration
        Developed by {ProductInfo.Developer}

          dnd-license keygen --out <folder>
          dnd-license issue --key <private.pem> --org <name> --customer <id> --modules ELEC,AUTO,... |ALL
                            --type Annual|Monthly|Perpetual|PerpetualWithMaintenance|Enterprise|Trial|Evaluation
                            [--seats Device|NamedUser|Floating] [--days 365] [--until yyyy-MM-dd] [--users 1] [--devices 1]
                            [--concurrent 0] [--device <deviceId>] [--grace 14] [--revalidate 0] [--offline true] [--out file.dndlic]
          dnd-license verify <file.dndlic> [--pub <public.pem>]
          dnd-license request-info <activation-request.json>
        """);
    return 0;
}

switch (args[0])
{
    case "keygen":
        {
            var outDir = Opt(args, "--out") ?? ".";
            Directory.CreateDirectory(outDir);
            var (id, priv, pub) = LicenseCrypto.GenerateKeyPair();
            File.WriteAllText(Path.Combine(outDir, $"signing-key-{id}.pem"), priv);
            File.WriteAllText(Path.Combine(outDir, $"public-key-{id}.pem"), pub);
            Console.WriteLine($"Key {id} generated in {outDir}. Keep the signing key secret; distribute only the public key.");
            return 0;
        }
    case "issue":
        {
            var keyPath = Opt(args, "--key") ?? throw new ArgumentException("--key is required");
            var priv = File.ReadAllText(keyPath);
            using var ec = System.Security.Cryptography.ECDsa.Create();
            ec.ImportFromPem(priv);
            var pub = ec.ExportSubjectPublicKeyInfoPem();
            var modules = Opt(args, "--modules") ?? "ALL";
            var codes = modules.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                ? ModuleCatalog.All.Select(m => m.Code).ToList()
                : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var c in codes) if (!ModuleCatalog.TryParse(c, out _)) throw new ArgumentException($"Unknown module '{c}'.");
            var type = Enum.Parse<SubscriptionType>(Opt(args, "--type") ?? "Annual", true);
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? until = Opt(args, "--until") is { } u ? DateTimeOffset.Parse(u, CultureInfo.InvariantCulture)
                : type is SubscriptionType.Perpetual or SubscriptionType.PerpetualWithMaintenance ? null
                : now.AddDays(int.Parse(Opt(args, "--days") ?? "365", CultureInfo.InvariantCulture));
            var claims = new LicenseClaims
            {
                LicenseId = Opt(args, "--id") ?? LicenseIssuer.NewLicenseId(),
                CustomerId = Opt(args, "--customer") ?? "CUST-0000",
                OrganizationId = Opt(args, "--org-id") ?? "ORG-0000",
                OrganizationName = Opt(args, "--org") ?? "Unnamed organisation",
                Owner = Opt(args, "--owner") ?? Opt(args, "--org") ?? string.Empty,
                Subscription = type,
                Seats = Enum.Parse<SeatModel>(Opt(args, "--seats") ?? "Device", true),
                Modules = codes,
                IssuedAt = now,
                ValidFrom = Opt(args, "--from") is { } f ? DateTimeOffset.Parse(f, CultureInfo.InvariantCulture) : now,
                ValidUntil = until,
                MaintenanceUntil = type == SubscriptionType.PerpetualWithMaintenance ? now.AddYears(1) : null,
                MaxUsers = int.Parse(Opt(args, "--users") ?? "1", CultureInfo.InvariantCulture),
                MaxDevices = int.Parse(Opt(args, "--devices") ?? "1", CultureInfo.InvariantCulture),
                MaxConcurrent = int.Parse(Opt(args, "--concurrent") ?? "0", CultureInfo.InvariantCulture),
                GracePeriodDays = int.Parse(Opt(args, "--grace") ?? "14", CultureInfo.InvariantCulture),
                RevalidationDays = int.Parse(Opt(args, "--revalidate") ?? "0", CultureInfo.InvariantCulture),
                OfflineAllowed = bool.Parse(Opt(args, "--offline") ?? "true"),
                DeviceId = Opt(args, "--device"),
                Notes = Opt(args, "--notes"),
            };
            var signed = LicenseCrypto.Sign(claims, priv, LicenseCrypto.KeyIdOf(pub));
            var output = Opt(args, "--out") ?? $"{claims.LicenseId}.dndlic";
            File.WriteAllText(output, signed.ToJson());
            Console.WriteLine($"Issued {claims.LicenseId} ({claims.Subscription}, {string.Join(",", codes)}) → {output}");
            return 0;
        }
    case "verify":
        {
            var doc = SignedLicense.FromJson(File.ReadAllText(args[1]));
            var keys = TrustedKeys.Product();
            if (Opt(args, "--pub") is { } p) keys.Add(File.ReadAllText(p));
            var (claims, error) = LicenseVerifier.Verify(doc, keys);
            if (claims is null) { Console.Error.WriteLine("INVALID: " + error); return 1; }
            Console.WriteLine($"VALID signature (key {doc.KeyId})\n{LicenseCrypto.Canonical(claims)}");
            return 0;
        }
    case "request-info":
        {
            var req = System.Text.Json.JsonSerializer.Deserialize<ActivationRequest>(File.ReadAllText(args[1]), LicenseJson.Options)!;
            Console.WriteLine($"Licence {req.LicenseId} · device {req.DeviceId} ({req.DeviceName}) · user {req.User} · {req.CreatedAt:u}");
            return 0;
        }
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 2;
}
