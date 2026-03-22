using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nutrir.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentPrepNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrepNotes",
                table: "Appointments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrepNotes",
                table: "Appointments");
        }
    }
}
