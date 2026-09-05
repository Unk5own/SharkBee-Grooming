using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetGrooming.Migrations;

public partial class AddEmailVerified : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing seeded/admin-created accounts are trusted; new registration
        // explicitly sets this value to false in AccountController.
        migrationBuilder.AddColumn<bool>(
            name: "EmailVerified",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EmailVerified",
            table: "Users");
    }
}
