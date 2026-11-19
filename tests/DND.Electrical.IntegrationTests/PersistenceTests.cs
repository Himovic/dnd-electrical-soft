using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Projects;
using DND.Electrical.Application.Templates;
using DND.Electrical.Domain;
using DND.Electrical.Infrastructure.Persistence;
using DND.Electrical.Schematic.Connectivity;
using DND.Electrical.Schematic.Entities;
using DND.Electrical.Wiring;
using Microsoft.Data.Sqlite;

namespace DND.Electrical.IntegrationTests;

public class PersistenceTests
{
    public static IEnumerable<object[]> DemoNumbers => DemoProjectFactory.Catalogue.Select(c => new object[] { c.Number });

    [Theory]
    [MemberData(nameof(DemoNumbers))]
    public async Task Demo_projects_roundtrip_through_sqlite(string number)
    {
        using var env = new TestEnvironment();
        var original = env.Demos.Create(number);
        var path = env.PathFor($"{number}.dndx");
        await env.Store.SaveAsync(original, path);
        var loaded = await env.Store.LoadAsync(path);

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Info.Number, loaded.Info.Number);
        Assert.True(loaded.Info.IsDemo);
        Assert.Equal(original.Sheets.Select(s => s.Title), loaded.Sheets.Select(s => s.Title));
        Assert.Equal(original.Devices.Select(d => d.Tag), loaded.Devices.Select(d => d.Tag));
        Assert.Equal(original.Cables.Sum(c => c.Cores.Count), loaded.Cables.Sum(c => c.Cores.Count));
        Assert.Equal(original.Cables.SelectMany(c => c.Cores).Select(c => c.Marking), loaded.Cables.SelectMany(c => c.Cores).Select(c => c.Marking));
        Assert.Equal(original.PlcStations.SelectMany(s => s.Channels).Select(c => c.Address), loaded.PlcStations.SelectMany(s => s.Channels).Select(c => c.Address));
        Assert.Equal(original.Calculations.Count, loaded.Calculations.Count);
        Assert.Equal(original.Cabinets.Sum(c => c.Items.Count), loaded.Cabinets.Sum(c => c.Items.Count));
        Assert.Equal(original.Catalog.Count, loaded.Catalog.Count);
        Assert.Equal(original.Loads.Count, loaded.Loads.Count);
        Assert.Equal(original.Symbols.Count, loaded.Symbols.Count);
        Assert.Equal(original.Settings.WireNumberFormat, loaded.Settings.WireNumberFormat);

        // Connectivity is identical after reload (symbols resolved from the project cache only).
        var g1 = ConnectivityGraph.Build(original);
        var g2 = ConnectivityGraph.Build(loaded);
        Assert.Equal(g1.Potentials.Count, g2.Potentials.Count);
        Assert.Equal(g1.Wires.Count, g2.Wires.Count);
        Assert.Equal(WiringReports.ConnectionList(original, g1).Select(r => (r.WireNumber, r.From, r.To)),
                     WiringReports.ConnectionList(loaded, g2).Select(r => (r.WireNumber, r.From, r.To)));
        Assert.Empty(Directory.GetFiles(env.Folder, "*.saving-*"));
    }

    [Fact]
    public async Task Derived_connection_table_is_queryable_with_sql()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var path = env.PathFor("conn.dndx");
        await env.Store.SaveAsync(p, path);
        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Connections WHERE ToDesignation LIKE '-M1:%'";
        Assert.Equal(4L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task Non_project_files_are_rejected()
    {
        using var env = new TestEnvironment();
        var bogus = env.PathFor("bogus.dndx");
        File.WriteAllText(bogus, "not a database");
        await Assert.ThrowsAsync<InvalidDataException>(() => env.Store.LoadAsync(bogus));
    }

    [Fact]
    public async Task Project_service_lifecycle_with_lock_backup_and_revision()
    {
        using var env = new TestEnvironment();
        var session = env.Projects.CreateNew(ProjectTemplate.Get("industrial"), new ProjectInfo { Number = "P-100", Name = "Lifecycle", Engineer = "Tester" });
        Assert.Null(session.FilePath);
        Assert.Equal(3, session.Project.Sheets.Count); // cover + power + control
        var path = env.PathFor("p100.dndx");
        await env.Projects.SaveAsAsync(path);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".lock"));
        Assert.False(session.IsDirty);

        env.Projects.CreateRevision("B", "Customer comments", "Tester");
        Assert.True(session.IsDirty);
        await env.Projects.SaveAsync();
        Assert.Single(env.Backups.List(path));

        env.Projects.Close();
        Assert.False(File.Exists(path + ".lock"));
        var reopened = await env.Projects.OpenAsync(path);
        Assert.Equal("B", reopened.Project.Info.CurrentRevision);
        Assert.Equal(2, reopened.Project.Revisions.Count);
        Assert.Contains(reopened.Project.AuditEvents, a => a.Action == "Revision");
        env.Projects.Close();
    }

    [Fact]
    public async Task Lock_held_by_another_process_blocks_opening()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-BLD-005");
        var path = env.PathFor("locked.dndx");
        await env.Store.SaveAsync(p, path);
        File.WriteAllText(path + ".lock", System.Text.Json.JsonSerializer.Serialize(new DND.Electrical.Application.Abstractions.LockInfo("alice", "OTHER-PC", 4242, DateTimeOffset.Now)));
        var ex = await Assert.ThrowsAsync<ProjectLockedException>(() => env.Projects.OpenAsync(path));
        Assert.Equal("alice", ex.Holder.User);
        var forced = await env.Projects.OpenAsync(path, forceLock: true);
        Assert.NotNull(forced);
        env.Projects.Close();
    }

    [Fact]
    public async Task Autosave_recovery_roundtrip()
    {
        using var env = new TestEnvironment();
        var session = env.Projects.OpenInMemory(env.Demos.Create("DEMO-IND-001"));
        session.MarkDirty();
        await env.Projects.AutosaveAsync();
        var entries = env.Recovery.FindRecoverable();
        Assert.Single(entries);
        var recovered = await env.Projects.RecoverAsync(entries[0]);
        Assert.Equal(session.Project.Devices.Count, recovered.Project.Devices.Count);
        Assert.True(recovered.IsDirty);
        env.Recovery.Discard(entries[0].ProjectId);
        Assert.Empty(env.Recovery.FindRecoverable());
    }

    [Fact]
    public async Task Archive_export_import_and_duplicate()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-CAB-004");
        var pkg = env.PathFor("cab.dndpkg");
        await ProjectArchive.ExportAsync(env.Store, p, pkg);
        var target = env.PathFor("imported.dndx");
        var manifest = await ProjectArchive.ImportAsync(pkg, target);
        Assert.Equal("DEMO-CAB-004", manifest.Number);
        var loaded = await env.Store.LoadAsync(target);
        Assert.Equal(p.Devices.Count, loaded.Devices.Count);

        var dup = env.PathFor("dup.dndx");
        await env.Projects.DuplicateAsync(target, dup, "CAB-COPY");
        var copy = await env.Store.LoadAsync(dup);
        Assert.NotEqual(p.Id, copy.Id);
        Assert.Equal("CAB-COPY", copy.Info.Number);
        Assert.False(copy.Info.IsDemo);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\x.dndx")]
    public void Archive_entry_names_are_validated(string name) =>
        Assert.Throws<InvalidDataException>(() => ProjectArchive.ValidateEntryName(name));

    [Fact]
    public async Task Malicious_archive_is_rejected()
    {
        using var env = new TestEnvironment();
        var pkg = env.PathFor("evil.dndpkg");
        using (var zip = System.IO.Compression.ZipFile.Open(pkg, System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("manifest.json");
            zip.CreateEntry("../../outside.dndx");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectArchive.ImportAsync(pkg, env.PathFor("x.dndx")));
        Assert.False(File.Exists(Path.Combine(env.Folder, "..", "outside.dndx")));
    }

    [Fact]
    public async Task Central_store_holds_many_projects()
    {
        using var env = new TestEnvironment();
        var store = new CentralProjectStore(CentralProvider.Sqlite, $"Data Source={env.PathFor("central.db")}");
        await store.InitializeAsync();
        var a = env.Demos.Create("DEMO-IND-001");
        var b = env.Demos.Create("DEMO-BLD-005");
        await store.SaveAsync(a);
        await store.SaveAsync(b);
        a.Info.Name = "Renamed";
        await store.SaveAsync(a); // replace in place
        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        var loaded = await store.LoadAsync(a.Id);
        Assert.Equal("Renamed", loaded.Info.Name);
        Assert.Equal(a.Devices.Count, loaded.Devices.Count);
        await store.DeleteAsync(b.Id);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public void Json_interchange_roundtrip()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-AUT-002");
        var json = ProjectInterchange.ToJson(p);
        var back = ProjectInterchange.FromJson(json);
        Assert.Equal(p.Devices.Count, back.Devices.Count);
        Assert.Equal(p.Sheets.Sum(s => s.Drawing.Entities.Count), back.Sheets.Sum(s => s.Drawing.Entities.Count));
        Assert.Equal(ConnectivityGraph.Build(p).Potentials.Count, ConnectivityGraph.Build(back).Potentials.Count);
        var xml = ProjectInterchange.ToEngineeringXml(p);
        Assert.NotEmpty(xml.Root!.Element("PlcIo")!.Elements());
    }
}
