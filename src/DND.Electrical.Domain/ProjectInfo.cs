namespace DND.Electrical.Domain;

/// <summary>Descriptive project metadata shown in title blocks, reports and the project explorer.</summary>
public sealed class ProjectInfo
{
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string CustomerContact { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;

    /// <summary>Id of the attachment holding the company logo (PNG/JPEG), if any.</summary>
    public Guid? CompanyLogoAttachmentId { get; set; }

    public string Engineer { get; set; } = string.Empty;
    public string CheckedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;

    public ProjectIndustry Industry { get; set; } = ProjectIndustry.General;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public string CurrentRevision { get; set; } = "A";
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>True for the bundled demonstration projects (fictional data).</summary>
    public bool IsDemo { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    public ProjectInfo Clone() => (ProjectInfo)MemberwiseClone();
}

public sealed class ProjectSettings
{
    public WireNumberingMode WireNumberingMode { get; set; } = WireNumberingMode.PerPotential;

    /// <summary>Format for automatic wire numbers. Tokens: {GSEQ} (project-wide sequence), {SHEET}, {COL}, {SEQ} (sequence within sheet), optional zero padding e.g. {GSEQ:000}.</summary>
    public string WireNumberFormat { get; set; } = "{GSEQ}";

    /// <summary>Format for device tags. Tokens: {PREFIX}, {N}, {SHEET}, {COL}.</summary>
    public string DeviceTagFormat { get; set; } = "-{PREFIX}{N}";

    public string StandardProfileId { get; set; } = "IEC";
    public string DefaultSheetFormat { get; set; } = "A3";
    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "EUR";
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>Nominal system voltage (line-to-line) in volts.</summary>
    public double NominalVoltage { get; set; } = 400;
    public double FrequencyHz { get; set; } = 50;
    public string EarthingSystem { get; set; } = "TN-S";

    /// <summary>Number of reference columns on schematic sheets (used for cross references).</summary>
    public int SheetColumns { get; set; } = 10;

    public Dictionary<string, ValidationRuleSetting> ValidationRules { get; set; } = new();

    public ProjectSettings Clone()
    {
        var c = (ProjectSettings)MemberwiseClone();
        c.ValidationRules = ValidationRules.ToDictionary(kv => kv.Key, kv => kv.Value with { });
        return c;
    }
}

public sealed record ValidationRuleSetting(bool Enabled = true, string? SeverityOverride = null);

public sealed class ProjectRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "A";
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RevisionStatus Status { get; set; } = RevisionStatus.Draft;
}
