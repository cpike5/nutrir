using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nutrir.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ComplianceHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "ProgressGoals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "ProgressEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "MealPlans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConsentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ConsentPurpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentEvents_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentEvents_ClientId",
                table: "ConsentEvents",
                column: "ClientId");

            // Audit log immutability trigger
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_audit_log_modification() RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'AuditLogEntries table is immutable.';
                END; $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_log_immutability
                BEFORE UPDATE OR DELETE ON "AuditLogEntries"
                FOR EACH ROW EXECUTE FUNCTION prevent_audit_log_modification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_log_immutability ON "AuditLogEntries";
                DROP FUNCTION IF EXISTS prevent_audit_log_modification();
                """);

            migrationBuilder.DropTable(
                name: "ConsentEvents");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "ProgressGoals");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "ProgressEntries");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "MealPlans");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Appointments");
        }
    }
}
