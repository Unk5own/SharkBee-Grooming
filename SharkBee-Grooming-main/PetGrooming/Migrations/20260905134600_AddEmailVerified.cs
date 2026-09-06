using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetGrooming.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "bit",
                nullable: false,
                // Existing seeded/admin-created accounts are trusted; new registration
                // explicitly sets this to false in AccountController.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Users");
        }
    }
}
