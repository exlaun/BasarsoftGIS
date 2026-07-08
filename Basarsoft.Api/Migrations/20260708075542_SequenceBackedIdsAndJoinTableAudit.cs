using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class SequenceBackedIdsAndJoinTableAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_permissions",
                table: "user_permissions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_role_permissions",
                table: "role_permissions");

            migrationBuilder.CreateSequence<int>(
                name: "seq_permissions");

            migrationBuilder.CreateSequence<int>(
                name: "seq_role_permissions");

            migrationBuilder.CreateSequence<int>(
                name: "seq_roles");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_line");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_point");

            migrationBuilder.CreateSequence<int>(
                name: "seq_tbl_polygon");

            migrationBuilder.CreateSequence<int>(
                name: "seq_user_permissions");

            migrationBuilder.CreateSequence<int>(
                name: "seq_user_roles");

            migrationBuilder.CreateSequence<int>(
                name: "seq_users");

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_users')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "user_roles",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_user_roles')");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "user_roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "user_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "user_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "user_roles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "user_permissions",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_user_permissions')");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "user_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "user_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "user_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "user_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_polygon",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_tbl_polygon')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_point",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_tbl_point')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_line",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_tbl_line')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "roles",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_roles')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "role_permissions",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_role_permissions')");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "role_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "role_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "role_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_date",
                table: "role_permissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "permissions",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('seq_permissions')",
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_permissions",
                table: "user_permissions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_role_permissions",
                table: "role_permissions",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_id",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_permissions_user_id_permission_id",
                table: "user_permissions",
                columns: new[] { "user_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_id_permission_id",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" },
                unique: true);

            // Pre-existing rows kept their identity-generated ids, so each sequence must start past the
            // current MAX(id) or the first insert would collide with an existing primary key. The join
            // tables need no setval: their id columns were just added and the volatile nextval default
            // ran per-row during the table rewrite, advancing those sequences already.
            migrationBuilder.Sql("""
                SELECT setval('seq_users', COALESCE((SELECT MAX(id) FROM users), 0) + 1, false);
                SELECT setval('seq_roles', COALESCE((SELECT MAX(id) FROM roles), 0) + 1, false);
                SELECT setval('seq_permissions', COALESCE((SELECT MAX(id) FROM permissions), 0) + 1, false);
                SELECT setval('seq_tbl_point', COALESCE((SELECT MAX(id) FROM tbl_point), 0) + 1, false);
                SELECT setval('seq_tbl_line', COALESCE((SELECT MAX(id) FROM tbl_line), 0) + 1, false);
                SELECT setval('seq_tbl_polygon', COALESCE((SELECT MAX(id) FROM tbl_polygon), 0) + 1, false);
                """);

            // Backfill the new audit columns on existing join rows: links that predate the columns are
            // live links, and the timestamp columns were added with the DateTime.MinValue placeholder.
            migrationBuilder.Sql("""
                UPDATE user_roles SET is_active = true, created_at = now(), modified_date = now();
                UPDATE user_permissions SET is_active = true, created_at = now(), modified_date = now();
                UPDATE role_permissions SET is_active = true, created_at = now(), modified_date = now();
                """);

            // Tie each sequence to the column it serves so dropping a table drops its sequence too.
            migrationBuilder.Sql("""
                ALTER SEQUENCE seq_users OWNED BY users.id;
                ALTER SEQUENCE seq_roles OWNED BY roles.id;
                ALTER SEQUENCE seq_permissions OWNED BY permissions.id;
                ALTER SEQUENCE seq_tbl_point OWNED BY tbl_point.id;
                ALTER SEQUENCE seq_tbl_line OWNED BY tbl_line.id;
                ALTER SEQUENCE seq_tbl_polygon OWNED BY tbl_polygon.id;
                ALTER SEQUENCE seq_user_roles OWNED BY user_roles.id;
                ALTER SEQUENCE seq_user_permissions OWNED BY user_permissions.id;
                ALTER SEQUENCE seq_role_permissions OWNED BY role_permissions.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Detach sequence ownership first: otherwise dropping the join tables' id columns below
            // would auto-drop their owned sequences and the explicit DropSequence calls would fail.
            migrationBuilder.Sql("""
                ALTER SEQUENCE seq_users OWNED BY NONE;
                ALTER SEQUENCE seq_roles OWNED BY NONE;
                ALTER SEQUENCE seq_permissions OWNED BY NONE;
                ALTER SEQUENCE seq_tbl_point OWNED BY NONE;
                ALTER SEQUENCE seq_tbl_line OWNED BY NONE;
                ALTER SEQUENCE seq_tbl_polygon OWNED BY NONE;
                ALTER SEQUENCE seq_user_roles OWNED BY NONE;
                ALTER SEQUENCE seq_user_permissions OWNED BY NONE;
                ALTER SEQUENCE seq_role_permissions OWNED BY NONE;
                """);

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "ix_user_roles_user_id_role_id",
                table: "user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_permissions",
                table: "user_permissions");

            migrationBuilder.DropIndex(
                name: "ix_user_permissions_user_id_permission_id",
                table: "user_permissions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_role_permissions",
                table: "role_permissions");

            migrationBuilder.DropIndex(
                name: "ix_role_permissions_role_id_permission_id",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "id",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "id",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "user_permissions");

            migrationBuilder.DropColumn(
                name: "id",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "modified_date",
                table: "role_permissions");

            migrationBuilder.DropSequence(
                name: "seq_permissions");

            migrationBuilder.DropSequence(
                name: "seq_role_permissions");

            migrationBuilder.DropSequence(
                name: "seq_roles");

            migrationBuilder.DropSequence(
                name: "seq_tbl_line");

            migrationBuilder.DropSequence(
                name: "seq_tbl_point");

            migrationBuilder.DropSequence(
                name: "seq_tbl_polygon");

            migrationBuilder.DropSequence(
                name: "seq_user_permissions");

            migrationBuilder.DropSequence(
                name: "seq_user_roles");

            migrationBuilder.DropSequence(
                name: "seq_users");

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "users",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_users')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_polygon",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_tbl_polygon')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_point",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_tbl_point')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "tbl_line",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_tbl_line')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "roles",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_roles')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "permissions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValueSql: "nextval('seq_permissions')")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles",
                columns: new[] { "user_id", "role_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_permissions",
                table: "user_permissions",
                columns: new[] { "user_id", "permission_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_role_permissions",
                table: "role_permissions",
                columns: new[] { "role_id", "permission_id" });
        }
    }
}
