using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PgFreshCache.TestApi.Migrations
{
    /// <inheritdoc />
    public partial class CreatePg2JsonSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA pgoutput2json");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP IF EXISTS SCHEMA pgoutput2json");
        }
    }
}
