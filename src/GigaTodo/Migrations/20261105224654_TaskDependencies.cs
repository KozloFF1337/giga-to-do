using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigaTodo.Migrations
{
    /// <inheritdoc />
    public partial class TaskDependencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sequential",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "Sequential",
                table: "schedules");

            migrationBuilder.DropColumn(
                name: "BarrierTaskId",
                table: "boards");

            migrationBuilder.AddColumn<Guid[]>(
                name: "DependencyIds",
                table: "tasks",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "DependencyIds",
                table: "schedules",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependencyIds",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "DependencyIds",
                table: "schedules");

            migrationBuilder.AddColumn<bool>(
                name: "Sequential",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Sequential",
                table: "schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "BarrierTaskId",
                table: "boards",
                type: "uuid",
                nullable: true);
        }
    }
}
