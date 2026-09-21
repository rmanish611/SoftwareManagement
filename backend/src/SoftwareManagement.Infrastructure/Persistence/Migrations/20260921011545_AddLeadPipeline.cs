using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftwareManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Leads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaleReminderSentAtUtc",
                table: "Leads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HolidayDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsRecurring = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityType = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromStage = table.Column<int>(type: "int", nullable: true),
                    ToStage = table.Column<int>(type: "int", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadActivities", x => x.Id);
                    table.CheckConstraint("CK_LeadActivities_HasContent", "[Body] IS NOT NULL OR [ActivityType] IN (5, 6)");
                    table.CheckConstraint("CK_LeadActivities_StageChangeHasStages", "[ActivityType] <> 5 OR ([FromStage] IS NOT NULL AND [ToStage] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_LeadActivities_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Gstin = table.Column<string>(type: "nchar(15)", fixedLength: true, maxLength: 15, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Industry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Country = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_ContactId",
                table: "Leads",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_MergedIntoLeadId",
                table: "Leads",
                column: "MergedIntoLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_OrganisationId",
                table: "Leads",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Owner_Stage",
                table: "Leads",
                columns: new[] { "OwnerUserId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_SlaDue",
                table: "Leads",
                column: "SlaDueAtUtc",
                filter: "[FirstResponseAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_OrganisationId_Email",
                table: "Contacts",
                columns: new[] { "OrganisationId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_HolidayDate",
                table: "Holidays",
                column: "HolidayDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_Due",
                table: "LeadActivities",
                column: "DueAtUtc",
                filter: "[DueAtUtc] IS NOT NULL AND [IsCompleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LeadActivities_Lead",
                table: "LeadActivities",
                columns: new[] { "LeadId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_DisplayName",
                table: "Organisations",
                column: "DisplayName");

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Contacts_ContactId",
                table: "Leads",
                column: "ContactId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Leads_MergedIntoLeadId",
                table: "Leads",
                column: "MergedIntoLeadId",
                principalTable: "Leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Leads_Organisations_OrganisationId",
                table: "Leads",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Contacts_ContactId",
                table: "Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Leads_MergedIntoLeadId",
                table: "Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_Leads_Organisations_OrganisationId",
                table: "Leads");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "LeadActivities");

            migrationBuilder.DropTable(
                name: "Organisations");

            migrationBuilder.DropIndex(
                name: "IX_Leads_ContactId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_MergedIntoLeadId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_OrganisationId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_Owner_Stage",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_SlaDue",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ContactId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "StaleReminderSentAtUtc",
                table: "Leads");
        }
    }
}
