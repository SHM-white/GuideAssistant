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

    private static readonly string[] s_directionWords = DirectionWords.All;
    /// <summary>
    /// Stores the most recent subtitle text from the active provider.
    /// Marked volatile so that if provider events ever fire on a different
    /// thread, the cached text read by <see cref="ShouldFireDirection"/>
    /// is always the latest write from <see cref="OnProviderSubtitleChanged"/>.
    /// </summary>
    private volatile string _lastProviderSubtitleText = "";

    /// <summary>
    /// When ON (default), direction arrows only appear when "小地图" or "地图" is
    /// mentioned in the same subtitle text, and "大地图" is suppressed.
    /// When OFF, all direction keywords trigger arrows unconditionally.
    /// Toggle via settings UI or hotkey (default: B key).
    /// </summary>
    public bool MinimapFilterEnabled { get; set; } = true;

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

        // Immediately clear subtitle overlay when switching videos
        SubtitleChanged?.Invoke("");
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
        _lastProviderSubtitleText = text;
        SubtitleChanged?.Invoke(text);
    }

    private void OnProviderDirectionDetected(string word)
    {
        if (ShouldFireDirection(word, _lastProviderSubtitleText))
            DirectionWordDetected?.Invoke(word);
    }

    private void CheckDirectionWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var word in s_directionWords)
        {
            if (text.Contains(word))
            {
                if (ShouldFireDirection(word, text))
                    DirectionWordDetected?.Invoke(word);
                return;
            }
        }
    }

    /// <summary>
    /// Returns true if a direction arrow should be shown for <paramref name="fullText"/>.
    /// Rules (checked in order):
    ///   1. Null/empty text → suppress (return false).
    ///   2. Filter ON:
    ///      a. "大地图" in text → suppress (return false).
    ///      b. Require "小地图" or "地图" in text.
    ///      NOTE: We match the broader "地图" (not just "小地图") so that phrases like
    ///      "地图左下角" (without literal "小地图") still trigger. This is a deliberate
    ///      pragmatic choice — game guide subtitles often omit "小" before "地图".
    ///   3. Filter OFF → allow all (including "大地图" text, for user override).
    /// </summary>
    private bool ShouldFireDirection(string word, string fullText)
    {
        if (string.IsNullOrEmpty(fullText)) return false;

        if (MinimapFilterEnabled)
        {
            // Filter ON: suppress 大地图, require 小地图/地图
            if (fullText.Contains("大地图"))
                return false;
            return fullText.Contains("小地图") || fullText.Contains("地图");
        }

        // Filter OFF: show all direction keywords (user override for 大地图 etc.)
        return true;
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
        // Use shared AngleMap — longest-keyword-first to ensure compound
        // terms (西南方向) match before shorter substrings (西).
        foreach (var kvp in DirectionWords.AngleMap)
        {
            if (text.Contains(kvp.Key))
                return kvp.Value;
        }
        return null;
    }
}
