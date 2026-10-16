using System.IO;
using Microsoft.Data.Sqlite;
using Serilog;

namespace GuideAssistant.Data;

public class Database
{
    private readonly string _connectionString;

    public Database()
    {
        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuideAssistant", "data.db");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Initialize()
    {
        using var conn = CreateConnection();
        var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS games (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                icon_path TEXT,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS bookmarks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                game_id INTEGER REFERENCES games(id) ON DELETE CASCADE,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                favicon_url TEXT,
                tags TEXT,
                notes TEXT,
                is_favorite INTEGER DEFAULT 0,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS game_configs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                game_name TEXT NOT NULL,
                game_path TEXT NOT NULL,
                helper_path TEXT,
                launch_args TEXT,
                auto_detect INTEGER DEFAULT 1,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS hotkey_profiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                game_id INTEGER REFERENCES games(id),
                game_config_id INTEGER REFERENCES game_configs(id),
                is_default INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS hotkey_bindings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id INTEGER REFERENCES hotkey_profiles(id),
                action_name TEXT NOT NULL,
                action_display TEXT,
                modifiers INTEGER,
                virtual_key INTEGER,
                display_text TEXT,
                UNIQUE(profile_id, action_name)
            );

            CREATE TABLE IF NOT EXISTS window_states (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                window_name TEXT NOT NULL UNIQUE,
                x REAL, y REAL, width REAL, height REAL,
                opacity REAL DEFAULT 0.9,
                is_always_on_top INTEGER DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );

            CREATE TABLE IF NOT EXISTS bookmark_folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                sort_order INTEGER DEFAULT 0,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            INSERT OR IGNORE INTO window_states (window_name, x, y, width, height, opacity, is_always_on_top)
            VALUES ('MainWindow', 100, 100, 960, 640, 0.9, 1);

            INSERT OR IGNORE INTO window_states (window_name, x, y, width, height, opacity, is_always_on_top)
            VALUES ('ToolbarWindow', 100, 0, 600, 40, 1.0, 1);
        ";
        cmd.ExecuteNonQuery();

        // One-time rebuild of bookmarks table
        RebuildBookmarksIfNeeded(conn);
        EnsureGameHotkeyProfiles(conn);
        EnsureBookmarkGameConfigs(conn);

        Log.Information("Database initialized");
    }

    private static void EnsureGameHotkeyProfiles(SqliteConnection conn)
    {
        using var columns = conn.CreateCommand();
        columns.CommandText = "PRAGMA table_info(hotkey_profiles)";
        using var reader = columns.ExecuteReader();
        var hasColumn = false;
        while (reader.Read())
            hasColumn |= string.Equals(reader.GetString(1), "game_config_id", StringComparison.OrdinalIgnoreCase);
        reader.Close();

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (!hasColumn)
        {
            cmd.CommandText = "ALTER TABLE hotkey_profiles ADD COLUMN game_config_id INTEGER REFERENCES game_configs(id)";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_hotkey_profiles_game_config ON hotkey_profiles(game_config_id) WHERE game_config_id IS NOT NULL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO settings(key, value) VALUES('db_version', '4') ON CONFLICT(key) DO UPDATE SET value='4' WHERE CAST(settings.value AS INTEGER) < 4";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static void RebuildBookmarksIfNeeded(SqliteConnection conn)
    {
        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "SELECT value FROM settings WHERE key = 'db_version'";
        var currentVersion = versionCmd.ExecuteScalar() as string;
        if (int.TryParse(currentVersion, out var version) && version > 5)
        {
            Log.Warning("Database version {Version} is newer than this application", version);
            return;
        }

        using var columnsCmd = conn.CreateCommand();
        columnsCmd.CommandText = "PRAGMA table_info(bookmarks)";
        using var reader = columnsCmd.ExecuteReader();
        bool hasFolderId = false;
        while (reader.Read())
            if (string.Equals(reader.GetString(1), "folder_id", StringComparison.OrdinalIgnoreCase))
                hasFolderId = true;
        reader.Close();
        if (version >= 3 && hasFolderId) return;

        // Never drop a user's existing bookmarks, even if version metadata is missing.
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM bookmarks";
        if (Convert.ToInt64(countCmd.ExecuteScalar()) > 0)
        {
            var backupPath = conn.DataSource + ".pre-v3.bak";
            if (!File.Exists(backupPath))
            {
                using var backup = new SqliteConnection($"Data Source={backupPath}");
                backup.Open();
                conn.BackupDatabase(backup);
                Log.Information("Database migration backup saved at {Path}", backupPath);
            }
        }

        using var tx = conn.BeginTransaction();
        if (!hasFolderId)
        {
            using var addColumn = conn.CreateCommand();
            addColumn.Transaction = tx;
            addColumn.CommandText = "ALTER TABLE bookmarks ADD COLUMN folder_id INTEGER REFERENCES bookmark_folders(id)";
            addColumn.ExecuteNonQuery();
        }
        using var update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ('db_version', '3')";
        update.ExecuteNonQuery();
        tx.Commit();
        Log.Information("Database upgraded to version 3 without deleting bookmarks");
    }

    private static void EnsureBookmarkGameConfigs(SqliteConnection conn)
    {
        using var columns = conn.CreateCommand();
        columns.CommandText = "PRAGMA table_info(bookmarks)";
        using var reader = columns.ExecuteReader();
        bool hasColumn = false;
        while (reader.Read())
            hasColumn |= reader.GetString(1) == "game_config_id";
        reader.Close();
        if (hasColumn) return;

        // Preserve a recoverable copy before changing existing game associations.
        var backupPath = conn.DataSource + ".pre-v5.bak";
        if (!File.Exists(backupPath))
        {
            using var backup = new SqliteConnection($"Data Source={backupPath}");
            backup.Open();
            conn.BackupDatabase(backup);
        }

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            ALTER TABLE bookmarks ADD COLUMN game_config_id INTEGER REFERENCES game_configs(id) ON DELETE SET NULL;
            UPDATE bookmarks SET game_config_id = (
                SELECT c.id FROM game_configs c JOIN games g ON c.game_name = g.name
                WHERE g.id = bookmarks.game_id
                GROUP BY g.id HAVING COUNT(*) = 1
            ) WHERE game_id IN (SELECT id FROM games);
            UPDATE bookmarks SET game_config_id = game_id
                WHERE game_id NOT IN (SELECT id FROM games)
                AND game_id IN (SELECT id FROM game_configs);
            UPDATE bookmarks SET game_id = NULL WHERE game_config_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_bookmarks_game_config ON bookmarks(game_config_id);
            INSERT INTO settings(key, value) VALUES('db_version', '5')
                ON CONFLICT(key) DO UPDATE SET value='5' WHERE CAST(settings.value AS INTEGER) < 5;";
        cmd.ExecuteNonQuery();
        tx.Commit();
        Log.Information("Database upgraded to version 5 with game-specific bookmarks");
    }
}
