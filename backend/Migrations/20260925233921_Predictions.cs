using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokemonTCG.API.Migrations
{
    /// <inheritdoc />
    public partial class Predictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "artist",
                table: "cards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "national_dex",
                table: "cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "set_printed_total",
                table: "cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "set_released",
                table: "cards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "set_series",
                table: "cards",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "subtypes",
                table: "cards",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "supertype",
                table: "cards",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "prediction_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: true),
                    horizon_days = table.Column<int>(type: "integer", nullable: false),
                    training_rows = table.Column<int>(type: "integer", nullable: false),
                    validation_rows = table.Column<int>(type: "integer", nullable: false),
                    mae = table.Column<double>(type: "double precision", nullable: true),
                    baseline_mae = table.Column<double>(type: "double precision", nullable: true),
                    direction_accuracy = table.Column<double>(type: "double precision", nullable: true),
                    residual_low = table.Column<double>(type: "double precision", nullable: true),
                    residual_high = table.Column<double>(type: "double precision", nullable: true),
                    importance = table.Column<string>(type: "jsonb", nullable: true),
                    predictions = table.Column<int>(type: "integer", nullable: false),
                    checkpoint = table.Column<bool>(type: "boolean", nullable: false),
                    realized_count = table.Column<int>(type: "integer", nullable: true),
                    realized_mae = table.Column<double>(type: "double precision", nullable: true),
                    realized_baseline_mae = table.Column<double>(type: "double precision", nullable: true),
                    realized_direction_accuracy = table.Column<double>(type: "double precision", nullable: true),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prediction_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_predictions",
                columns: table => new
                {
                    run_id = table.Column<int>(type: "integer", nullable: false),
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    predicted = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    low = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    high = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    change_percent = table.Column<double>(type: "double precision", nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_predictions", x => new { x.run_id, x.card_id, x.variant });
                    table.ForeignKey(
                        name: "FK_price_predictions_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_price_predictions_prediction_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_predictions_card_id_run_id",
                table: "price_predictions",
                columns: new[] { "card_id", "run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_predictions");

            migrationBuilder.DropTable(
                name: "prediction_runs");

            migrationBuilder.DropColumn(
                name: "artist",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "national_dex",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "set_printed_total",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "set_released",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "set_series",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "subtypes",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "supertype",
                table: "cards");
        }
    }
}
