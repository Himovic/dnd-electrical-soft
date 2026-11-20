using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DND.Electrical.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThreeDLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Layouts3D",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CabinetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SceneJson = table.Column<string>(type: "TEXT", nullable: false),
                    Modified = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Layouts3D", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Layouts3D_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Layouts3D_ProjectId",
                table: "Layouts3D",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Layouts3D");
        }
    }
}
