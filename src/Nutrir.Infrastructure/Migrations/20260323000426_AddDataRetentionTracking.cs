using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nutrir.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataRetentionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPurged",
                table: "Clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastInteractionDate",
                table: "Clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionExpiresAt",
                table: "Clients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionYears",
                table: "Clients",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            // Backfill LastInteractionDate from most recent interaction across all entity types
            migrationBuilder.Sql("""
                UPDATE "Clients" c
                SET "LastInteractionDate" = sub.latest,
                    "RetentionExpiresAt" = sub.latest + make_interval(years => c."RetentionYears")
                FROM (
                    SELECT client_id, MAX(latest) AS latest
                    FROM (
                        SELECT "ClientId" AS client_id, MAX("StartTime") AS latest FROM "Appointments" WHERE NOT "IsDeleted" GROUP BY "ClientId"
                        UNION ALL
                        SELECT "ClientId", MAX("UpdatedAt") FROM "MealPlans" WHERE NOT "IsDeleted" AND "UpdatedAt" IS NOT NULL GROUP BY "ClientId"
                        UNION ALL
                        SELECT "ClientId", MAX("EntryDate"::timestamp AT TIME ZONE 'UTC') FROM "ProgressEntries" WHERE NOT "IsDeleted" GROUP BY "ClientId"
                        UNION ALL
                        SELECT "ClientId", MAX("Timestamp") FROM "ConsentEvents" GROUP BY "ClientId"
                    ) interactions
                    GROUP BY client_id
                ) sub
                WHERE c."Id" = sub.client_id;
                """);

            // For clients with no interactions, use their CreatedAt
            migrationBuilder.Sql("""
                UPDATE "Clients"
                SET "LastInteractionDate" = "CreatedAt",
                    "RetentionExpiresAt" = "CreatedAt" + make_interval(years => "RetentionYears")
                WHERE "LastInteractionDate" IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "DataPurgeAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    PurgedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    ClientIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PurgedEntities = table.Column<string>(type: "text", nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataPurgeAuditLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataPurgeAuditLogs");

            migrationBuilder.DropColumn(
                name: "IsPurged",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LastInteractionDate",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "RetentionExpiresAt",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "RetentionYears",
                table: "Clients");
        }
    }
}
