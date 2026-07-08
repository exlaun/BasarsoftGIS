using Basarsoft.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260708103000_HardDeleteLegacySeedPermissions")]
    public partial class HardDeleteLegacySeedPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    mapping record;
                    old_id integer;
                    new_id integer;
                BEGIN
                    CREATE TEMP TABLE legacy_seed_permission_map (
                        old_name text PRIMARY KEY,
                        new_name text NOT NULL,
                        new_description text NOT NULL
                    ) ON COMMIT DROP;

                    INSERT INTO legacy_seed_permission_map (old_name, new_name, new_description) VALUES
                        ('point_ekleme', 'add_point', 'Add points to the map'),
                        ('line_ekleme', 'add_line', 'Add lines to the map'),
                        ('polygon_ekleme', 'add_polygon', 'Add polygons to the map'),
                        ('user_yonetimi', 'manage_users', 'Create, update, and delete users'),
                        ('rol_yonetimi', 'manage_roles', 'Create, update, and delete roles'),
                        ('yetki_yonetimi', 'manage_permissions', 'Manage permissions');

                    FOR mapping IN SELECT * FROM legacy_seed_permission_map LOOP
                        SELECT id INTO old_id
                        FROM permissions
                        WHERE name = mapping.old_name
                        ORDER BY is_deleted, id
                        LIMIT 1;

                        SELECT id INTO new_id
                        FROM permissions
                        WHERE name = mapping.new_name
                        ORDER BY is_deleted, id
                        LIMIT 1;

                        IF old_id IS NULL THEN
                            IF new_id IS NOT NULL THEN
                                UPDATE permissions
                                SET description = mapping.new_description,
                                    is_deleted = false,
                                    is_active = true,
                                    modified_date = now()
                                WHERE id = new_id;
                            END IF;
                            CONTINUE;
                        END IF;

                        IF new_id IS NULL THEN
                            UPDATE permissions
                            SET name = mapping.new_name,
                                description = mapping.new_description,
                                is_deleted = false,
                                is_active = true,
                                modified_date = now()
                            WHERE id = old_id;
                            CONTINUE;
                        END IF;

                        INSERT INTO role_permissions (role_id, permission_id, created_at, is_deleted, is_active, modified_date)
                        SELECT rp.role_id, new_id, rp.created_at, false, true, now()
                        FROM role_permissions rp
                        WHERE rp.permission_id = old_id
                        ON CONFLICT (role_id, permission_id) DO UPDATE
                        SET is_deleted = false,
                            is_active = true,
                            modified_date = now();

                        INSERT INTO user_permissions (user_id, permission_id, created_at, is_deleted, is_active, modified_date)
                        SELECT up.user_id, new_id, up.created_at, false, true, now()
                        FROM user_permissions up
                        WHERE up.permission_id = old_id
                        ON CONFLICT (user_id, permission_id) DO UPDATE
                        SET is_deleted = false,
                            is_active = true,
                            modified_date = now();

                        DELETE FROM role_permissions WHERE permission_id = old_id;
                        DELETE FROM user_permissions WHERE permission_id = old_id;

                        UPDATE permissions
                        SET description = mapping.new_description,
                            is_deleted = false,
                            is_active = true,
                            modified_date = now()
                        WHERE id = new_id;

                        DELETE FROM permissions WHERE id = old_id;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no-op: the removed rows are obsolete seed data, and their grants have been
            // transferred to the English permissions.
        }
    }
}
