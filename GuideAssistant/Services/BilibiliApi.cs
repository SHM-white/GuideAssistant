using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace GuideAssistant.Services;

public class BilibiliApi
{
    private readonly HttpClient _http;
    private string? _cookieHeader;

    // Intercepted subtitle data from WebView2 fetch interceptor (keyed by bvid)
    private static readonly Dictionary<string, SubtitleData> InterceptedCache = new();

    public BilibiliApi()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com");
        _http.DefaultRequestHeaders.Add("Origin", "https://www.bilibili.com");
    }

    /// <summary>Set cookies extracted from WebView2 for API authentication.</summary>
    public void SetCookies(string cookies)
    {
        if (string.IsNullOrEmpty(cookies)) return;
        _cookieHeader = cookies;
        _http.DefaultRequestHeaders.Remove("Cookie");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookies);
        Log.Information("BilibiliApi: cookies set (length={Len})", cookies.Length);
    }

    /// <summary>Store subtitle data intercepted from WebView2 fetch/XHR.</summary>
    public static void CacheInterceptedSubtitle(string bvid, string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
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
            InterceptedCache[bvid] = new SubtitleData { Items = items };
            Log.Information("BilibiliApi: intercepted subtitle cached for bvid={Bvid}, {Count} items", bvid, items.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BilibiliApi: failed to parse intercepted subtitle for {Bvid}", bvid);
        }
    }

    public async Task<SubtitleData?> GetSubtitle(string url)
    {
        try
        {
            var bvid = ExtractBvid(url);
            if (bvid == null) return null;

            // Check intercepted cache first (data captured from WebView2 fetch)
            lock (InterceptedCache)
            {
                if (InterceptedCache.TryGetValue(bvid, out var cached))
                {
                    Log.Information("BilibiliApi: using intercepted subtitle for bvid={Bvid}, {Count} items", bvid, cached.Items.Count);
                    return cached;
                }
            }

            // Get video info
            var apiUrl = $"https://api.bilibili.com/x/web-interface/view?bvid={bvid}";
            var response = await _http.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");
            if (!data.TryGetProperty("cid", out var cidEl))
            {
                Log.Warning("BilibiliApi: no cid in response for {Bvid}", bvid);
                return null;
            }
            var cid = cidEl.GetInt64();
            var aid = data.TryGetProperty("aid", out var aidEl) ? aidEl.GetInt64() : 0L;
            Log.Information("BilibiliApi: bvid={Bvid} aid={Aid} cid={Cid}", bvid, aid, cid);

            // Get player info (subtitles) — use wbi/v2 endpoint which is more stable
            var playerUrl = $"https://api.bilibili.com/x/player/wbi/v2?aid={aid}&cid={cid}";
            var playerResponse = await _http.GetStringAsync(playerUrl);
            using var playerDoc = JsonDocument.Parse(playerResponse);
            var playerData = playerDoc.RootElement.GetProperty("data");

            if (!playerData.TryGetProperty("subtitle", out var subtitleWrap))
            {
                Log.Debug("BilibiliApi: no subtitle field in player response for {Bvid}", bvid);
                return null;
            }
            if (!subtitleWrap.TryGetProperty("subtitles", out var subtitleJson))
            {
                Log.Debug("BilibiliApi: no subtitles array in response for {Bvid}", bvid);
                return null;
            }

            Log.Debug("BilibiliApi: subtitle candidate languages: {Langs}",
                string.Join(",", subtitleJson.EnumerateArray().Select(s =>
                {
                    try { return s.GetProperty("lan").GetString() ?? "?"; }
                    catch { return "?"; }
                })));

            if (subtitleJson.GetArrayLength() == 0) return null;

            // Dump subtitle candidates for debugging
            foreach (var sub in subtitleJson.EnumerateArray())
            {
                try
                {
                    var raw = sub.GetRawText();
                    Log.Information("BilibiliApi: subtitle candidate raw={Raw}", raw);
                }
                catch { }
            }

            // Prefer Chinese subtitle (including AI-generated)
            var subtitle = subtitleJson.EnumerateArray()
                .FirstOrDefault(s =>
                {
                    var lan = s.GetProperty("lan").GetString();
                    return lan == "zh-CN" || lan == "zh-Hans" || lan == "ai-zh" ||
                           (s.TryGetProperty("lan_doc", out var ld) &&
                            (ld.GetString()?.Contains("中文") == true || ld.GetString()?.Contains("AI") == true));
                });

            // Fallback to first
            if (subtitle.ValueKind == JsonValueKind.Undefined)
                subtitle = subtitleJson[0];

            // Try subtitle_url first, fallback to url field
            var subtitleUrl = "";
            if (subtitle.TryGetProperty("subtitle_url", out var surl) && !string.IsNullOrWhiteSpace(surl.GetString()))
            {
                subtitleUrl = surl.GetString()!;
            }
            else if (subtitle.TryGetProperty("url", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
            {
                subtitleUrl = u.GetString()!;
            }

            if (string.IsNullOrWhiteSpace(subtitleUrl))
            {
                Log.Warning("BilibiliApi: subtitle URL is empty for bvid={Bvid}", bvid);
                return null;
            }

            if (!subtitleUrl.StartsWith("http"))
                subtitleUrl = $"https:{subtitleUrl}";

            var lan = subtitle.GetProperty("lan").GetString() ?? "?";
            Log.Information("BilibiliApi: fetching subtitle lang={Lang} url={Url}", lan, subtitleUrl);

            var subtitleContent = await _http.GetStringAsync(subtitleUrl);
            using var subDoc = JsonDocument.Parse(subtitleContent);
            var body = subDoc.RootElement.GetProperty("body");

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

            // Log first 5 items for debugging
            var preview = items.Take(5).Select(i => $"[{i.From:F1}s] {i.Content}");
            Log.Information("BilibiliApi: {Count} subtitle items loaded, preview: {Preview}",
                items.Count, string.Join(" | ", preview));

            return new SubtitleData { Items = items };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get subtitle from {Url}", url);
            return null;
        }
    }

    public static string? ExtractBvid(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"BV[a-zA-Z0-9_]{8,}");
        return match.Success ? match.Value : null;
    }
}

public class SubtitleData
{
    public List<SubtitleItem> Items { get; set; } = new();
}

public class SubtitleItem
{
    public double From { get; set; }
    public double To { get; set; }
    public string Content { get; set; } = string.Empty;
}
