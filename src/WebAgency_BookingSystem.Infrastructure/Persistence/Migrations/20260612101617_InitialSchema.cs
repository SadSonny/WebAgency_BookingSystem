using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    site_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    owner_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    timezone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "Europe/Rome"),
                    min_advance_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    min_cancellation_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    visible_days_ahead = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    staff_choice_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notification_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "email"),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ip_anonymized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    parallel_slots = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    buffer_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    buffer_position = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_services_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    specialization = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_api_keys_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_business_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_business_hours", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_business_hours_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_special_closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_from = table.Column<DateOnly>(type: "date", nullable: false),
                    date_to = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_special_closures", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_special_closures_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: false),
                    booking_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    customer_notes = table.Column<string>(type: "text", nullable: true),
                    gdpr_consent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    gdpr_consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cancellation_token = table.Column<Guid>(type: "uuid", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    no_show_marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    price_at_booking = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.ForeignKey(
                        name: "fk_bookings_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_business_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    break_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_business_hours", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_business_hours_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_business_hours_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_override = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_services_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_services_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_services_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_created_at",
                table: "audit_log",
                columns: new[] { "tenant_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_cancellation_token",
                table: "bookings",
                column: "cancellation_token");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_service_id",
                table: "bookings",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_staff_id_booking_date",
                table: "bookings",
                columns: new[] { "staff_id", "booking_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_booking_date",
                table: "bookings",
                columns: new[] { "tenant_id", "booking_date" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_tenant_id_service_id_booking_date_status",
                table: "bookings",
                columns: new[] { "tenant_id", "service_id", "booking_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id",
                table: "services",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_services_tenant_id_active",
                table: "services",
                columns: new[] { "tenant_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_tenant_id",
                table: "staff",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_tenant_id_active",
                table: "staff",
                columns: new[] { "tenant_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_business_hours_staff_id_day_of_week",
                table: "staff_business_hours",
                columns: new[] { "staff_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_business_hours_tenant_id",
                table: "staff_business_hours",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_services_service_id",
                table: "staff_services",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_services_staff_id_service_id",
                table: "staff_services",
                columns: new[] { "staff_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_services_tenant_id",
                table: "staff_services",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_keys_key_hash",
                table: "tenant_api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_keys_tenant_id",
                table: "tenant_api_keys",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_business_hours_tenant_id_day_of_week",
                table: "tenant_business_hours",
                columns: new[] { "tenant_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_special_closures_tenant_id_date_from_date_to",
                table: "tenant_special_closures",
                columns: new[] { "tenant_id", "date_from", "date_to" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "staff_business_hours");

            migrationBuilder.DropTable(
                name: "staff_services");

            migrationBuilder.DropTable(
                name: "tenant_api_keys");

            migrationBuilder.DropTable(
                name: "tenant_business_hours");

            migrationBuilder.DropTable(
                name: "tenant_special_closures");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
