using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nutrir.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicationLookupTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Medications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GenericName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Medications_Name",
                table: "Medications",
                column: "Name");

            // Replace case-sensitive unique index with case-insensitive functional index
            migrationBuilder.DropIndex(
                name: "IX_Medications_Name",
                table: "Medications");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Medications_Name_CI\" ON \"Medications\" (lower(\"Name\")) WHERE \"IsDeleted\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Medications_Name_CI\";");

            migrationBuilder.DropTable(
                name: "Medications");
        }
    }
}
