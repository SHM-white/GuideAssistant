namespace GuideAssistant.Services;

/// <summary>Produces Bilibili timestamp links without requiring a schema change.</summary>
public static class BilibiliMomentLink
{
    public static bool IsVideoUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return (uri.Host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase)) &&
               uri.AbsolutePath.StartsWith("/video/", StringComparison.OrdinalIgnoreCase) &&
               BilibiliApi.ExtractBvid(uri.AbsolutePath) != null;
    }

    public static bool TryCreate(string? url, double position, out string result, out int seconds)
    {
        result = string.Empty;
        seconds = 0;
        if (!IsVideoUrl(url) || !double.IsFinite(position) || position < 0 || position > int.MaxValue)
            return false;

        seconds = (int)Math.Floor(position);
        var builder = new UriBuilder(url!);
        // Preserve p= (multi-part video index) and any other existing query parameters.
        var parts = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.Split('=')[0].Equals("t", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Add($"t={seconds}");
        builder.Query = string.Join("&", parts);
        result = builder.Uri.AbsoluteUri;
        return true;
    }

    public static string FormatTime(int totalSeconds)
        => $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}";
}
