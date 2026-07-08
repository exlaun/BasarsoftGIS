using Basarsoft.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260708100000_RenameSeedPermissionsToEnglish")]
    public partial class RenameSeedPermissionsToEnglish : Migration
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
                    CREATE TEMP TABLE permission_rename_map (
                        old_name text PRIMARY KEY,
                        new_name text NOT NULL,
                        new_description text NOT NULL
                    ) ON COMMIT DROP;

                    INSERT INTO permission_rename_map (old_name, new_name, new_description) VALUES
                        ('point_ekleme', 'add_point', 'Add points to the map'),
                        ('line_ekleme', 'add_line', 'Add lines to the map'),
                        ('polygon_ekleme', 'add_polygon', 'Add polygons to the map'),
                        ('user_yonetimi', 'manage_users', 'Create, update, and delete users'),
                        ('rol_yonetimi', 'manage_roles', 'Create, update, and delete roles'),
                        ('yetki_yonetimi', 'manage_permissions', 'Manage permissions');

                    FOR mapping IN SELECT * FROM permission_rename_map LOOP
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
                        SELECT rp.role_id, new_id, rp.created_at, rp.is_deleted, rp.is_active, now()
                        FROM role_permissions rp
                        WHERE rp.permission_id = old_id
                          AND NOT EXISTS (
                              SELECT 1
                              FROM role_permissions existing
                              WHERE existing.role_id = rp.role_id
                                AND existing.permission_id = new_id
                          );

                        INSERT INTO user_permissions (user_id, permission_id, created_at, is_deleted, is_active, modified_date)
                        SELECT up.user_id, new_id, up.created_at, up.is_deleted, up.is_active, now()
                        FROM user_permissions up
                        WHERE up.permission_id = old_id
                          AND NOT EXISTS (
                              SELECT 1
                              FROM user_permissions existing
                              WHERE existing.user_id = up.user_id
                                AND existing.permission_id = new_id
                          );

                        DELETE FROM role_permissions WHERE permission_id = old_id;
                        DELETE FROM user_permissions WHERE permission_id = old_id;

                        UPDATE permissions
                        SET description = mapping.new_description,
                            is_deleted = false,
                            is_active = true,
                            modified_date = now()
                        WHERE id = new_id;

                        UPDATE permissions
                        SET is_deleted = true,
                            is_active = false,
                            modified_date = now()
                        WHERE id = old_id;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    mapping record;
                    old_id integer;
                    new_id integer;
                BEGIN
                    CREATE TEMP TABLE permission_rename_map (
                        old_name text PRIMARY KEY,
                        new_name text NOT NULL,
                        new_description text NOT NULL
                    ) ON COMMIT DROP;

                    INSERT INTO permission_rename_map (old_name, new_name, new_description) VALUES
                        ('add_point', 'point_ekleme', 'Haritaya nokta ekleme'),
                        ('add_line', 'line_ekleme', U&'Haritaya \00E7izgi ekleme'),
                        ('add_polygon', 'polygon_ekleme', 'Haritaya poligon ekleme'),
                        ('manage_users', 'user_yonetimi', U&'Kullan\0131c\0131 ekleme/\00E7\0131karma/g\00FCncelleme'),
                        ('manage_roles', 'rol_yonetimi', U&'Rol ekleme/\00E7\0131karma/silme'),
                        ('manage_permissions', 'yetki_yonetimi', U&'Yetki (permission) y\00F6netimi');

                    FOR mapping IN SELECT * FROM permission_rename_map LOOP
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
                        SELECT rp.role_id, new_id, rp.created_at, rp.is_deleted, rp.is_active, now()
                        FROM role_permissions rp
                        WHERE rp.permission_id = old_id
                          AND NOT EXISTS (
                              SELECT 1
                              FROM role_permissions existing
                              WHERE existing.role_id = rp.role_id
                                AND existing.permission_id = new_id
                          );

                        INSERT INTO user_permissions (user_id, permission_id, created_at, is_deleted, is_active, modified_date)
                        SELECT up.user_id, new_id, up.created_at, up.is_deleted, up.is_active, now()
                        FROM user_permissions up
                        WHERE up.permission_id = old_id
                          AND NOT EXISTS (
                              SELECT 1
                              FROM user_permissions existing
                              WHERE existing.user_id = up.user_id
                                AND existing.permission_id = new_id
                          );

                        DELETE FROM role_permissions WHERE permission_id = old_id;
                        DELETE FROM user_permissions WHERE permission_id = old_id;

                        UPDATE permissions
                        SET description = mapping.new_description,
                            is_deleted = false,
                            is_active = true,
                            modified_date = now()
                        WHERE id = new_id;

                        UPDATE permissions
                        SET is_deleted = true,
                            is_active = false,
                            modified_date = now()
                        WHERE id = old_id;
                    END LOOP;
                END $$;
                """);
        }
    }
}
