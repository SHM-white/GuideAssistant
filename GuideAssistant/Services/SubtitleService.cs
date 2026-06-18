using System.Text.Json;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class SubtitleService
{
    private readonly BilibiliApi _bilibiliApi;
    private List<SubtitleItem>? _currentSubtitles;
    private string? _currentUrl;
    private Timer? _syncTimer;
    private double _currentTime;
    private string? _lastSubtitleContent;

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

    public SubtitleService(BilibiliApi api)
    {
        _bilibiliApi = api;
    }

    public async Task LoadSubtitle(string url)
    {
        if (url == _currentUrl) return;
        _currentUrl = url;
        _currentSubtitles = null;

        var data = await _bilibiliApi.GetSubtitle(url);
        if (data != null)
        {
            _currentSubtitles = data.Items;
            Log.Information("Subtitle loaded: {Count} items for {Url}", data.Items.Count, url);
        }
    }

    public void StartSync()
    {
        _syncTimer?.Dispose();
        _syncTimer = new Timer(SyncTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
    }

    public void StopSync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    public void UpdateTime(double currentTime)
    {
        _currentTime = currentTime;
    }

    private void SyncTick(object? state)
    {
        if (_currentSubtitles == null) return;

        var item = _currentSubtitles.FirstOrDefault(s => _currentTime >= s.From && _currentTime <= s.To);
        if (item != null)
        {
            if (item.Content != _lastSubtitleContent)
            {
                _lastSubtitleContent = item.Content;
                SubtitleChanged?.Invoke(item.Content);
                CheckDirectionWords(item.Content);
            }
        }
        else
        {
            _lastSubtitleContent = null;
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
        StopSync();
    }
}

public class DirectionService
{
    public static (double angle, string label)? ParseDirection(string text)
    {
        var dirMap = new Dictionary<string, (double angle, string label)>
        {
            { "东", (0, "东") },
            { "东方向", (0, "东") },
            { "东方", (0, "东") },
            { "南", (90, "南") },
            { "南方向", (90, "南") },
            { "南方", (90, "南") },
            { "西", (180, "西") },
            { "西方向", (180, "西") },
            { "西方", (180, "西") },
            { "北", (270, "北") },
            { "北方向", (270, "北") },
            { "北方", (270, "北") },
            { "右上", (315, "↗") },
            { "右上方", (315, "↗") },
            { "右下", (45, "↘") },
            { "右下方", (45, "↘") },
            { "左上", (225, "↖") },
            { "左上方", (225, "↖") },
            { "左下", (135, "↙") },
            { "左下方", (135, "↙") },
            { "前方", (270, "↑") },
            { "后方", (90, "↓") },
            { "左边", (180, "←") },
            { "右边", (0, "→") },
        };

        foreach (var kvp in dirMap)
        {
            if (text.Contains(kvp.Key))
                return kvp.Value;
        }
        return null;
    }
}
