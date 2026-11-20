using System.Security.Cryptography;
using System.Text;

namespace DND.Electrical.Licensing;

/// <summary>
/// Privacy-conscious device identifier: a salted SHA-256 of the machine name, OS platform and a random
/// installation identifier stored locally. No hardware serial numbers, MAC addresses or user data are collected,
/// and the hash cannot be reversed to the machine name.
/// </summary>
public static class DeviceIdentity
{
    public static string Compute(string stateFolder)
    {
        Directory.CreateDirectory(stateFolder);
        var idFile = Path.Combine(stateFolder, "installation.id");
        if (!File.Exists(idFile)) File.WriteAllText(idFile, Guid.NewGuid().ToString("N"));
        var install = File.ReadAllText(idFile).Trim();
        var material = $"{Shared.ProductInfo.ProductId}|{Environment.MachineName}|{Environment.OSVersion.Platform}|{install}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32].ToLowerInvariant();
    }

    public static string DeviceName => Environment.MachineName;
}
