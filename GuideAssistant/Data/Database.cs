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

            INSERT OR IGNORE INTO window_states (window_name, x, y, width, height, opacity, is_always_on_top)
            VALUES ('MainWindow', 100, 100, 960, 640, 0.9, 1);

            INSERT OR IGNORE INTO window_states (window_name, x, y, width, height, opacity, is_always_on_top)
            VALUES ('ToolbarWindow', 100, 0, 600, 40, 1.0, 1);
        ";
        cmd.ExecuteNonQuery();
        Log.Information("Database initialized");
    }
}
