using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace GuideAssistant.Services;

public class BilibiliApi
{
    private readonly HttpClient _http;

    public BilibiliApi()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com");
    }

    public async Task<SubtitleData?> GetSubtitle(string url)
    {
        try
        {
            // Extract aid or bvid from URL
            var bvid = ExtractBvid(url);
            if (bvid == null) return null;

            // Get video info
            var apiUrl = $"https://api.bilibili.com/x/web-interface/view?bvid={bvid}";
            var response = await _http.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(response);
            var data = doc.RootElement.GetProperty("data");
            var cid = data.GetProperty("cid").GetInt64();

            // Get player info (subtitles)
            var playerUrl = $"https://api.bilibili.com/x/player/v2?cid={cid}&bvid={bvid}";
            var playerResponse = await _http.GetStringAsync(playerUrl);
            using var playerDoc = JsonDocument.Parse(playerResponse);
            var subtitleJson = playerDoc.RootElement
                .GetProperty("data")
                .GetProperty("subtitle")
                .GetProperty("subtitles");

            if (subtitleJson.GetArrayLength() == 0) return null;

            // Prefer Chinese subtitle
            var subtitle = subtitleJson.EnumerateArray()
                .FirstOrDefault(s => s.GetProperty("lan").GetString() == "zh-CN" ||
                                     s.GetProperty("lan").GetString() == "zh-Hans" ||
                                     s.GetProperty("lan_doc").GetString()?.Contains("中文") == true);

            // Fallback to first
            if (subtitle.ValueKind == JsonValueKind.Undefined)
                subtitle = subtitleJson[0];

            var subtitleUrl = subtitle.GetProperty("subtitle_url").GetString()!;
            if (!subtitleUrl.StartsWith("http"))
                subtitleUrl = $"https:{subtitleUrl}";

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

            return new SubtitleData { Items = items };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get subtitle from {Url}", url);
            return null;
        }
    }

    private static string? ExtractBvid(string url)
    {
        if (url.Contains("bilibili.com/video/"))
        {
            var idx = url.IndexOf("bilibili.com/video/") + 19;
            var remaining = url[idx..];
            var slash = remaining.IndexOf('/');
            if (slash > 0) return remaining[..slash];
            var qmark = remaining.IndexOf('?');
            return qmark > 0 ? remaining[..qmark] : remaining;
        }
        return null;
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
