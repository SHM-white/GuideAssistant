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
            INSERT INTO bookmarks (game_id, folder_id, title, url, favicon_url, tags, notes, is_favorite, created_at, updated_at)
            VALUES (@GameId, @FolderId, @Title, @Url, @FaviconUrl, @Tags, @Notes, @IsFavorite, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();", b);
    }

    public void Update(Bookmark b)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE bookmarks SET title=@Title, tags=@Tags, notes=@Notes, game_id=@GameId, folder_id=@FolderId, updated_at=@UpdatedAt WHERE id=@Id", b);
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM bookmarks WHERE id=@id", new { id });
    }

    public void MoveToFolder(int bookmarkId, int? folderId)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE bookmarks SET folder_id=@folderId WHERE id=@id", new { id = bookmarkId, folderId });
    }
}

public class FolderRepository
{
    private readonly Database _db;
    public FolderRepository(Database db) => _db = db;

    public List<BookmarkFolder> GetAll()
    {
        using var conn = _db.CreateConnection();
        return conn.Query<BookmarkFolder>("SELECT * FROM bookmark_folders ORDER BY sort_order, name").ToList();
    }

    public int Add(BookmarkFolder f)
    {
        using var conn = _db.CreateConnection();
        return conn.ExecuteScalar<int>(@"
            INSERT INTO bookmark_folders (name, sort_order, created_at)
            VALUES (@Name, @SortOrder, @CreatedAt);
            SELECT last_insert_rowid();", f);
    }

    public void Update(BookmarkFolder f)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("UPDATE bookmark_folders SET name=@Name, sort_order=@SortOrder WHERE id=@Id", f);
    }

    public void Delete(int id)
    {
        using var conn = _db.CreateConnection();
        // Unlink bookmarks before deleting folder
        conn.Execute("UPDATE bookmarks SET folder_id=NULL WHERE folder_id=@id", new { id });
        conn.Execute("DELETE FROM bookmark_folders WHERE id=@id", new { id });
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
