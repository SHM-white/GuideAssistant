using Serilog;
using System.Timers;
using Timer = System.Timers.Timer;

namespace GuideAssistant.Services;

public interface ISubtitleProvider
{
    event Action<string>? SubtitleChanged;
    event Action<string>? DirectionWordDetected;
    Task<bool> StartAsync(string url);
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

    private static readonly string[] DirectionWords = {
        "东", "南", "西", "北",
        "东方向", "南方向", "西方向", "北方向",
        "左上", "右上", "左下", "右下",
        "左上方", "右上方", "左下方", "右下方",
        "东方", "南方", "西方", "北方",
        "前方", "后方", "左边", "右边",
        "左侧", "右侧", "上面", "下面"
    };

    public BilibiliCcProvider(BilibiliApi bilibiliApi)
    {
        _bilibiliApi = bilibiliApi;
    }

    public async Task<bool> StartAsync(string url)
    {
        var data = await _bilibiliApi.GetSubtitle(url);
        if (data == null || data.Items.Count == 0) return false;

        _subtitles = data.Items;
        Log.Information("Bilibili CC subtitle loaded: {Count} items", data.Items.Count);

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
        foreach (var word in DirectionWords)
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
