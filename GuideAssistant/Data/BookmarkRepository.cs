using Dapper;
using GuideAssistant.Models;

namespace GuideAssistant.Data;

public class BookmarkRepository
{
    private readonly Database _db;
    public BookmarkRepository(Database db) => _db = db;

    public List<Bookmark> GetAll()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Bookmark>(@"
            SELECT b.*, g.name AS GameName
            FROM bookmarks b
            LEFT JOIN games g ON b.game_id = g.id
            ORDER BY b.created_at DESC").ToList();
    }

    public List<Bookmark> GetByGame(int gameId)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Bookmark>(
            "SELECT b.*, g.name AS GameName FROM bookmarks b LEFT JOIN games g ON b.game_id = g.id WHERE b.game_id = @id ORDER BY b.created_at DESC",
            new { id = gameId }).ToList();
    }

    public List<Bookmark> Search(string keyword)
    {
        using var conn = _db.CreateConnection();
        return conn.Query<Bookmark>(@"
            SELECT b.*, g.name AS GameName FROM bookmarks b
            LEFT JOIN games g ON b.game_id = g.id
            WHERE b.title LIKE @k OR b.url LIKE @k OR b.tags LIKE @k
            ORDER BY b.created_at DESC", new { k = $"%{keyword}%" }).ToList();
    }

    public int Add(Bookmark b)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<int>(@"
            INSERT INTO bookmarks (game_id, title, url, favicon_url, tags, notes, is_favorite, created_at, updated_at)
            VALUES (@GameId, @Title, @Url, @FaviconUrl, @Tags, @Notes, @IsFavorite, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();", b);
    }

    public void Update(Bookmark b)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE bookmarks SET title=@Title, tags=@Tags, notes=@Notes, game_id=@GameId, updated_at=@UpdatedAt WHERE id=@Id", b);
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM bookmarks WHERE id=@id", new { id });
    }
}

public class GameRepository
{
    private readonly Database _db;
    public GameRepository(Database db) => _db = db;

    public List<GameConfig> GetAll()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<GameConfig>("SELECT * FROM game_configs ORDER BY game_name").ToList();
    }

    public int Add(GameConfig g)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<int>(@"
            INSERT INTO game_configs (game_name, game_path, helper_path, launch_args, auto_detect)
            VALUES (@GameName, @GamePath, @HelperPath, @LaunchArgs, @AutoDetect);
            SELECT last_insert_rowid();", g);
    }

    public void Update(GameConfig g)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE game_configs SET game_name=@GameName, game_path=@GamePath, helper_path=@HelperPath, launch_args=@LaunchArgs, auto_detect=@AutoDetect WHERE id=@Id", g);
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM game_configs WHERE id=@id", new { id });
    }
}
