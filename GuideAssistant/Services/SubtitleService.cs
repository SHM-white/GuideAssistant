using System.Text.Json;
using Serilog;

namespace GuideAssistant.Services;

public class SubtitleService
{
    private readonly BilibiliApi _bilibiliApi;
    private readonly SpeechRecognitionService _speechRecognition;
    private ISubtitleProvider? _activeProvider;
    private string? _currentUrl;
    private double _currentTime;
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

    public SubtitleService(BilibiliApi bilibiliApi, SpeechRecognitionService speechRecognition)
    {
        _bilibiliApi = bilibiliApi;
        _speechRecognition = speechRecognition;
    }

    public async Task LoadSubtitle(string url)
    {
        if (url == _currentUrl) return;
        _currentUrl = url;

        // Try CC subtitle first
        var data = await _bilibiliApi.GetSubtitle(url);
        if (data != null && data.Items.Count > 0)
        {
            await StopActiveProvider();
            var ccProvider = new BilibiliCcProvider(_bilibiliApi);
            var started = await ccProvider.StartAsync(url);
            if (started)
            {
                ccProvider.SubtitleChanged += OnProviderSubtitleChanged;
                ccProvider.DirectionWordDetected += OnProviderDirectionDetected;
                _activeProvider = ccProvider;
                Log.Information("SubtitleService: CC mode active ({Count} items)", data.Items.Count);
                return;
            }
        }

        // Fallback to speech recognition
        await StopActiveProvider();
        try
        {
            _speechRecognition.SpeechRecognized += OnSpeechRecognized;
            await _speechRecognition.StartAsync();
            Log.Information("SubtitleService: Speech recognition mode active");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubtitleService: Speech recognition unavailable");
            SubtitleChanged?.Invoke("（无可用字幕源）");
        }
    }

    public void OnSpeechRecognized(string text)
    {
        SubtitleChanged?.Invoke(text);
        CheckDirectionWords(text);
    }

    public void StartSync() { }

    public void StopSync() { }

    public void UpdateTime(double currentTime)
    {
        _currentTime = currentTime;
        if (_activeProvider != null)
        {
            _activeProvider.UpdateTime(currentTime);
        }
    }

    private void OnProviderSubtitleChanged(string text)
    {
        SubtitleChanged?.Invoke(text);
    }

    private void OnProviderDirectionDetected(string word)
    {
        DirectionWordDetected?.Invoke(word);
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

    private async Task StopActiveProvider()
    {
        if (_activeProvider is BilibiliCcProvider cc)
        {
            cc.SubtitleChanged -= OnProviderSubtitleChanged;
            cc.DirectionWordDetected -= OnProviderDirectionDetected;
            await cc.StopAsync();
        }
        _activeProvider = null;
    }

    public void Dispose()
    {
        _speechRecognition.SpeechRecognized -= OnSpeechRecognized;
        _ = StopActiveProvider();
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
