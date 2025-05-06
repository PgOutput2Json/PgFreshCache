using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PgFreshCache.TestApi.Migrations
{
    /// <inheritdoc />
    public partial class CreatePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE PUBLICATION cache_publication  
    FOR TABLE products, stores, stocks
    WITH (publish = 'insert, update, delete')
");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP PUBLICATION cache_publication");
        }
    }
}
