using Serilog;

namespace GuideAssistant.Services;

public class SubtitleService
{
    private readonly BilibiliApi _bilibiliApi;
    private readonly SpeechRecognitionService _speechRecognition;
    private ISubtitleProvider? _activeProvider;
    private Microsoft.Web.WebView2.Core.CoreWebView2? _coreWebView;
    private double _currentTime;
    private readonly Dictionary<string, SubtitleData> _subtitleCache = new();
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

    public void SetCoreWebView(Microsoft.Web.WebView2.Core.CoreWebView2? wv) => _coreWebView = wv;

    public async Task LoadSubtitle(string url)
    {
        var bvid = BilibiliApi.ExtractBvid(url);
        if (bvid == null) return;

        await StopActiveProvider();

        try
        {
            // Check cache first to avoid B站 API data inconsistency on repeated calls
            if (!_subtitleCache.TryGetValue(bvid, out var data))
            {
                data = await _bilibiliApi.GetSubtitle(url);
                if (data != null && data.Items.Count > 0)
                {
                    _subtitleCache[bvid] = data;
                    Log.Information("SubtitleService: fetched and cached {Count} items for bvid={Bvid}", data.Items.Count, bvid);
                }
            }
            else
            {
                Log.Information("SubtitleService: using cached {Count} items for bvid={Bvid}", data.Items.Count, bvid);
            }

            if (data != null && data.Items.Count > 0)
            {
                var ccProvider = new BilibiliCcProvider(_bilibiliApi);
                var started = await ccProvider.StartAsync(url, data);
                if (started)
                {
                    ccProvider.SubtitleChanged += OnProviderSubtitleChanged;
                    ccProvider.DirectionWordDetected += OnProviderDirectionDetected;
                    _activeProvider = ccProvider;
                    Log.Information("SubtitleService: API CC mode active ({Count} items)", data.Items.Count);
                    return;
                }
            }

            // Tier 2: DOM CC provider (reads CC text from WebView2 DOM in real-time)
            if (_coreWebView != null)
            {
                var domProvider = new DomCcProvider(_coreWebView);
                await domProvider.StartAsync(url);
                domProvider.SubtitleChanged += OnProviderSubtitleChanged;
                domProvider.DirectionWordDetected += OnProviderDirectionDetected;
                _activeProvider = domProvider;
                Log.Information("SubtitleService: DOM CC mode active (waiting for on-screen CC)");
                return;
            }

            // Tier 3: Speech recognition (last resort)
            try
            {
                _speechRecognition.SpeechRecognized += OnSpeechRecognized;
                await _speechRecognition.StartAsync();
                Log.Information("SubtitleService: Speech recognition mode active");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SubtitleService: Speech recognition unavailable");
                SubtitleChanged?.Invoke("（无可用字幕源 — 请手动开启视频CC字幕）");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubtitleService: LoadSubtitle failed for {Url}", url);
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

    /// <summary>Replace current provider with intercepted subtitle data (fetched from WebView2 fetch interceptor).</summary>
    public async Task ReplaceWithInterceptedAsync(string bvid, string jsonBody)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonBody);
            var body = doc.RootElement.GetProperty("body");
            var items = new List<SubtitleItem>();
            foreach (var item in body.EnumerateArray())
            {
                items.Add(new SubtitleItem
                {
                    From = item.GetProperty("from").GetDouble(),
                    To = item.GetProperty("to").GetDouble(),
                    Content = item.GetProperty("content").GetString() ?? ""
                });
            }
            var data = new SubtitleData { Items = items };
            _subtitleCache[bvid] = data;

            // Only replace if current provider is BilibiliCcProvider (API-based)
            if (_activeProvider is BilibiliCcProvider cc)
            {
                var started = await cc.StartAsync("", data);
                if (started)
                {
                    Log.Information("SubtitleService: replaced with intercepted subtitle for bvid={Bvid}, {Count} items", bvid, items.Count);
                }
            }
            else
            {
                // No active API provider yet — start one with intercepted data
                await StopActiveProvider();
                var ccProvider = new BilibiliCcProvider(_bilibiliApi);
                var started = await ccProvider.StartAsync("", data);
                if (started)
                {
                    ccProvider.SubtitleChanged += OnProviderSubtitleChanged;
                    ccProvider.DirectionWordDetected += OnProviderDirectionDetected;
                    _activeProvider = ccProvider;
                    Log.Information("SubtitleService: started API CC mode from intercepted data for bvid={Bvid}, {Count} items", bvid, items.Count);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubtitleService: failed to replace with intercepted subtitle for {Bvid}", bvid);
        }
    }

    private async Task StopActiveProvider()
    {
        if (_activeProvider != null)
        {
            _activeProvider.SubtitleChanged -= OnProviderSubtitleChanged;
            _activeProvider.DirectionWordDetected -= OnProviderDirectionDetected;
            await _activeProvider.StopAsync();
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
