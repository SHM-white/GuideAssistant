using Serilog;

namespace GuideAssistant.Services;

public class SubtitleService
{
    private readonly BilibiliApi _bilibiliApi;
    private readonly SpeechRecognitionService _speechRecognition;
    private ISubtitleProvider? _activeProvider;
    private Action<string>? _activeProviderSubtitleChanged;
    private Action<string>? _activeProviderDirectionDetected;
    private Action<string>? _activeSpeechRecognized;
    private Microsoft.Web.WebView2.Core.CoreWebView2? _coreWebView;
    private double _currentTime;
    private long _subtitleSessionGeneration;
    private string? _currentBvid;
    private readonly Dictionary<string, SubtitleData> _subtitleCache = new();
    public event Action<string>? SubtitleChanged;
    public event Action<string>? DirectionWordDetected;

    private static readonly string[] s_directionWords = DirectionWords.All;
    /// <summary>Stores the most recent subtitle text from the active provider callback.</summary>
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

        var generation = System.Threading.Interlocked.Increment(ref _subtitleSessionGeneration);
        _currentBvid = bvid;

        try
        {
            DetachActiveSpeechRecognition();
            await StopSpeechRecognitionAsync();
            if (!IsCurrentSession(generation, bvid)) return;

            await StopActiveProvider();
            if (!IsCurrentSession(generation, bvid)) return;

            // Clear subtitle overlay after old sources are detached and stopped.
            _lastProviderSubtitleText = "";
            SubtitleChanged?.Invoke("");

            // Check cache first to avoid B站 API data inconsistency on repeated calls
            if (!_subtitleCache.TryGetValue(bvid, out SubtitleData? data))
            {
                try
                {
                    data = await _bilibiliApi.GetSubtitle(url);
                    if (!IsCurrentSession(generation, bvid)) return;
                    if (data != null && data.Items.Count > 0)
                    {
                        _subtitleCache[bvid] = data;
                        Log.Information("SubtitleService: fetched and cached {Count} items for bvid={Bvid}", data.Items.Count, bvid);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SubtitleService: failed to fetch API CC for {Url}", url);
                }
            }
            else
            {
                Log.Information("SubtitleService: using cached {Count} items for bvid={Bvid}", data.Items.Count, bvid);
            }
            if (!IsCurrentSession(generation, bvid)) return;

            if (data != null && data.Items.Count > 0)
            {
                try
                {
                    var ccProvider = new BilibiliCcProvider(_bilibiliApi);
                    var started = await ccProvider.StartAsync(url, data);
                    if (!IsCurrentSession(generation, bvid))
                    {
                        await ccProvider.StopAsync();
                        return;
                    }
                    if (started)
                    {
                        ActivateProvider(ccProvider, generation, bvid);
                        Log.Information("SubtitleService: API CC mode active ({Count} items)", data.Items.Count);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SubtitleService: API CC provider failed for {Url}", url);
                }
            }
            if (!IsCurrentSession(generation, bvid)) return;

            var domActive = false;
            // Tier 2: DOM CC provider (reads CC text from WebView2 DOM in real-time)
            if (_coreWebView != null)
            {
                try
                {
                    var domProvider = new DomCcProvider(_coreWebView);
                    var started = await domProvider.StartAsync(url);
                    if (!IsCurrentSession(generation, bvid))
                    {
                        await domProvider.StopAsync();
                        return;
                    }
                    if (started)
                    {
                        ActivateProvider(domProvider, generation, bvid);
                        domActive = true;
                        Log.Information("SubtitleService: DOM CC mode active (waiting for on-screen CC)");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SubtitleService: DOM CC provider failed for {Url}", url);
                }
            }
            if (!IsCurrentSession(generation, bvid)) return;

            // Tier 3: Speech recognition (last resort)
            Action<string>? speechHandler = null;
            try
            {
                speechHandler = AttachSpeechRecognition(generation, bvid);
                var speechStarted = await _speechRecognition.StartAsync();
                if (!IsCurrentSession(generation, bvid))
                {
                    DetachSpeechRecognition(speechHandler);
                    return;
                }
                if (speechStarted)
                {
                    Log.Information("SubtitleService: Speech recognition mode active");
                }
                else
                {
                    DetachActiveSpeechRecognition();
                    if (domActive)
                    {
                        Log.Warning("SubtitleService: Speech recognition unavailable, DOM CC remains active");
                    }
                    else
                    {
                        SubtitleChanged?.Invoke("（无可用字幕源 — 请手动开启视频CC字幕）");
                    }
                }
            }
            catch (Exception ex)
            {
                if (speechHandler != null)
                    DetachSpeechRecognition(speechHandler);
                if (!IsCurrentSession(generation, bvid)) return;
                if (domActive)
                {
                    Log.Warning(ex, "SubtitleService: Speech recognition unavailable while DOM CC remains active");
                }
                else
                {
                    Log.Warning(ex, "SubtitleService: Speech recognition unavailable");
                    SubtitleChanged?.Invoke("（无可用字幕源 — 请手动开启视频CC字幕）");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubtitleService: LoadSubtitle failed for {Url}", url);
        }
    }

    private bool IsCurrentSession(long generation, string bvid) => _subtitleSessionGeneration == generation && string.Equals(_currentBvid, bvid, StringComparison.Ordinal);

    private void ActivateProvider(ISubtitleProvider provider, long generation, string bvid)
    {
        Action<string> subtitleHandler = text =>
        {
            if (!ReferenceEquals(_activeProvider, provider) || !IsCurrentSession(generation, bvid)) return;
            _lastProviderSubtitleText = text;
            SubtitleChanged?.Invoke(text);
        };
        Action<string> directionHandler = word =>
        {
            if (!ReferenceEquals(_activeProvider, provider) || !IsCurrentSession(generation, bvid)) return;
            if (ShouldFireDirection(word, _lastProviderSubtitleText))
                DirectionWordDetected?.Invoke(word);
        };

        _activeProvider = provider;
        _activeProviderSubtitleChanged = subtitleHandler;
        _activeProviderDirectionDetected = directionHandler;
        provider.SubtitleChanged += subtitleHandler;
        provider.DirectionWordDetected += directionHandler;
    }

    private Action<string> AttachSpeechRecognition(long generation, string bvid)
    {
        Action<string>? handler = null;
        handler = text =>
        {
            if (!ReferenceEquals(_activeSpeechRecognized, handler) || !IsCurrentSession(generation, bvid)) return;
            OnSpeechRecognized(text);
        };

        _activeSpeechRecognized = handler;
        _speechRecognition.SpeechRecognized += handler;
        return handler;
    }

    private void DetachActiveSpeechRecognition()
    {
        if (_activeSpeechRecognized == null) return;
        DetachSpeechRecognition(_activeSpeechRecognized);
    }

    private void DetachSpeechRecognition(Action<string> handler)
    {
        _speechRecognition.SpeechRecognized -= handler;
        if (ReferenceEquals(_activeSpeechRecognized, handler))
            _activeSpeechRecognized = null;
    }

    public void OnSpeechRecognized(string text)
    {
        SubtitleChanged?.Invoke(text);
        CheckDirectionWords(text);
    }

    public void UpdateTime(double currentTime)
    {
        _currentTime = currentTime;
        if (_activeProvider != null)
        {
            _activeProvider.UpdateTime(currentTime);
        }
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

    /// <summary>Returns true if a direction arrow should be shown for <paramref name="fullText"/>.</summary>
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
        var generation = _subtitleSessionGeneration;
        if (!IsCurrentSession(generation, bvid)) return;

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
            if (!IsCurrentSession(generation, bvid)) return;

            _subtitleCache[bvid] = data;

            // Only replace if current provider is BilibiliCcProvider (API-based)
            if (_activeProvider is BilibiliCcProvider cc)
            {
                if (!IsCurrentSession(generation, bvid) || !ReferenceEquals(_activeProvider, cc)) return;
                var started = await cc.StartAsync("", data);
                if (!IsCurrentSession(generation, bvid) || !ReferenceEquals(_activeProvider, cc))
                {
                    await cc.StopAsync();
                    return;
                }
                if (started)
                {
                    Log.Information("SubtitleService: replaced with intercepted subtitle for bvid={Bvid}, {Count} items", bvid, items.Count);
                }
            }
            else
            {
                // No active API provider yet — start one with intercepted data
                DetachActiveSpeechRecognition();
                await StopSpeechRecognitionAsync();
                if (!IsCurrentSession(generation, bvid)) return;

                await StopActiveProvider();
                if (!IsCurrentSession(generation, bvid)) return;

                var ccProvider = new BilibiliCcProvider(_bilibiliApi);
                var started = await ccProvider.StartAsync("", data);
                if (!IsCurrentSession(generation, bvid))
                {
                    await ccProvider.StopAsync();
                    return;
                }
                if (started)
                {
                    ActivateProvider(ccProvider, generation, bvid);
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
        var provider = _activeProvider;
        var subtitleHandler = _activeProviderSubtitleChanged;
        var directionHandler = _activeProviderDirectionDetected;

        _activeProvider = null;
        _activeProviderSubtitleChanged = null;
        _activeProviderDirectionDetected = null;
        _lastProviderSubtitleText = "";

        if (provider != null)
        {
            if (subtitleHandler != null)
                provider.SubtitleChanged -= subtitleHandler;
            if (directionHandler != null)
                provider.DirectionWordDetected -= directionHandler;
            await provider.StopAsync();
        }
    }

    private async Task StopSpeechRecognitionAsync()
    {
        try
        {
            await _speechRecognition.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SubtitleService: failed to stop existing speech session");
        }
    }

    public void Dispose()
    {
        DetachActiveSpeechRecognition();
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
