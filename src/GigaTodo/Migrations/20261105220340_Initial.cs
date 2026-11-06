using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GigaTodo.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExecutorLimit = table.Column<int>(type: "integer", nullable: false),
                    CriticLimit = table.Column<int>(type: "integer", nullable: false),
                    ReworkLimit = table.Column<int>(type: "integer", nullable: false),
                    BarrierTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultRepositoriesJson = table.Column<string>(type: "text", nullable: false),
                    ExecutorInstructions = table.Column<string>(type: "text", nullable: false),
                    CriticInstructions = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    ApprovalMode = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int[]>(type: "integer[]", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    NextAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Sequential = table.Column<bool>(type: "boolean", nullable: false),
                    HumanReview = table.Column<bool>(type: "boolean", nullable: false),
                    RepositoriesJson = table.Column<string>(type: "text", nullable: false),
                    LinksJson = table.Column<string>(type: "text", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedules_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    DedupeKey = table.Column<string>(type: "text", nullable: true),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false),
                    Sequential = table.Column<bool>(type: "boolean", nullable: false),
                    HumanReview = table.Column<bool>(type: "boolean", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    Blocker = table.Column<string>(type: "text", nullable: true),
                    Reworks = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RepositoriesJson = table.Column<string>(type: "text", nullable: false),
                    LinksJson = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    ExecutorSession = table.Column<string>(type: "text", nullable: true),
                    CriticSession = table.Column<string>(type: "text", nullable: true),
                    PendingRole = table.Column<int>(type: "integer", nullable: true),
                    ActiveRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tasks_boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "slots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slots_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_events_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Runner = table.Column<string>(type: "text", nullable: false),
                    ProfileJson = table.Column<string>(type: "text", nullable: false),
                    PendingResultJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_runs_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_TaskId_Id",
                table: "events",
                columns: new[] { "TaskId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_runs_TaskId_State",
                table: "runs",
                columns: new[] { "TaskId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_runs_TokenHash",
                table: "runs",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedules_BoardId",
                table: "schedules",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_slots_ScheduleId_At",
                table: "slots",
                columns: new[] { "ScheduleId", "At" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_BoardId_Stage_Position",
                table: "tasks",
                columns: new[] { "BoardId", "Stage", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_DedupeKey",
                table: "tasks",
                column: "DedupeKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "slots");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "schedules");

            migrationBuilder.DropTable(
                name: "boards");
        }
    }
}
