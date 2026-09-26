using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokemonTCG.API.Migrations
{
    /// <inheritdoc />
    public partial class SetGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "set_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_set_goals", x => x.id);
                    table.ForeignKey(
                        name: "FK_set_goals_sets_set_id",
                        column: x => x.set_id,
                        principalTable: "sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_set_goals_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_set_goals_set_id",
                table: "set_goals",
                column: "set_id");

            migrationBuilder.CreateIndex(
                name: "IX_set_goals_user_id_set_id",
                table: "set_goals",
                columns: new[] { "user_id", "set_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "set_goals");
        }
    }
}
