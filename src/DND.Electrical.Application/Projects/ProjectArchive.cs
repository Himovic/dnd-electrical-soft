using System.IO.Compression;
using System.Text.Json;
using DND.Electrical.Application.Abstractions;
using DND.Electrical.Domain;
using DND.Electrical.Shared;

namespace DND.Electrical.Application.Projects;

public sealed record ArchiveManifest(string Product, string ProductVersion, int FormatVersion, Guid ProjectId, string Number, string Name, DateTimeOffset Created, string ProjectEntry);

/// <summary>
/// ZIP project package (.dndpkg): manifest + project database. Extraction is defensive: entry names are
/// validated (no path traversal), sizes and entry counts are limited, and only expected entries are read.
/// </summary>
public static class ProjectArchive
{
    public const string ManifestEntry = "manifest.json";
    public const string ProjectEntry = "project.dndx";
    public const long MaxEntrySize = 2L * 1024 * 1024 * 1024;
    public const int MaxEntries = 64;

    public static async Task ExportAsync(IProjectStore store, ElectricalProject project, string archivePath, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"dnd-export-{Guid.NewGuid():N}.dndx");
        try
        {
            await store.SaveAsync(project, temp, ct);
            if (File.Exists(archivePath)) File.Delete(archivePath);
            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            var manifest = new ArchiveManifest(ProductInfo.ProductName, ProductInfo.VersionString, ProductInfo.ProjectFormatVersion,
                project.Id, project.Info.Number, project.Info.Name, DateTimeOffset.Now, ProjectEntry);
            var me = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
            await using (var s = me.Open()) await JsonSerializer.SerializeAsync(s, manifest, new JsonSerializerOptions { WriteIndented = true }, ct);
            zip.CreateEntryFromFile(temp, ProjectEntry, CompressionLevel.Optimal);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Extracts the project database to <paramref name="targetPath"/> and returns the manifest.</summary>
    public static async Task<ArchiveManifest> ImportAsync(string archivePath, string targetPath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("Archive contains too many entries.");
        foreach (var e in zip.Entries) ValidateEntryName(e.FullName);

        var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("Archive has no manifest.");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("Manifest is too large.");
        ArchiveManifest manifest;
        await using (var ms = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<ArchiveManifest>(ms, cancellationToken: ct) ?? throw new InvalidDataException("Invalid manifest.");
        if (manifest.FormatVersion > ProductInfo.ProjectFormatVersion)
            throw new InvalidDataException($"The archive was created by a newer version (format {manifest.FormatVersion}). Please update the application.");

        var entry = zip.GetEntry(ProjectEntry) ?? throw new InvalidDataException("Archive has no project database.");
        if (entry.Length > MaxEntrySize) throw new InvalidDataException("Project entry is too large.");
        if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200) throw new InvalidDataException("Suspicious compression ratio.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        var temp = targetPath + ".importing";
        await using (var src = entry.Open())
        await using (var dst = File.Create(temp))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxEntrySize) throw new InvalidDataException("Project entry exceeds the size limit.");
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        File.Move(temp, targetPath, overwrite: false);
        return manifest;
    }

    public static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || Path.IsPathRooted(name) || name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':'))
            throw new InvalidDataException($"Unsafe archive entry name '{name}'.");
    }
}
