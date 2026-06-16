using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeEmailGlobalAndAddSecurityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_email",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "security_stamp",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // WHY: existing rows would get Guid.Empty for security_stamp; we randomize them so the stamp
            // claim won't collide between users when JWT validation checks the stamp.
            migrationBuilder.Sql("UPDATE users SET security_stamp = gen_random_uuid() WHERE security_stamp = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id",
                table: "users",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "activated_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);
        }
    }
}
