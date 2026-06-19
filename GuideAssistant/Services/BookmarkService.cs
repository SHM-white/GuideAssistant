using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class BookmarkService
{
    private readonly BookmarkRepository _repo;
    private readonly FolderRepository _folderRepo;
    private readonly GameRepository _gameRepo;

    public event Action? BookmarksChanged;

    public BookmarkService(BookmarkRepository repo, FolderRepository folderRepo, GameRepository gameRepo)
    {
        _repo = repo;
        _folderRepo = folderRepo;
        _gameRepo = gameRepo;
    }

    public List<Bookmark> GetAll() => _repo.GetAll();
    public List<Bookmark> Search(string keyword) => _repo.Search(keyword);
    public List<Bookmark> GetByGame(int gameId) => _repo.GetByGame(gameId);
    public int Add(Bookmark b)
    {
        var id = _repo.Add(b);
        BookmarksChanged?.Invoke();
        return id;
    }
    public void Update(Bookmark b) => _repo.Update(b);
    public void Delete(int id)
    {
        _repo.Delete(id);
        BookmarksChanged?.Invoke();
    }

    public List<GameConfig> GetAllGames() => _gameRepo.GetAll();

    public bool IsUrlBookmarked(string url)
    {
        return _repo.GetAll().Any(b => b.Url == url);
    }

    public int QuickAdd(string title, string url, string? tags = null, int? gameId = null)
    {
        // Fall back to URL if title is empty or just a placeholder
        if (string.IsNullOrWhiteSpace(title) || title == "新标签页")
            title = url;

        var b = new Bookmark
        {
            Title = title,
            Url = url,
            Tags = tags,
            GameId = gameId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        var id = _repo.Add(b);
        Log.Information("Bookmark added: {Title}", title);
        BookmarksChanged?.Invoke();
        return id;
    }

    // ── Folder Management ──────────────────────────────

    public List<BookmarkFolder> GetAllFolders() => _folderRepo.GetAll();

    public int AddFolder(string name)
    {
        var folder = new BookmarkFolder { Name = name, CreatedAt = DateTime.Now };
        var id = _folderRepo.Add(folder);
        BookmarksChanged?.Invoke();
        return id;
    }

    public void RenameFolder(int id, string name)
    {
        var folder = new BookmarkFolder { Id = id, Name = name };
        _folderRepo.Update(folder);
        BookmarksChanged?.Invoke();
    }

    public void DeleteFolder(int id)
    {
        _folderRepo.Delete(id);
        BookmarksChanged?.Invoke();
    }

    public void MoveBookmarkToFolder(int bookmarkId, int? folderId)
    {
        _repo.MoveToFolder(bookmarkId, folderId);
        BookmarksChanged?.Invoke();
    }
}
