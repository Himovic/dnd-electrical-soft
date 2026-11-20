using System.Text.Json;
using DND.Electrical.CAD.Model;
using DND.Electrical.CAD.Serialization;
using DND.Electrical.Domain;
using DND.Electrical.Schematic.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DND.Electrical.Infrastructure.Persistence;

/// <summary>Derived connection data written on save so that external tools can query connectivity with SQL.</summary>
public sealed class ConnectionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid WireId { get; set; }
    public Guid SheetId { get; set; }
    public string WireNumber { get; set; } = string.Empty;
    public string FromDesignation { get; set; } = string.Empty;
    public string ToDesignation { get; set; } = string.Empty;
    public string Potential { get; set; } = string.Empty;
    public string Cable { get; set; } = string.Empty;
}

/// <summary>
/// EF Core model of a project. Sheet geometry is stored as JSON (efficient and schema-stable);
/// engineering data is relational. A "SortOrder" shadow property preserves list order.
/// </summary>
public sealed class ProjectDbContext(DbContextOptions<ProjectDbContext> options) : DbContext(options)
{
    public const string SortOrder = "SortOrder";

    static ProjectDbContext() => SchematicEntityTypes.Register();

    public DbSet<ElectricalProject> Projects => Set<ElectricalProject>();
    public DbSet<Sheet> Sheets => Set<Sheet>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Cable> Cables => Set<Cable>();
    public DbSet<TerminalStrip> TerminalStrips => Set<TerminalStrip>();
    public DbSet<PlcStation> PlcStations => Set<PlcStation>();
    public DbSet<Motor> Motors => Set<Motor>();
    public DbSet<Cabinet> Cabinets => Set<Cabinet>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
    public DbSet<ElectricalLoad> Loads => Set<ElectricalLoad>();
    public DbSet<CalculationRecord> Calculations => Set<CalculationRecord>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ConnectionRecord> Connections => Set<ConnectionRecord>();

    private static readonly JsonSerializerOptions Json = CadJson.Options;

    private static ValueConverter<T, string> JsonConverter<T>() where T : class, new() =>
        new(v => JsonSerializer.Serialize(v, Json), v => string.IsNullOrEmpty(v) ? new T() : JsonSerializer.Deserialize<T>(v, Json) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() where T : class, new() =>
        new((a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ElectricalProject>(e =>
        {
            e.ToTable("Projects");
            e.HasKey(x => x.Id);
            e.Ignore(x => x.OrderedSheets);
            e.OwnsOne(x => x.Info, o =>
            {
                o.Property(i => i.Number).HasMaxLength(100);
                o.Property(i => i.Name).HasMaxLength(300);
                o.Property(i => i.Status).HasConversion<string>().HasMaxLength(40);
                o.Property(i => i.Industry).HasConversion<string>().HasMaxLength(40);
                o.HasIndex(i => i.Number);
            });
            e.Property(x => x.Settings).HasConversion(JsonConverter<ProjectSettings>(), JsonComparer<ProjectSettings>()).HasColumnName("SettingsJson");
            Children(e, x => x.Revisions);
            Children(e, x => x.Sheets);
            Children(e, x => x.Devices);
            Children(e, x => x.Cables);
            Children(e, x => x.TerminalStrips);
            Children(e, x => x.PlcStations);
            Children(e, x => x.Motors);
            Children(e, x => x.Cabinets);
            Children(e, x => x.Catalog);
            Children(e, x => x.Procurement);
            Children(e, x => x.Loads);
            Children(e, x => x.Calculations);
            Children(e, x => x.Attachments);
            Children(e, x => x.AuditEvents);
            Children(e, x => x.ValidationRuns);
            Children(e, x => x.Members);
            Children(e, x => x.Tasks);
            Children(e, x => x.Milestones);
            Children(e, x => x.Issues);
            Children(e, x => x.Approvals);
            Children(e, x => x.Comments);
            Children(e, x => x.DocumentRegister);
            Children(e, x => x.Maintenance);
            Children(e, x => x.Commissioning);
            Children(e, x => x.Layouts3D);
            Children(e, x => x.PowerNetworks);
            Children(e, x => x.Instruments);
            Children(e, x => x.Loops);
            Children(e, x => x.Alarms);
            Children(e, x => x.CauseEffects);
            Children(e, x => x.ScadaNodes);
            e.OwnsMany(x => x.Symbols, s =>
            {
                s.ToTable("ProjectSymbols");
                s.WithOwner().HasForeignKey("ProjectId");
                s.HasKey("ProjectId", nameof(ProjectSymbol.SymbolId));
                s.Property(p => p.SymbolId).HasMaxLength(200);
            });
        });

        b.Entity<ProjectRevision>(e => { e.ToTable("Revisions"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); });
        b.Entity<Sheet>(e =>
        {
            e.ToTable("Sheets");
            e.Ignore(x => x.Format);
            e.Ignore(x => x.DisplayName);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Drawing).HasConversion(
                    new ValueConverter<CadDrawing, string>(v => CadJson.SerializeDrawing(v, false), v => CadJson.DeserializeDrawing(v)),
                    new ValueComparer<CadDrawing>((a, b) => ReferenceEquals(a, b), v => v.GetHashCode(), v => v))
                .HasColumnName("DrawingJson");
            e.Property(x => x.Fields).HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>()).HasColumnName("FieldsJson");
        });
        b.Entity<Device>(e =>
        {
            e.ToTable("Devices");
            e.Ignore(x => x.DisplayName);
            e.HasIndex(x => x.Tag);
            e.Property(x => x.Tag).HasMaxLength(100);
            e.Property(x => x.Properties).HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>()).HasColumnName("PropertiesJson");
        });
        b.Entity<Cable>(e =>
        {
            e.ToTable("Cables");
            e.Ignore(x => x.Designation);
            e.Property(x => x.Material).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Insulation).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Cores).WithOne().HasForeignKey("CableId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CableCore>(e => { e.ToTable("CableCores"); e.Ignore(x => x.IsSpare); e.Property<int>(SortOrder); });
        b.Entity<TerminalStrip>(e =>
        {
            e.ToTable("TerminalStrips");
            e.HasMany(x => x.Terminals).WithOne().HasForeignKey("TerminalStripId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TerminalDefinition>(e => { e.ToTable("Terminals"); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30); e.Property<int>(SortOrder); });
        b.Entity<PlcStation>(e =>
        {
            e.ToTable("PlcStations");
            e.Ignore(x => x.Modules);
            e.Ignore(x => x.Channels);
            e.HasMany(x => x.Racks).WithOne().HasForeignKey("PlcStationId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlcRack>(e =>
        {
            e.ToTable("PlcRacks");
            e.Property<int>(SortOrder);
            e.HasMany(x => x.Modules).WithOne().HasForeignKey("PlcRackId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlcModule>(e =>
        {
            e.ToTable("PlcModules");
            e.Property<int>(SortOrder);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.HasMany(x => x.Channels).WithOne().HasForeignKey("PlcModuleId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlcChannel>(e =>
        {
            e.ToTable("PlcChannels");
            e.Ignore(x => x.IsSpare);
            e.Property<int>(SortOrder);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Address);
        });
        b.Entity<Motor>(e => { e.ToTable("Motors"); e.Property(x => x.StartingMethod).HasConversion<string>().HasMaxLength(40); });
        b.Entity<Cabinet>(e =>
        {
            e.ToTable("Cabinets");
            e.HasMany(x => x.Items).WithOne().HasForeignKey("CabinetId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CabinetItem>(e =>
        {
            e.ToTable("CabinetItems");
            e.Property<int>(SortOrder);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Face).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CatalogProduct>(e =>
        {
            e.ToTable("CatalogProducts");
            e.HasIndex(x => new { x.Manufacturer, x.PartNumber });
            e.Property(x => x.UnitPrice).HasConversion<double>();
            e.Property(x => x.Specifications).HasConversion(JsonConverter<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>()).HasColumnName("SpecificationsJson");
            e.Property(x => x.Alternatives).HasConversion(JsonConverter<List<string>>(), JsonComparer<List<string>>()).HasColumnName("AlternativesJson");
        });
        b.Entity<ProcurementEntry>(e => { e.ToTable("Procurement"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); });
        b.Entity<ElectricalLoad>(e =>
        {
            e.ToTable("Loads");
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
        });
        b.Entity<CalculationRecord>(e => { e.ToTable("Calculations"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); });
        b.Entity<Attachment>(e => { e.ToTable("Attachments"); e.Ignore(x => x.Size); });
        b.Entity<AuditEvent>(e => { e.ToTable("AuditEvents"); e.HasIndex(x => x.Timestamp); });
        b.Entity<ValidationRunRecord>(e => e.ToTable("ValidationRuns"));
        b.Entity<ProjectMember>(e => { e.ToTable("ProjectMembers"); e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30); });
        b.Entity<ProjectTask>(e => { e.ToTable("ProjectTasks"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(40); });
        b.Entity<Milestone>(e => { e.ToTable("Milestones"); e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(40); });
        b.Entity<EngineeringIssue>(e => { e.ToTable("EngineeringIssues"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20); });
        b.Entity<ApprovalRecord>(e => { e.ToTable("Approvals"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.HasIndex(x => x.SubjectId); });
        b.Entity<CommentRecord>(e => { e.ToTable("Comments"); e.HasIndex(x => x.TargetId); });
        b.Entity<DocumentRegisterEntry>(e => e.ToTable("DocumentRegister"));
        b.Entity<MaintenanceRecord>(e => { e.ToTable("MaintenanceRecords"); e.HasIndex(x => x.ObjectId); });
        b.Entity<Instrument>(e =>
        {
            e.ToTable("Instruments"); e.HasIndex(x => x.Tag); e.Ignore(x => x.IsAnalog);
            e.Property(x => x.Variable).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Function).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Signal).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        });
        b.Entity<ControlLoop>(e => { e.ToTable("ControlLoops"); e.HasIndex(x => x.Tag); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30); });
        b.Entity<AlarmDefinition>(e =>
        {
            e.ToTable("Alarms"); e.HasIndex(x => x.Tag);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Class).HasConversion<string>().HasMaxLength(30);
        });
        b.Entity<CauseEffectMatrix>(e =>
        {
            e.ToTable("CauseEffectMatrices");
            e.Property(x => x.Causes).HasConversion(JsonConverter<List<CauseEffectCause>>(), JsonComparer<List<CauseEffectCause>>()).HasColumnName("CausesJson");
            e.Property(x => x.Effects).HasConversion(JsonConverter<List<CauseEffectEffect>>(), JsonComparer<List<CauseEffectEffect>>()).HasColumnName("EffectsJson");
            e.Property(x => x.Links).HasConversion(JsonConverter<List<CauseEffectLink>>(), JsonComparer<List<CauseEffectLink>>()).HasColumnName("LinksJson");
        });
        b.Entity<ScadaNode>(e => { e.ToTable("ScadaNodes"); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30); });
        b.Entity<PowerNetworkDocument>(e => { e.ToTable("PowerNetworks"); e.Property(x => x.Name).HasMaxLength(200); });
        b.Entity<Layout3DDocument>(e => { e.ToTable("Layouts3D"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Kind).HasMaxLength(40); });
        b.Entity<CommissioningRecord>(e => { e.ToTable("CommissioningRecords"); e.Property(x => x.Result).HasConversion<string>().HasMaxLength(30); e.HasIndex(x => x.ObjectId); });
        b.Entity<ConnectionRecord>(e => { e.ToTable("Connections"); e.HasIndex(x => x.WireNumber); e.HasIndex(x => x.ProjectId); });

        foreach (var entity in b.Model.GetEntityTypes().ToList())
        {
            // Preserve list order of every child collection.
            if (entity.ClrType != typeof(ElectricalProject) && entity.ClrType != typeof(ProjectInfo) && entity.ClrType != typeof(ConnectionRecord)
                && entity.FindProperty(SortOrder) is null)
                entity.AddProperty(SortOrder, typeof(int));
            // SQLite cannot order/compare DateTimeOffset natively: store as ISO 8601 text (sortable).
            if (Database.IsSqlite())
                foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset)))
                    prop.SetValueConverter(new DateTimeOffsetToStringConverter());
        }
    }

    private static void Children<TChild>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ElectricalProject> e,
        System.Linq.Expressions.Expression<Func<ElectricalProject, IEnumerable<TChild>?>> nav) where TChild : class
    {
        e.HasMany(nav).WithOne().HasForeignKey("ProjectId").OnDelete(DeleteBehavior.Cascade);
        e.Navigation(nav!).AutoInclude(false);
    }
}

/// <summary>Design-time factory used by "dotnet ef migrations".</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ProjectDbContext>
{
    public ProjectDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProjectDbContext>().UseSqlite("Data Source=design.dndx").Options);
}
