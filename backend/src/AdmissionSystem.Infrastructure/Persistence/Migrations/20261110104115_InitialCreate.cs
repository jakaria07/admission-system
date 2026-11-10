using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdmissionSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admission_classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    total_seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admission_classes", x => x.id);
                    table.CheckConstraint("ck_admission_classes_total_seats_positive", "total_seats > 0");
                });

            migrationBuilder.CreateTable(
                name: "admission_holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "character(17)", fixedLength: true, maxLength: 17, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admission_holds", x => x.id);
                    table.ForeignKey(
                        name: "FK_admission_holds_admission_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "admission_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admission_classes_name_normalized",
                table: "admission_classes",
                column: "name_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_class_status_expires",
                table: "admission_holds",
                columns: new[] { "class_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_status_expires",
                table: "admission_holds",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_admission_holds_student_created",
                table: "admission_holds",
                columns: new[] { "student_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_admission_holds_active_student_per_class",
                table: "admission_holds",
                columns: new[] { "class_id", "student_id" },
                unique: true,
                filter: "status IN ('HELD', 'CONFIRMED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admission_holds");

            migrationBuilder.DropTable(
                name: "admission_classes");
        }
    }
}
