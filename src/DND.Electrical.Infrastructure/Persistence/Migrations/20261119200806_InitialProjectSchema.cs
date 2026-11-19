using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DND.Electrical.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProjectSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WireId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SheetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WireNumber = table.Column<string>(type: "TEXT", nullable: false),
                    FromDesignation = table.Column<string>(type: "TEXT", nullable: false),
                    ToDesignation = table.Column<string>(type: "TEXT", nullable: false),
                    Potential = table.Column<string>(type: "TEXT", nullable: false),
                    Cable = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Info_Number = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Info_Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Info_Description = table.Column<string>(type: "TEXT", nullable: false),
                    Info_Reference = table.Column<string>(type: "TEXT", nullable: false),
                    Info_Site = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CustomerName = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CustomerAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CustomerContact = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CompanyAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CompanyPhone = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CompanyEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CompanyLogoAttachmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Info_Engineer = table.Column<string>(type: "TEXT", nullable: false),
                    Info_CheckedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Info_ApprovedBy = table.Column<string>(type: "TEXT", nullable: false),
                    Info_Industry = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Info_Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Info_CurrentRevision = table.Column<string>(type: "TEXT", nullable: false),
                    Info_TemplateId = table.Column<string>(type: "TEXT", nullable: false),
                    Info_IsDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    Info_Created = table.Column<string>(type: "TEXT", nullable: false),
                    Info_Modified = table.Column<string>(type: "TEXT", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Added = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    User = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cabinets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Depth = table.Column<double>(type: "REAL", nullable: false),
                    PlateWidth = table.Column<double>(type: "REAL", nullable: false),
                    PlateHeight = table.Column<double>(type: "REAL", nullable: false),
                    ClearanceMm = table.Column<double>(type: "REAL", nullable: false),
                    EnclosurePartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    IpRating = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cabinets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cabinets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    CableType = table.Column<string>(type: "TEXT", nullable: false),
                    CoreCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CrossSectionMm2 = table.Column<double>(type: "REAL", nullable: false),
                    LengthM = table.Column<double>(type: "REAL", nullable: false),
                    Material = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Insulation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Shielded = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromLocation = table.Column<string>(type: "TEXT", nullable: false),
                    ToLocation = table.Column<string>(type: "TEXT", nullable: false),
                    FromDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ToDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Route = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    OuterDiameterMm = table.Column<double>(type: "REAL", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cables_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Calculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CalculationType = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    StandardProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    InputsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LinkedObjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calculations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UnitPrice = table.Column<double>(type: "REAL", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Supplier = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierReference = table.Column<string>(type: "TEXT", nullable: false),
                    StockReference = table.Column<string>(type: "TEXT", nullable: false),
                    WidthMm = table.Column<double>(type: "REAL", nullable: false),
                    HeightMm = table.Column<double>(type: "REAL", nullable: false),
                    DepthMm = table.Column<double>(type: "REAL", nullable: false),
                    IsDemoData = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    AlternativesJson = table.Column<string>(type: "TEXT", nullable: false),
                    DatasheetUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogProducts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", nullable: false),
                    Function = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TagLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Loads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Board = table.Column<string>(type: "TEXT", nullable: false),
                    Circuit = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PowerKw = table.Column<double>(type: "REAL", nullable: false),
                    PowerFactor = table.Column<double>(type: "REAL", nullable: false),
                    DemandFactor = table.Column<double>(type: "REAL", nullable: false),
                    VoltageV = table.Column<double>(type: "REAL", nullable: false),
                    CableLengthM = table.Column<double>(type: "REAL", nullable: false),
                    CableCrossSectionMm2 = table.Column<double>(type: "REAL", nullable: false),
                    ProtectionRatingA = table.Column<double>(type: "REAL", nullable: false),
                    ProtectionDeviceTag = table.Column<string>(type: "TEXT", nullable: false),
                    CableId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loads_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Motors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    PowerKw = table.Column<double>(type: "REAL", nullable: false),
                    VoltageV = table.Column<double>(type: "REAL", nullable: false),
                    Phases = table.Column<int>(type: "INTEGER", nullable: false),
                    FrequencyHz = table.Column<double>(type: "REAL", nullable: false),
                    RatedCurrentA = table.Column<double>(type: "REAL", nullable: false),
                    SpeedRpm = table.Column<double>(type: "REAL", nullable: false),
                    PowerFactor = table.Column<double>(type: "REAL", nullable: false),
                    Efficiency = table.Column<double>(type: "REAL", nullable: false),
                    StartingMethod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartingCurrentRatio = table.Column<double>(type: "REAL", nullable: false),
                    Duty = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    DatasheetUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectionDeviceTag = table.Column<string>(type: "TEXT", nullable: false),
                    CabinetTag = table.Column<string>(type: "TEXT", nullable: false),
                    PlcChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Motors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Motors_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlcStations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlcStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlcStations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Procurement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Supplier = table.Column<string>(type: "TEXT", nullable: false),
                    PurchaseOrder = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Procurement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Procurement_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectSymbols",
                columns: table => new
                {
                    SymbolId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    LibraryId = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectSymbols", x => new { x.ProjectId, x.SymbolId });
                    table.ForeignKey(
                        name: "FK_ProjectSymbols_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Revisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    FormatName = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", nullable: false),
                    Function = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    DrawingJson = table.Column<string>(type: "TEXT", nullable: false),
                    FieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Modified = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sheets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalStrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    TerminalPartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalStrips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalStrips_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValidationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    Errors = table.Column<int>(type: "INTEGER", nullable: false),
                    Warnings = table.Column<int>(type: "INTEGER", nullable: false),
                    Infos = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CabinetItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Face = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Depth = table.Column<double>(type: "REAL", nullable: false),
                    RailId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CabinetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CabinetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CabinetItems_Cabinets_CabinetId",
                        column: x => x.CabinetId,
                        principalTable: "Cabinets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CableCores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Marking = table.Column<string>(type: "TEXT", nullable: false),
                    WireId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CableId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CableCores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CableCores_Cables_CableId",
                        column: x => x.CableId,
                        principalTable: "Cables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlcRacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsRemote = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlcStationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlcRacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlcRacks_PlcStations_PlcStationId",
                        column: x => x.PlcStationId,
                        principalTable: "PlcStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Terminals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    JumperGroup = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SortIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    TerminalStripId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terminals_TerminalStrips_TerminalStripId",
                        column: x => x.TerminalStripId,
                        principalTable: "TerminalStrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlcModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slot = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TypeName = table.Column<string>(type: "TEXT", nullable: false),
                    PartNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    StartAddress = table.Column<int>(type: "INTEGER", nullable: false),
                    PlcRackId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlcModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlcModules_PlcRacks_PlcRackId",
                        column: x => x.PlcRackId,
                        principalTable: "PlcRacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlcChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SignalType = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SymbolInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TerminalReference = table.Column<string>(type: "TEXT", nullable: false),
                    PlcModuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlcChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlcChannels_PlcModules_PlcModuleId",
                        column: x => x.PlcModuleId,
                        principalTable: "PlcModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ProjectId",
                table: "Attachments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ProjectId",
                table: "AuditEvents",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CabinetItems_CabinetId",
                table: "CabinetItems",
                column: "CabinetId");

            migrationBuilder.CreateIndex(
                name: "IX_Cabinets_ProjectId",
                table: "Cabinets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CableCores_CableId",
                table: "CableCores",
                column: "CableId");

            migrationBuilder.CreateIndex(
                name: "IX_Cables_ProjectId",
                table: "Cables",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Calculations_ProjectId",
                table: "Calculations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_Manufacturer_PartNumber",
                table: "CatalogProducts",
                columns: new[] { "Manufacturer", "PartNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_ProjectId",
                table: "CatalogProducts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_ProjectId",
                table: "Connections",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_WireNumber",
                table: "Connections",
                column: "WireNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ProjectId",
                table: "Devices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Tag",
                table: "Devices",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_Loads_ProjectId",
                table: "Loads",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Motors_ProjectId",
                table: "Motors",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PlcChannels_Address",
                table: "PlcChannels",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_PlcChannels_PlcModuleId",
                table: "PlcChannels",
                column: "PlcModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_PlcModules_PlcRackId",
                table: "PlcModules",
                column: "PlcRackId");

            migrationBuilder.CreateIndex(
                name: "IX_PlcRacks_PlcStationId",
                table: "PlcRacks",
                column: "PlcStationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlcStations_ProjectId",
                table: "PlcStations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Procurement_ProjectId",
                table: "Procurement",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Info_Number",
                table: "Projects",
                column: "Info_Number");

            migrationBuilder.CreateIndex(
                name: "IX_Revisions_ProjectId",
                table: "Revisions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Sheets_ProjectId",
                table: "Sheets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_TerminalStripId",
                table: "Terminals",
                column: "TerminalStripId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalStrips_ProjectId",
                table: "TerminalStrips",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_ProjectId",
                table: "ValidationRuns",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CabinetItems");

            migrationBuilder.DropTable(
                name: "CableCores");

            migrationBuilder.DropTable(
                name: "Calculations");

            migrationBuilder.DropTable(
                name: "CatalogProducts");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Loads");

            migrationBuilder.DropTable(
                name: "Motors");

            migrationBuilder.DropTable(
                name: "PlcChannels");

            migrationBuilder.DropTable(
                name: "Procurement");

            migrationBuilder.DropTable(
                name: "ProjectSymbols");

            migrationBuilder.DropTable(
                name: "Revisions");

            migrationBuilder.DropTable(
                name: "Sheets");

            migrationBuilder.DropTable(
                name: "Terminals");

            migrationBuilder.DropTable(
                name: "ValidationRuns");

            migrationBuilder.DropTable(
                name: "Cabinets");

            migrationBuilder.DropTable(
                name: "Cables");

            migrationBuilder.DropTable(
                name: "PlcModules");

            migrationBuilder.DropTable(
                name: "TerminalStrips");

            migrationBuilder.DropTable(
                name: "PlcRacks");

            migrationBuilder.DropTable(
                name: "PlcStations");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
