using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class ImproveRiskPredictionFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EntertainmentSpendPercentage",
                table: "RiskPredictions",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FoodSpendPercentage",
                table: "RiskPredictions",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MoMSpendChangePercentage",
                table: "RiskPredictions",
                type: "decimal(7,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TopCategory",
                table: "RiskPredictions",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "TopCategoryPercentage",
                table: "RiskPredictions",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntertainmentSpendPercentage",
                table: "RiskPredictions");

            migrationBuilder.DropColumn(
                name: "FoodSpendPercentage",
                table: "RiskPredictions");

            migrationBuilder.DropColumn(
                name: "MoMSpendChangePercentage",
                table: "RiskPredictions");

            migrationBuilder.DropColumn(
                name: "TopCategory",
                table: "RiskPredictions");

            migrationBuilder.DropColumn(
                name: "TopCategoryPercentage",
                table: "RiskPredictions");
        }
    }
}
