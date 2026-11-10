using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlow.Mvc.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentTaskCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "TaskCodeSequence");

            migrationBuilder.AddColumn<long>(
                name: "CodeNumber",
                table: "Tasks",
                type: "bigint",
                nullable: true);

            // ALTER TABLE locks are retained until this migration transaction commits.
            // nextval is used for both existing and future rows; deleted numbers are never recycled.
            migrationBuilder.Sql("""
                DO $$
                DECLARE existing_task RECORD;
                BEGIN
                    FOR existing_task IN SELECT "Id" FROM "Tasks" ORDER BY "CreatedDate", "Id"
                    LOOP
                        UPDATE "Tasks" SET "CodeNumber" = nextval('"TaskCodeSequence"')
                        WHERE "Id" = existing_task."Id";
                    END LOOP;
                END $$;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "CodeNumber", table: "Tasks", type: "bigint", nullable: false,
                defaultValueSql: "nextval('\"TaskCodeSequence\"')",
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CodeNumber",
                table: "Tasks",
                column: "CodeNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_CodeNumber",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CodeNumber",
                table: "Tasks");

            migrationBuilder.DropSequence(
                name: "TaskCodeSequence");
        }
    }
}
