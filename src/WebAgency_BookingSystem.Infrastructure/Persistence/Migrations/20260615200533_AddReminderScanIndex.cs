using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderScanIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_reminder_sent_at_booking_date",
                table: "bookings",
                columns: new[] { "status", "reminder_sent_at", "booking_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_status_reminder_sent_at_booking_date",
                table: "bookings");
        }
    }
}
