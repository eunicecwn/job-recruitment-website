using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demo.Migrations
{
    /// <inheritdoc />
    public partial class updateSaveJob3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
    name: "SavedJobs",
    columns: table => new
    {
        Id = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
        JobSeekerId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
        JobId = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
        SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_SavedJobs", x => x.Id);
        table.ForeignKey(
            name: "FK_SavedJobs_Jobs_JobId",
            column: x => x.JobId,
            principalTable: "Jobs",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
        table.ForeignKey(
            name: "FK_SavedJobs_Users_JobSeekerId",
            column: x => x.JobSeekerId,
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    });

            migrationBuilder.CreateIndex(
                name: "IX_SavedJobs_JobId",
                table: "SavedJobs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedJobs_JobSeekerId_JobId",
                table: "SavedJobs",
                columns: new[] { "JobSeekerId", "JobId" },
                unique: true);
        
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
