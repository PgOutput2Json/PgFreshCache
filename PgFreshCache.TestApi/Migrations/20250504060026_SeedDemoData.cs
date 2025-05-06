using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PgFreshCache.TestApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedDemoData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData("products", ["id", "name", "price"], new object[,] 
            {
                {1, "Orange", 10.10m },
                {2, "Plum", 20.20m },
                {3, "Watermelon", 30.30m },
            });

            migrationBuilder.InsertData("stores", ["id", "name", "address"], new object[,] 
            {
                { 1, "Store One", "254b, Oak Street" },
                { 2, "Store Two", "123a, Elm Street" },
                { 3, "Store Three", "921, Pinetree Street"},
            });

            migrationBuilder.InsertData("stocks", ["id", "store_id", "product_id", "quantity"], new object[,]
            {
                { 1, 1, 1, 10 },
                { 2, 1, 2, 20 },
                { 3, 1, 3, 30 },

                { 4, 2, 1, 40 },
                { 5, 2, 2, 50 },
                { 6, 2, 3, 60 },

                { 7, 3, 1, 70 },
                { 8, 3, 2, 80 },
                { 9, 3, 3, 90 },
            });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData("stocks", "id", [1, 2, 3, 4, 5, 6]);
            migrationBuilder.DeleteData("stores", "id", [1, 2, 3]);
            migrationBuilder.DeleteData("products", "id", [1, 2, 3]);

        }
    }
}
