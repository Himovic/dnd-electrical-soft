using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DND.Electrical.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutomationAndInstrumentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alarms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SetPoint = table.Column<double>(type: "REAL", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Deadband = table.Column<double>(type: "REAL", nullable: false),
                    DelayS = table.Column<double>(type: "REAL", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Class = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Consequence = table.Column<string>(type: "TEXT", nullable: false),
                    OperatorAction = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alarms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alarms_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CauseEffectMatrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", nullable: false),
                    CausesJson = table.Column<string>(type: "TEXT", nullable: false),
                    EffectsJson = table.Column<string>(type: "TEXT", nullable: false),
                    LinksJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CauseEffectMatrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CauseEffectMatrices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ControlLoops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MeasurementId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinalElementId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinalDeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Controller = table.Column<string>(type: "TEXT", nullable: false),
                    SetPoint = table.Column<double>(type: "REAL", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Gain = table.Column<double>(type: "REAL", nullable: true),
                    IntegralS = table.Column<double>(type: "REAL", nullable: true),
                    DerivativeS = table.Column<double>(type: "REAL", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlLoops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlLoops_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Variable = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Function = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Signal = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RangeMin = table.Column<double>(type: "REAL", nullable: false),
                    RangeMax = table.Column<double>(type: "REAL", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    Area = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessLine = table.Column<string>(type: "TEXT", nullable: false),
                    LoopId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlcChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JunctionBox = table.Column<string>(type: "TEXT", nullable: false),
                    HazardousZone = table.Column<string>(type: "TEXT", nullable: false),
                    ExMarking = table.Column<string>(type: "TEXT", nullable: false),
                    Manufacturer = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CalibrationDue = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Instruments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScadaNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Network = table.Column<string>(type: "TEXT", nullable: false),
                    Zone = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: false),
                    Redundant = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlcStationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScadaNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScadaNodes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alarms_ProjectId",
                table: "Alarms",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Alarms_Tag",
                table: "Alarms",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_CauseEffectMatrices_ProjectId",
                table: "CauseEffectMatrices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlLoops_ProjectId",
                table: "ControlLoops",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlLoops_Tag",
                table: "ControlLoops",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_ProjectId",
                table: "Instruments",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Instruments_Tag",
                table: "Instruments",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_ScadaNodes_ProjectId",
                table: "ScadaNodes",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alarms");

            migrationBuilder.DropTable(
                name: "CauseEffectMatrices");

            migrationBuilder.DropTable(
                name: "ControlLoops");

            migrationBuilder.DropTable(
                name: "Instruments");

            migrationBuilder.DropTable(
                name: "ScadaNodes");
        }
    }
}
