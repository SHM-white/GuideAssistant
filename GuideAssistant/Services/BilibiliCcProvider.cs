using Serilog;
using System.Timers;
using Timer = System.Timers.Timer;

namespace GuideAssistant.Services;

public interface ISubtitleProvider
{
    event Action<string>? SubtitleChanged;
    event Action<string>? DirectionWordDetected;
    Task<bool> StartAsync(string url, SubtitleData? data = null);
    Task StopAsync();
    void UpdateTime(double currentTime);
}

public class BilibiliCcProvider : ISubtitleProvider, IDisposable
{
    private readonly BilibiliApi _bilibiliApi;
    private List<SubtitleItem>? _subtitles;
    private Timer? _syncTimer;
    private double _currentTime;
    private string? _lastContent;

    public event Action<string>? SubtitleChanged;
    public event Action<string>? DirectionWordDetected;

    private static readonly string[] s_directionWords = DirectionWords.All;

    public BilibiliCcProvider(BilibiliApi bilibiliApi)
    {
        _bilibiliApi = bilibiliApi;
    }

    public async Task<bool> StartAsync(string url, SubtitleData? data = null)
    {
        // Dispose old timer before replacing
        _syncTimer?.Stop();
        _syncTimer?.Dispose();

        if (data != null)
        {
            _subtitles = data.Items;
        }
        else
        {
            var result = await _bilibiliApi.GetSubtitle(url);
            if (result == null || result.Items.Count == 0) return false;
            _subtitles = result.Items;
        }
        _lastContent = null;
        Log.Information("Bilibili CC subtitle loaded: {Count} items", _subtitles!.Count);

        _syncTimer = new Timer(250) { AutoReset = true };
        _syncTimer.Elapsed += SyncTick;
        _syncTimer.Start();

        return true;
    }

    public Task StopAsync()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        _syncTimer = null;
        _subtitles = null;
        return Task.CompletedTask;
    }

    public void UpdateTime(double currentTime)
    {
        _currentTime = currentTime;
    }

    private void SyncTick(object? sender, ElapsedEventArgs e)
    {
        if (_subtitles == null) return;

        var item = _subtitles.FirstOrDefault(s => _currentTime >= s.From && _currentTime <= s.To);
        if (item != null)
        {
            if (item.Content != _lastContent)
            {
                _lastContent = item.Content;
                Log.Information("BilibiliCc: time={Time:F1}s text=\"{Text}\"", _currentTime, item.Content);
                SubtitleChanged?.Invoke(item.Content);
                CheckDirectionWords(item.Content);
            }
        }
        else
        {
            _lastContent = null;
        }
    }

    private void CheckDirectionWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var word in s_directionWords)
        {
            if (text.Contains(word))
            {
                DirectionWordDetected?.Invoke(word);
                return;
            }
        }
    }

    public void Dispose()
    {
        _syncTimer?.Stop();
        _syncTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
