using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class BookmarkService
{
    private readonly BookmarkRepository _repo;
    private readonly GameRepository _gameRepo;

    public event Action? BookmarksChanged;

    public BookmarkService(BookmarkRepository repo, GameRepository gameRepo)
    {
        _repo = repo;
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
}
